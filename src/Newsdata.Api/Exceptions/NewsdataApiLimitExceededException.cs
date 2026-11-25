namespace Newsdata.Api.Exceptions;

/// <summary>
/// Thrown on 429 when the account's pool of API credits is exhausted, rather
/// than a transient rate limit having been hit.
///
/// <para>Never retried — waiting out the backoff cannot conjure more credits.
/// Derives from <see cref="NewsdataRateLimitException"/>, so an existing
/// <c>catch</c> on that type keeps working.</para>
/// </summary>
public sealed class NewsdataApiLimitExceededException : NewsdataRateLimitException
{
    public NewsdataApiLimitExceededException(
        string message, int statusCode, string? responseBody, int retryAfter)
        : base(message, statusCode, responseBody, retryAfter)
    {
    }
}
