namespace Newsdata.Api.Exceptions;

/// <summary>The API returned a structured error response.</summary>
public class NewsdataApiException : NewsdataException
{
    /// <summary>HTTP status returned by the API.</summary>
    public int StatusCode { get; }

    /// <summary>Raw JSON response body, when available.</summary>
    public string? ResponseBody { get; }

    public NewsdataApiException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
