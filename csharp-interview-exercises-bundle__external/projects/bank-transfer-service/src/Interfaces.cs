namespace BankTransfer;

public sealed record Account(Guid Id, decimal Balance);

public interface IAccountStore
{
    Task<Account?> GetAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(Account account, CancellationToken ct);
}

public interface IIdempotencyStore
{
    /// <summary>Returns true if this is the first time this requestId is seen.</summary>
    Task<bool> TryRecordAsync(string requestId, CancellationToken ct);
}

public interface ITransferService
{
    Task TransferAsync(Guid fromId, Guid toId, decimal amount, string requestId, CancellationToken ct = default);
}