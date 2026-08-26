namespace Newsdata.Api.Exceptions;

/// <summary>
/// A real-time WebSocket stream failure
/// (see <see cref="Newsdata.Api.NewsDataApiWebSocket"/>).
/// </summary>
public class NewsdataWebSocketException : NewsdataException
{
    public NewsdataWebSocketException(string message, Exception? cause = null)
        : base(message, cause ?? new Exception(message)) { }
}
