namespace Newsdata.Api.Exceptions;

/// <summary>Thrown on 5xx responses once retries are exhausted.</summary>
public sealed class NewsdataServerException : NewsdataApiException
{
    public NewsdataServerException(string message, int statusCode, string? responseBody = null)
        : base(message, statusCode, responseBody) { }
}
