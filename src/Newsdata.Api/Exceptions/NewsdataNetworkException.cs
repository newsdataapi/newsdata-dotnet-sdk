namespace Newsdata.Api.Exceptions;

/// <summary>
/// A transport-level failure (DNS, TLS, timeout, cancellation, socket error)
/// prevented the request from completing. <see cref="Exception.InnerException"/>
/// holds the underlying error.
/// </summary>
public sealed class NewsdataNetworkException : NewsdataException
{
    public NewsdataNetworkException(string message, Exception? cause = null)
        : base(message, cause ?? new Exception(message)) { }
}
