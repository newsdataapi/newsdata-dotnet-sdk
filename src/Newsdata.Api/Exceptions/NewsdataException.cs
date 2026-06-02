namespace Newsdata.Api.Exceptions;

/// <summary>
/// Base class for every exception thrown by the Newsdata.io SDK. Catch this
/// for a catch-all; catch a subclass to react to specific failure modes.
/// </summary>
public class NewsdataException : Exception
{
    public NewsdataException(string message) : base(message) { }
    public NewsdataException(string message, Exception innerException) : base(message, innerException) { }
}
