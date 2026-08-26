using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Newsdata.Api.Exceptions;

namespace Newsdata.Api;

/// <summary>
/// NewsData.io real-time WebSocket service.
///
/// <para>Registers, lists, and deletes the account's real-time queries and
/// streams the responses for a registered query. The management calls go
/// through the wrapped <see cref="NewsDataApiClient"/>:</para>
///
/// <code>
/// using var client = new NewsDataApiClient(new NewsDataApiClientOptions { ApiKey = key });
/// using var ws = new NewsDataApiWebSocket(client);
///
/// var registered = await ws.WebsocketRegisterAsync(Params.Of().With("q", "bitcoin"));
/// var id = registered.Results.GetProperty("registration_id").GetString()!;
///
/// await foreach (var response in ws.StreamAsync(id))
/// {
///     foreach (var article in response.GetArticles())
///         Console.WriteLine(article.Title);
/// }
/// </code>
///
/// <para>Transient drops (network errors, server restarts, abnormal closes)
/// are reconnected automatically with a capped exponential backoff; set
/// <see cref="NewsDataApiWebSocketOptions.Reconnect"/> to <c>false</c> to stop
/// on the first disconnect. A permanent rejection always throws
/// <see cref="NewsdataWebSocketAuthException"/> and is never retried.</para>
///
/// <para>Break out of the <c>await foreach</c>, cancel the token, or dispose
/// the instance to stop; the connection is closed either way.</para>
/// </summary>
public sealed class NewsDataApiWebSocket : IDisposable
{
    private readonly NewsDataApiClient _client;
    private readonly NewsDataApiWebSocketOptions _options;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    /// <summary>Construct with the default options.</summary>
    public NewsDataApiWebSocket(NewsDataApiClient client)
        : this(client, new NewsDataApiWebSocketOptions()) { }

