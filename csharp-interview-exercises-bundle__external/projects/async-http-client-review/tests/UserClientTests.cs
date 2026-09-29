using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HttpClientExample;
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
                Content = new StringContent("{\"id\":1,\"name\":\"Alex\"}")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new UserClient(http);

        var name = await client.GetUserName(1);
        Assert.Equal("Alex", name);
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
            Content = new StringContent("{'error':'boom'}")
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new UserClient(http);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserName(2));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("GET https://api.test/api/users/2 failed", ex.Message);
    }
}
