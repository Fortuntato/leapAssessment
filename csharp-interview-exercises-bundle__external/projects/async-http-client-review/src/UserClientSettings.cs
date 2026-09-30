namespace HttpClientReview;

public sealed class UserClientSettings
{
    /// <summary>Number of retries after the first attempt (0 disables retries).</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Fixed delay between retries (a server Retry-After header takes precedence).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Timeout applied to each individual attempt.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
