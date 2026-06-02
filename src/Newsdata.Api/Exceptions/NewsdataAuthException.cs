namespace Newsdata.Api.Exceptions;

/// <summary>Thrown on 401 / 403 responses (missing, invalid, or unauthorized API key).</summary>
public sealed class NewsdataAuthException : NewsdataApiException
{
    public NewsdataAuthException(string message, int statusCode, string? responseBody = null)
        : base(message, statusCode, responseBody) { }
}
