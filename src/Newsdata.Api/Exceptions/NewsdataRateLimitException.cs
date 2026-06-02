namespace Newsdata.Api.Exceptions;

/// <summary>
/// Thrown on 429 responses once retries are exhausted. <see cref="RetryAfter"/>
/// holds the value parsed from the <c>Retry-After</c> header, or 0 when the
/// header was missing or unparseable.
/// </summary>
public sealed class NewsdataRateLimitException : NewsdataApiException
{
    /// <summary>Seconds to wait before retrying, or 0 when not provided.</summary>
    public int RetryAfter { get; }

    public NewsdataRateLimitException(string message, int statusCode, string? responseBody, int retryAfter)
        : base(message, statusCode, responseBody)
    {
        RetryAfter = retryAfter;
    }
}