    /// <summary>Construct with explicit connection options.</summary>
    public NewsDataApiWebSocket(NewsDataApiClient client, NewsDataApiWebSocketOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // ---- query management -------------------------------------------------

    /// <summary>Register a real-time query. See <see cref="NewsDataApiClient.WebsocketRegisterAsync"/>.</summary>
    public Task<NewsdataResponse> WebsocketRegisterAsync(
        IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => _client.WebsocketRegisterAsync(p, ct);

    /// <summary>List registered queries. See <see cref="NewsDataApiClient.WebsocketFetchAsync"/>.</summary>
    public Task<NewsdataResponse> WebsocketFetchAsync(CancellationToken ct = default)
        => _client.WebsocketFetchAsync(ct);

    /// <summary>Delete a registered query. See <see cref="NewsDataApiClient.WebsocketDeleteAsync"/>.</summary>
    public Task<NewsdataResponse> WebsocketDeleteAsync(
        string registrationId, CancellationToken ct = default)
        => _client.WebsocketDeleteAsync(registrationId, ct);

    // ---- streaming --------------------------------------------------------

    private Uri BuildUri(string registrationId)
    {
        var query = "apikey=" + Uri.EscapeDataString(_client.ApiKeyForWebSocket)
                  + "&registration_id=" + Uri.EscapeDataString(registrationId);
        return new Uri(_options.BaseUrl + "?" + query);
    }

    private TimeSpan NextDelay(TimeSpan delay)
    {
        var doubled = delay + delay;
        return doubled > _options.ReconnectDelayMax ? _options.ReconnectDelayMax : doubled;
    }

    /// <summary>
    /// Connect and yield each response for <paramref name="registrationId"/>
    /// as it arrives. Responses have the familiar <c>status</c> /
    /// <c>totalResults</c> / <c>results</c> shape.
    /// </summary>
    public async IAsyncEnumerable<NewsdataResponse> StreamAsync(
        string registrationId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(registrationId))
            throw new NewsdataValidationException(
                "registrationId must be a non-empty string", "registration_id");

        var uri = BuildUri(registrationId);
        var logUrl = NewsDataApiClient.RedactApiKey(uri.ToString());
        var delay = _options.ReconnectDelay;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var token = linked.Token;

        while (!token.IsCancellationRequested)
        {
            var session = await ConnectAsync(uri, logUrl, token).ConfigureAwait(false);
            if (session is not null)
            {
                delay = _options.ReconnectDelay; // reset after a successful connect
                using var socket = session;

                while (true)
                {
                    ReadResult read;
                    try
                    {
                        read = await ReceiveAsync(socket, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        HandleFailure(ex, logUrl, socket.CloseStatus, socket.CloseStatusDescription);
                        break; // transient with reconnect on — fall through to backoff
                    }

                    if (read.Closed)
                    {
                        if (read.CloseStatus is not null
                            && (int)read.CloseStatus == Constants.WsPolicyViolation)
                        {
                            throw new NewsdataWebSocketAuthException(
                                string.IsNullOrEmpty(read.CloseDescription)
                                    ? "connection rejected"
                                    : read.CloseDescription!);
                        }
                        if (read.CloseStatus == WebSocketCloseStatus.NormalClosure)
                        {
                            if (!_options.Reconnect) yield break;
                            break; // normal close, reconnect enabled
                        }
                        if (!_options.Reconnect)
                            throw new NewsdataWebSocketException("connection closed");
                        break;
                    }

                    var parsed = Parse(read.Payload!);
                    if (parsed is not null) yield return parsed;
                }
            }

            if (token.IsCancellationRequested) yield break;
            if (!_options.Reconnect) yield break;

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            delay = NextDelay(delay);
        }
    }

    /// <summary>
    /// Open one connection. Returns null when the attempt failed transiently
    /// and the caller should back off and retry; throws for a permanent
    /// rejection, or for any failure when reconnect is disabled.
    /// </summary>
    private async Task<ClientWebSocket?> ConnectAsync(Uri uri, string logUrl, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        foreach (var (name, value) in _options.Headers)
            socket.Options.SetRequestHeader(name, value);

        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_options.HandshakeTimeout > TimeSpan.Zero)
            handshakeCts.CancelAfter(_options.HandshakeTimeout);

        try
        {
            await socket.ConnectAsync(uri, handshakeCts.Token).ConfigureAwait(false);
            _client.LogFromWebSocket("info", $"connected to {logUrl}");
            return socket;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            if (ct.IsCancellationRequested) return null;
            HandleFailure(ex, logUrl, null, null);
            return null;
        }
    }

    /// <summary>
    /// Classify a connection failure.
    /// <para>The server always accepts the handshake and then closes with code
    /// 1008 on a permanent failure — <c>invalid credentials or registration not
    /// found</c>, <c>api limit reached</c>, or <c>device limit reached</c>.
    /// Those always throw. Every other close code, including 1013
    /// (<c>send timeout</c> — this client read too slowly), is transient: it
    /// throws only when reconnect is disabled, otherwise it is logged so the
    /// caller backs off and retries. The handshake-status check is defensive,
    /// for proxies in front of the documented server.</para>
    /// </summary>
    private void HandleFailure(
        Exception ex, string logUrl, WebSocketCloseStatus? closeStatus, string? closeDescription)
    {
        if (closeStatus is not null && (int)closeStatus == Constants.WsPolicyViolation)
        {
            throw new NewsdataWebSocketAuthException(
                string.IsNullOrEmpty(closeDescription) ? "connection rejected" : closeDescription!, ex);
        }

        if (ex is WebSocketException wse)
        {
            // .NET surfaces the rejected handshake status on the exception when
            // the server answered with one.
            var status = HandshakeStatusOf(wse);
            if (status is 401 or 403)
                throw new NewsdataWebSocketAuthException("connection rejected", ex);

            if (!_options.Reconnect)
            {
                throw new NewsdataWebSocketException(
                    status is not null
                        ? $"handshake failed (HTTP {status})"
                        : "connection closed",
                    ex);
            }
        }
        else if (!_options.Reconnect)
        {
            throw new NewsdataWebSocketException($"connection error: {ex.Message}", ex);
        }

        _client.LogFromWebSocket("warn", $"connection to {logUrl} failed ({ex.Message}); reconnecting");
    }

    /// <summary>
    /// Recover the HTTP status from a failed handshake. .NET exposes it on
    /// <see cref="WebSocketException"/> only from .NET 7; older runtimes leave
    /// it in the message, so both are checked.
    /// </summary>
    private static int? HandshakeStatusOf(WebSocketException ex)
    {
        var prop = ex.GetType().GetProperty("HttpStatusCode");
        if (prop?.GetValue(ex) is System.Net.HttpStatusCode code && (int)code != 0)
            return (int)code;

        foreach (var candidate in new[] { 401, 403 })
        {
            if (ex.Message.Contains($"'{candidate}'", StringComparison.Ordinal)
                || ex.Message.Contains($" {candidate} ", StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        return null;
    }

    private readonly record struct ReadResult(
        bool Closed, string? Payload, WebSocketCloseStatus? CloseStatus, string? CloseDescription);

    /// <summary>Read one complete message, reassembling continuation frames.</summary>
    private static async Task<ReadResult> ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new ReadResult(true, null, result.CloseStatus, result.CloseStatusDescription);
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
                return new ReadResult(false, builder.ToString(), null, null);
        }
    }

    /// <summary>Parse one frame, returning null when it isn't a JSON object.</summary>
    private static NewsdataResponse? Parse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var root = doc.RootElement;
            return new NewsdataResponse
            {
                Status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() : null,
                TotalResults = root.TryGetProperty("totalResults", out var tr) && tr.TryGetInt32(out var n)
                    ? n : 0,
                Results = root.TryGetProperty("results", out var r) ? r.Clone() : default,
                NextPage = root.TryGetProperty("nextPage", out var np) && np.ValueKind == JsonValueKind.String
                    ? np.GetString() : null,
                Headers = null,
            };
        }
        catch (JsonException)
        {
            return null; // skip malformed frames
        }
    }

    /// <summary>Close the active connection, ending any in-flight stream.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}

/// <summary>Connection options for <see cref="NewsDataApiWebSocket"/>.</summary>
public sealed class NewsDataApiWebSocketOptions
{
    /// <summary>
    /// WebSocket endpoint. Defaults to <c>wss://ws.newsdata.io/ws/event</c>;
    /// override for staging, self-hosted, or proxied environments.
    /// </summary>
    public string BaseUrl { get; set; } = Constants.WsBaseUrl;

    /// <summary>Reconnect automatically on transient drops. Default true.</summary>
    public bool Reconnect { get; set; } = true;

    /// <summary>Wait before the first reconnect; doubles after each failure.</summary>
    public TimeSpan ReconnectDelay { get; set; } = Constants.WsReconnectDelay;

    /// <summary>Upper bound on the reconnect delay.</summary>
    public TimeSpan ReconnectDelayMax { get; set; } = Constants.WsReconnectDelayMax;

    /// <summary>Bound on the opening handshake. Zero disables the timeout.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = Constants.WsHandshakeTimeout;

    /// <summary>Extra HTTP headers for the opening handshake.</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}
