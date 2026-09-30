namespace BankTransfer;

public sealed class TransferService : ITransferService
{
    private readonly IAccountStore _accounts;
    private readonly IIdempotencyStore _idempotency;
    private readonly IAccountLockProvider _locks;

    public TransferService(IAccountStore accounts, IIdempotencyStore idempotency) :
        this(accounts, idempotency, InMemoryAccountLockProvider.Shared)
    { }

    public TransferService(IAccountStore accounts, IIdempotencyStore idempotency, IAccountLockProvider locks)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
    }

    /// <summary>
    /// Transfers funds from one account to another as a single idempotent operation identified by the request ID.
    /// </summary>
    /// <remarks>Acquires account-level locks before executing the transfer to prevent concurrent double-spend
    /// scenarios.</remarks>
    /// <param name="fromId">Id of the account to debit.</param>
    /// <param name="toId">Id of the account to credit.</param>
    /// <param name="amount">Amount to transfer.</param>
    /// <param name="requestId">Idempotency key used to detect and prevent duplicate transfer requests.</param>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous transfer operation.</returns>
    public async Task TransferAsync(Guid fromId, Guid toId, decimal amount, string requestId, CancellationToken ct = default)
    {
        ValidateTransferParameters(fromId, toId, amount, requestId);

        await using (await _locks.AcquireAsync(fromId, toId, ct).ConfigureAwait(false))
        {
            await ExecuteTransferAsync(fromId, toId, amount, requestId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validates account transfer input values
    /// </summary>
    /// <param name="fromId">Id of the source account. Must not be Guid.Empty and must differ from toId.</param>
    /// <param name="toId">Id of the destination account. Must not be Guid.Empty and must differ from fromId.</param>
    /// <param name="amount">Transfer amount. Must be greater than zero.</param>
    /// <param name="requestId">Request identifier used for correlation. Must not be null, empty, or whitespace.</param>
    private static void ValidateTransferParameters(Guid fromId, Guid toId, decimal amount, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId is required.", nameof(requestId));
        if (fromId == Guid.Empty)
            throw new ArgumentException("Source account id is required.", nameof(fromId));
        if (toId == Guid.Empty)
            throw new ArgumentException("Destination account id is required.", nameof(toId));
        if (fromId == toId)
            throw new ArgumentException("Source and destination accounts must be different.", nameof(toId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be greater than zero.");
    }

    /// <summary>
    /// Function to execute the transfer of funds.
    /// </summary>
    /// <param name="fromId">Id of the account to debit.</param>
    /// <param name="toId">Id of the account to credit.</param>
    /// <param name="amount">Amount to transfer.</param>
    /// <param name="requestId">Idempotency key used to prevent processing the same transfer more than once.</param>
    /// <param name="ct">Cancellation token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous transfer operation.</returns>
    private async Task ExecuteTransferAsync(Guid fromId, Guid toId, decimal amount, string requestId, CancellationToken ct)
    {
        Account source = await GetAccountById(fromId, ct).ConfigureAwait(false);
        Account destination = await GetAccountById(toId, ct).ConfigureAwait(false);

        // Idempotency is checked before the balance so a repeated request is a no-op
        // even if the balance has changed since the original transfer.
        if (!await _idempotency.TryRecordAsync(requestId, ct).ConfigureAwait(false))
            return;

        CheckBalanceForTransfer(source, amount);

        Account debited = source with { Balance = source.Balance - amount };
        Account credited = destination with { Balance = destination.Balance + amount };

        await _accounts.UpdateAsync(debited, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _accounts.UpdateAsync(credited, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Compensate to keep the transfer atomic.
            await _accounts.UpdateAsync(source, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<Account> GetAccountById(Guid accountId, CancellationToken ct)
        => await _accounts.GetAsync(accountId, ct).ConfigureAwait(false)
           ?? throw new AccountNotFoundException(accountId);

    private static void CheckBalanceForTransfer(Account source, decimal amount)
    {
        if (source.Balance < amount)
            throw new InsufficientFundsException(source.Id, source.Balance, amount);
    }
}