using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Newsdata.Api.Tests;

/// <summary>
/// A minimal RFC 6455 server for tests — enough to accept the handshake and
/// push text / close frames at the client. Server-to-client frames are
/// unmasked, which keeps the writer trivial; inbound frames are ignored.
///
/// Not a general-purpose implementation: no fragmentation, no extensions, no
/// payloads over 65535 bytes.
/// </summary>
internal sealed class MockWebSocketServer : IDisposable
{
    private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<Session, int, Task> _onConnect;
    private readonly int _handshakeStatus;
    private int _connections;
    private readonly List<string> _queries = new();
    private readonly object _lock = new();

    /// <param name="handshakeStatus">101 to accept, or an error status to reject with.</param>
    /// <param name="onConnect">Runs per accepted connection: (session, connectionNumber).</param>
    public MockWebSocketServer(int handshakeStatus, Func<Session, int, Task> onConnect)
    {
        _handshakeStatus = handshakeStatus;
        _onConnect = onConnect;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public string Url => $"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/ws/event";

    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>Query strings seen on each handshake, in order.</summary>
    public IReadOnlyList<string> Queries
    {
        get { lock (_lock) return _queries.ToList(); }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception)
            {
                return; // listener stopped
            }
            var n = Interlocked.Increment(ref _connections);
            _ = ServeAsync(client, n);
        }
    }

    private async Task ServeAsync(TcpClient client, int n)
    {
        using (client)
        {
            var stream = client.GetStream();
            var request = await ReadHeadersAsync(stream);
            if (request is null) return;

            var (requestLine, key) = request.Value;
            var q = requestLine.IndexOf('?');
            var end = requestLine.IndexOf(' ', q < 0 ? 0 : q);
            lock (_lock)
            {
                _queries.Add(q >= 0
                    ? requestLine.Substring(q + 1, (end < 0 ? requestLine.Length : end) - q - 1)
                    : string.Empty);
            }

            if (_handshakeStatus != 101)
            {
                var body = Encoding.UTF8.GetBytes("{\"status\":\"error\"}");
                var head = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 {_handshakeStatus} NO\r\nContent-Length: {body.Length}\r\n"
                    + "Connection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
                return;
            }

            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.UTF8.GetBytes(key + Guid)));
            var response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\nConnection: Upgrade\r\n"
                + $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();

            try
            {
                await _onConnect(new Session(stream), n);
            }
            catch (Exception)
            {
                // client went away
            }
        }
    }

    private static async Task<(string RequestLine, string Key)?> ReadHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
            if (n == 0) return null;
            read += n;
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (!text.Contains("\r\n\r\n")) continue;

            var lines = text.Split("\r\n");
            var key = lines.FirstOrDefault(l =>
                l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
            return (lines[0], key?[(key.IndexOf(':') + 1)..].Trim() ?? string.Empty);
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }

    /// <summary>Write side of one accepted connection.</summary>
    internal sealed class Session
    {
        private readonly NetworkStream _stream;

        public Session(NetworkStream stream) => _stream = stream;

        /// <summary>Send one unmasked text frame.</summary>
        public async Task SendTextAsync(string payload)
        {
            var data = Encoding.UTF8.GetBytes(payload);
            var frame = new List<byte> { 0x81 }; // FIN + text
            if (data.Length < 126)
            {
                frame.Add((byte)data.Length);
            }
            else
            {
                frame.Add(126);
                frame.Add((byte)((data.Length >> 8) & 0xFF));
                frame.Add((byte)(data.Length & 0xFF));
            }
            frame.AddRange(data);
            await _stream.WriteAsync(frame.ToArray());
            await _stream.FlushAsync();
        }

        /// <summary>Send a close frame with the given code and reason.</summary>
        public async Task SendCloseAsync(int code, string reason)
        {
            var r = Encoding.UTF8.GetBytes(reason);
            var frame = new List<byte> { 0x88, (byte)(2 + r.Length) };
            frame.Add((byte)((code >> 8) & 0xFF));
            frame.Add((byte)(code & 0xFF));
            frame.AddRange(r);
            await _stream.WriteAsync(frame.ToArray());
            await _stream.FlushAsync();
        }

        /// <summary>Keep the connection open.</summary>
        public Task HoldAsync(int milliseconds) => Task.Delay(milliseconds);
    }
}
