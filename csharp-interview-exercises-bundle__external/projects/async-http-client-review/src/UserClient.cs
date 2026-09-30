using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HttpClientReview;

public class UserClient
{
    private readonly HttpClient _http;
    private readonly UserClientSettings _options;

    public UserClient(HttpClient http, UserClientSettings? options = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? new UserClientSettings();
    }

    /// <summary>
    /// Returns the user's name, or null if the user does not exist (404).
    /// Transient failures (network errors, timeouts, 408, 429, 5xx) are retried with a fixed delay.
    /// </summary>
    public async Task<string?> GetUserName(int id, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var isLastAttempt = attempt >= _options.MaxRetries;
            TimeSpan? retryAfter = null;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(_options.AttemptTimeout);

            try
            {
                using var req = CreateGetUserRequest(id);

                using var resp = await _http
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (resp.IsSuccessStatusCode)
                {
                    return await ReadUserNameAsync(resp.Content, attemptCts.Token).ConfigureAwait(false);
                }

                if (isLastAttempt || !IsTransient(resp.StatusCode))
                {
                    throw await CreateHttpErrorAsync(req, resp, attemptCts.Token).ConfigureAwait(false);
                }

                retryAfter = resp.Headers.RetryAfter?.Delta;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null && !isLastAttempt)
            {
                // Retry after network-level failure
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (isLastAttempt)
                {
                    throw new TimeoutException(
                        $"GET api/users/{id} timed out after {_options.AttemptTimeout.TotalSeconds}s.", ex);
                }
            }

            await Task.Delay(GetRetryDelay(retryAfter), ct).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private TimeSpan GetRetryDelay(TimeSpan? retryAfter)
    {
        if (retryAfter is { } serverDelay)
        {
            return serverDelay;
        }

        return _options.BaseDelay;
    }

    private static HttpRequestMessage CreateGetUserRequest(int id)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"api/users/{id}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private static async Task<HttpRequestException> CreateHttpErrorAsync(
        HttpRequestMessage req, HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var uri = resp.RequestMessage?.RequestUri ?? req.RequestUri;

        return new HttpRequestException(
            $"GET {uri} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}",
            inner: null,
            statusCode: resp.StatusCode);
    }

    private static async Task<string?> ReadUserNameAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
    }
}
