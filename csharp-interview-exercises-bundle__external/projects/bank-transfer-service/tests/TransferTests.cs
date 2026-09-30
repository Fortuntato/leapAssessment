using System;
using System.Linq;
using System.Threading.Tasks;
using BankTransfer;
using Xunit;

public class TransferTests
{
    [Fact]
    public async Task Transfer_MovesMoney_Once_WithIdempotency()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var store = new InMemoryAccountStore(new[]
        {
            new Account(a, 100m),
            new Account(b, 0m)
        });
        var idem = new InMemoryIdempotencyStore();
        var svc = new TransferService(store, idem);

        var requestId = Guid.NewGuid().ToString();
        await svc.TransferAsync(a, b, 10m, requestId);
        await svc.TransferAsync(a, b, 10m, requestId); // same id, should no-op

        var from = await store.GetAsync(a, default);
        var to = await store.GetAsync(b, default);

        Assert.Equal(90m, from!.Balance);
        Assert.Equal(10m, to!.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Transfer_RejectsNonPositiveAmount(decimal amount)
    {
        var (svc, a, b, _) = Setup(100m);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.TransferAsync(a, b, amount, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34"));
    }

    [Fact]
    public async Task Transfer_RejectsSameAccount()
    {
        var (svc, a, _, _) = Setup(100m);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.TransferAsync(a, a, 10m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34"));
    }

    [Fact]
    public async Task Transfer_ThrowsForMissingAccount()
    {
        var (svc, a, _, _) = Setup(100m);
        await Assert.ThrowsAsync<AccountNotFoundException>(() => svc.TransferAsync(a, Guid.NewGuid(), 10m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34"));
    }

    [Fact]
    public async Task Transfer_ThrowsForInsufficientFunds()
    {
        var (svc, a, b, _) = Setup(5m);
        await Assert.ThrowsAsync<InsufficientFundsException>(() => svc.TransferAsync(a, b, 10m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34"));
    }

    [Fact]
    public async Task Transfer_DuplicateRequest_IsNoOp_EvenWhenBalanceIsNowZero()
    {
        var (svc, a, b, store) = Setup(10m);
        await svc.TransferAsync(a, b, 10m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34");
        await svc.TransferAsync(a, b, 10m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34");
        Assert.Equal(0m, (await store.GetAsync(a, default))!.Balance);
        Assert.Equal(10m, (await store.GetAsync(b, default))!.Balance);
    }

    [Fact]
    public async Task Transfer_ConcurrentCalls_DoNotDoubleSpend()
    {
        var (svc, a, b, store) = Setup(100m);

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(async () =>
            {
                try { await svc.TransferAsync(a, b, 10m, $"request-{i}"); return true; }
                catch (InvalidOperationException) { return false; }
            }));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(ok => ok));
        Assert.Equal(0m, (await store.GetAsync(a, default))!.Balance);
        Assert.Equal(100m, (await store.GetAsync(b, default))!.Balance);
    }

    [Fact]
    public async Task Transfer_ConcurrentSameRequestId_ExecutesOnce()
    {
        var (svc, a, b, store) = Setup(100m);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => svc.TransferAsync(a, b, 10m, "same"))));
        Assert.Equal(90m, (await store.GetAsync(a, default))!.Balance);
        Assert.Equal(10m, (await store.GetAsync(b, default))!.Balance);
    }

    private static (TransferService svc, Guid a, Guid b, InMemoryAccountStore store) Setup(decimal balanceA)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var store = new InMemoryAccountStore(new[] { new Account(a, balanceA), new Account(b, 0m) });
        return (new TransferService(store, new InMemoryIdempotencyStore()), a, b, store);
    }

    [Fact]
    public async Task Transfer_WhenCreditFails_RestoresSourceBalance()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var inner = new InMemoryAccountStore(new[] { new Account(a, 100m), new Account(b, 0m) });
        var store = new FailingUpdateAccountStore(inner, failForAccountId: b);
        var svc = new TransferService(store, new InMemoryIdempotencyStore());

        // The storage error reaches the caller unchanged.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TransferAsync(a, b, 30m, "7f3c2a9e-4b1d-4e8a-9c6f-2d5b8e1a0c34"));
        Assert.Equal(FailingUpdateAccountStore.FailureMessage, ex.Message);

        // Neither balance changed: the debit was undone after the credit failed.
        Assert.Equal(100m, (await inner.GetAsync(a, default))!.Balance);
        Assert.Equal(0m, (await inner.GetAsync(b, default))!.Balance);
    }

    /// <summary>Wraps a real store and throws when updating one specific account.</summary>
    private sealed class FailingUpdateAccountStore : IAccountStore
    {
        public const string FailureMessage = "Simulated storage failure.";

        private readonly IAccountStore _inner;
        private readonly Guid _failForAccountId;

        public FailingUpdateAccountStore(IAccountStore inner, Guid failForAccountId)
        {
            _inner = inner;
            _failForAccountId = failForAccountId;
        }

        public Task<Account?> GetAsync(Guid id, System.Threading.CancellationToken ct)
            => _inner.GetAsync(id, ct);

        public Task UpdateAsync(Account account, System.Threading.CancellationToken ct)
            => account.Id == _failForAccountId
                ? throw new InvalidOperationException(FailureMessage)
                : _inner.UpdateAsync(account, ct);
    }
}