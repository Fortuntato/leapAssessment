using System.Net;
using System.Text.Json;

namespace HttpClientReview;

// Intentionally flawed code for refactoring
public class UserClient
{
    private readonly HttpClient _http;

    public UserClient(HttpClient http)
    {
        _http = http ?? new HttpClient();
    }

    public async Task<string?> GetUserName(int id, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"api/users/{id}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        var resp = _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).Result;

        if (!resp.IsSuccessStatusCode)
        {
            var body = resp.Content.ReadAsStringAsync().Result;

            throw new HttpRequestException(
                $"GET {req.RequestUri} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        }

        var json = resp.Content.ReadAsStringAsync().Result;

        var name = JsonDocument.Parse(json).RootElement.GetString();

        return await Task.FromResult(name);
    }
}
