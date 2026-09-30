using System.Collections.Concurrent;

namespace BankTransfer;

/// <summary>
/// Per-account async locks for a single process.
/// </summary>
public sealed class InMemoryAccountLockProvider : IAccountLockProvider
{
    /// <summary>Process-wide instance so all services without an explicit provider share the same locks.</summary>
    public static InMemoryAccountLockProvider Shared { get; } = new();

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(Guid firstAccountId, Guid secondAccountId, CancellationToken ct)
    {
        // Acquire locks in a consistent order to avoid deadlocks between opposing transfers.
        var (firstId, secondId) = firstAccountId.CompareTo(secondAccountId) < 0
            ? (firstAccountId, secondAccountId)
            : (secondAccountId, firstAccountId);
        var firstLock = _locks.GetOrAdd(firstId, _ => new SemaphoreSlim(1, 1));
        var secondLock = _locks.GetOrAdd(secondId, _ => new SemaphoreSlim(1, 1));

        await firstLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await secondLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            firstLock.Release();
            throw;
        }

        return new Releaser(firstLock, secondLock);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private SemaphoreSlim? _first;
        private SemaphoreSlim? _second;

        public Releaser(SemaphoreSlim first, SemaphoreSlim second)
        {
            _first = first;
            _second = second;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _second, null)?.Release();
            Interlocked.Exchange(ref _first, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
