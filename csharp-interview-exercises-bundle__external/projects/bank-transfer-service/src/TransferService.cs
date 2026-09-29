using System.Collections.Concurrent;

namespace BankTransfer;

public sealed class TransferService : ITransferService
{
    private readonly IAccountStore _accounts;
    private readonly IIdempotencyStore _idempotency;
    private static readonly ConcurrentDictionary<Guid, object> _locks = new();

    public TransferService(IAccountStore accounts, IIdempotencyStore idempotency)
    {
        _accounts = accounts;
        _idempotency = idempotency;
    }

    public async Task TransferAsync(Guid fromId, Guid toId, decimal amount, string requestId, CancellationToken ct = default)
    {
        // TODO: Implement:
        // - Validate inputs    
        // - Ensure idempotency using requestId
        // - Move funds atomically from 'fromId' to 'toId'
        // - Prevent double-spend with concurrent calls
        // - Throw for insufficient funds / missing accounts
        
    }
}