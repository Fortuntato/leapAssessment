namespace BankTransfer;

public interface IAccountLockProvider
{
    /// <summary>
    /// Acquires exclusive locks on both accounts. Dispose the result to release them.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(Guid firstAccountId, Guid secondAccountId, CancellationToken ct);
}
