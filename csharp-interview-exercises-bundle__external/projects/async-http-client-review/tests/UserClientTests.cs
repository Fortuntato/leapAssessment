using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HttpClientReview;
using Xunit;

namespace HttpClientExample.Tests;

public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
    public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request, cancellationToken));
}

public class UserClientTests
{
    [Fact]
    public async Task Returns_Name_On_200()
    {
        var handler = new FakeHandler((req, ct) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://api.test/api/users/1", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":1,\"name\":\"Leap\"}")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new UserClient(http);

        var name = await client.GetUserName(1);
        Assert.Equal("Leap", name);
    }

    [Fact]
    public async Task Returns_Null_On_404()
    {
        var handler = new FakeHandler((req, ct) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new UserClient(http);

        var name = await client.GetUserName(42);
        Assert.Null(name);
    }

    [Fact]
    public async Task Throws_On_500_Including_StatusCode()
    {
        var handler = new FakeHandler((req, ct) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{'error':'leap was too big'}")
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new UserClient(http);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserName(2));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("GET https://api.test/api/users/2 failed", ex.Message);
    }
}

public class UserClientResilienceTests
{
    private static readonly UserClientSettings FastOptions = new()
    {
        MaxRetries = 2,
        BaseDelay = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromMilliseconds(200)
    };

    private static UserClient CreateClient(HttpMessageHandler handler, UserClientSettings options) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }, options);

    [Fact]
    public async Task Retries_Transient_Error_Then_Succeeds()
    {
        var calls = 0;
        var handler = new FakeHandler((req, ct) => ++calls < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"name\":\"Leap\"}") });

        var name = await CreateClient(handler, FastOptions).GetUserName(1);

        Assert.Equal("Leap", name);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Throws_After_Retries_Exhausted()
    {
        var calls = 0;
        var handler = new FakeHandler((req, ct) => { calls++; return new HttpResponseMessage(HttpStatusCode.InternalServerError); });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(handler, FastOptions).GetUserName(1));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Does_Not_Retry_Non_Transient_Error()
    {
        var calls = 0;
        var handler = new FakeHandler((req, ct) => { calls++; return new HttpResponseMessage(HttpStatusCode.BadRequest); });

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(handler, FastOptions).GetUserName(1));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Throws_TimeoutException_When_Every_Attempt_Times_Out()
    {
        var handler = new SlowHandler();

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(handler, FastOptions).GetUserName(1));

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Caller_Cancellation_Is_Not_Retried()
    {
        var handler = new SlowHandler();
        var options = new UserClientSettings { MaxRetries = 2, BaseDelay = TimeSpan.Zero, AttemptTimeout = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler, options).GetUserName(1, cts.Token));

        Assert.Equal(1, handler.Calls);
    }
}

public sealed class SlowHandler : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
