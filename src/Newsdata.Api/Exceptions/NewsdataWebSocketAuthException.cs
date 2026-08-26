namespace Newsdata.Api.Exceptions;

/// <summary>
/// The server rejected the WebSocket connection — bad API key, missing
/// WebSocket entitlement, unknown <c>registration_id</c>, device limit
/// reached, or exhausted quota. Never retried, regardless of the
/// reconnect setting.
/// </summary>
public sealed class NewsdataWebSocketAuthException : NewsdataWebSocketException
{
    public NewsdataWebSocketAuthException(string message, Exception? cause = null)
        : base(message, cause) { }
}
