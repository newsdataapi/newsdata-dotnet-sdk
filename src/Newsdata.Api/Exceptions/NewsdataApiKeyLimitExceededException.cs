namespace Newsdata.Api.Exceptions;

/// <summary>
/// Thrown on 429 when this specific API key's own credit cap is exhausted,
/// even though the account's overall pool may still have credits left.
///
/// <para>Never retried — waiting out the backoff cannot conjure more credits.
/// Derives from <see cref="NewsdataRateLimitException"/>, so an existing
/// <c>catch</c> on that type keeps working.</para>
/// </summary>
public sealed class NewsdataApiKeyLimitExceededException : NewsdataRateLimitException
{
    public NewsdataApiKeyLimitExceededException(
        string message, int statusCode, string? responseBody, int retryAfter)
        : base(message, statusCode, responseBody, retryAfter)
    {
    }
}
