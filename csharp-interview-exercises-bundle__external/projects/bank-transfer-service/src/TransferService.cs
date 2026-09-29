using System.Collections.Concurrent;

namespace BankTransfer;

public sealed class TransferService : ITransferService
{
    private readonly IAccountStore _accounts;
    private readonly IIdempotencyStore _idempotency;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public TransferService(IAccountStore accounts, IIdempotencyStore idempotency)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
    }

    // TODO: Implement:
    // - Validate inputs    
    // - Ensure idempotency using requestId
    // - Move funds atomically from 'fromId' to 'toId'
    // - Prevent double-spend with concurrent calls
    // - Throw for insufficient funds / missing accounts
    public async Task TransferAsync(Guid fromId, Guid toId, decimal amount, string requestId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId is required.", nameof(requestId));
        if (fromId == Guid.Empty)
            throw new ArgumentException("Source account id is required.", nameof(fromId));
        if (toId == Guid.Empty)
            throw new ArgumentException("Destination account id is required.", nameof(toId));
        if (fromId == toId)
            throw new ArgumentException("Source and destination accounts must differ.", nameof(toId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be greater than zero.");

        // Acquire locks in a consistent order to avoid deadlocks between opposing transfers.
        var firstId = fromId.CompareTo(toId) < 0 ? fromId : toId;
        var secondId = firstId == fromId ? toId : fromId;
        var firstLock = _locks.GetOrAdd(firstId, _ => new SemaphoreSlim(1, 1));
        var secondLock = _locks.GetOrAdd(secondId, _ => new SemaphoreSlim(1, 1));

        await firstLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await secondLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var source = await _accounts.GetAsync(fromId, ct).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Account '{fromId}' not found.");
                var destination = await _accounts.GetAsync(toId, ct).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Account '{toId}' not found.");

                // Idempotency is checked before the balance so a repeated request is a no-op
                // even if the balance has changed since the original transfer.
                if (!await _idempotency.TryRecordAsync(requestId, ct).ConfigureAwait(false))
                    return;

                if (source.Balance < amount)
                    throw new InvalidOperationException($"Insufficient funds in account '{fromId}'.");

                var debited = source with { Balance = source.Balance - amount };
                var credited = destination with { Balance = destination.Balance + amount };

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
            finally
            {
                secondLock.Release();
            }
        }
        finally
        {
            firstLock.Release();
        }
    }
}