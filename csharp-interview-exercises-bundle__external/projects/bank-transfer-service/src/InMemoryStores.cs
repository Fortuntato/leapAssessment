using System.Collections.Concurrent;

namespace BankTransfer;

// Minimal test doubles for use in unit tests / local runs.
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly ConcurrentDictionary<Guid, Account> _db = new();
    public InMemoryAccountStore(IEnumerable<Account>? seed = null)
    {
        if (seed != null) foreach (var a in seed) _db[a.Id] = a;
    }
    public Task<Account?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_db.TryGetValue(id, out var a) ? a : null);
    public Task UpdateAsync(Account account, CancellationToken ct)
    { _db[account.Id] = account; return Task.CompletedTask; }
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    public Task<bool> TryRecordAsync(string requestId, CancellationToken ct)
        => Task.FromResult(_seen.TryAdd(requestId, 1));
}