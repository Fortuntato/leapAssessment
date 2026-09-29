using System;
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
}