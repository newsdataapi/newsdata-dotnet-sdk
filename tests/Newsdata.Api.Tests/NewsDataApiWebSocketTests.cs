using System.Net;
using System.Text;
using Newsdata.Api;
using Newsdata.Api.Exceptions;
using Xunit;

namespace Newsdata.Api.Tests;

/// <summary>Real-time WebSocket tests, against a local RFC 6455 mock.</summary>
public class NewsDataApiWebSocketTests
{
    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Resp(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static NewsDataApiClient ClientWith(MockHandler handler)
        => new(new NewsDataApiClientOptions
        {
            ApiKey = "key",
            HttpClient = new HttpClient(handler),
            RetryBackoff = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(1),
        });

    private static NewsDataApiClient PlainClient()
        => new(new NewsDataApiClientOptions { ApiKey = "key" });

    private static string ArticleFrame(string id, string title)
        => $"{{\"status\":\"success\",\"totalResults\":1,\"results\":"
           + $"[{{\"article_id\":\"{id}\",\"title\":\"{title}\"}}]}}";

    private static NewsDataApiWebSocketOptions Options(string url, Action<NewsDataApiWebSocketOptions>? cfg = null)
    {
        var o = new NewsDataApiWebSocketOptions
        {
            BaseUrl = url,
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            ReconnectDelayMax = TimeSpan.FromMilliseconds(50),
        };
        cfg?.Invoke(o);
        return o;
    }

    [Fact(Timeout = 20000)]
    public async Task Streams_responses_as_they_arrive()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            await session.SendTextAsync(ArticleFrame("a1", "one"));
            await session.SendTextAsync(ArticleFrame("a2", "two"));
            await session.HoldAsync(500);
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url, o => o.Reconnect = false));

        var titles = new List<string>();
        await foreach (var response in ws.StreamAsync("reg-1"))
        {
            titles.Add(response.GetArticles()[0].Title!);
            if (titles.Count == 2) break;
        }

        Assert.Equal(new[] { "one", "two" }, titles);
    }

    [Fact(Timeout = 20000)]
    public async Task Sends_apikey_and_registration_id_in_query()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            await session.SendTextAsync(ArticleFrame("a1", "one"));
            await session.HoldAsync(300);
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url, o => o.Reconnect = false));

        await foreach (var _ in ws.StreamAsync("reg-42")) break;

        var query = server.Queries[0];
        Assert.Contains("apikey=key", query);
        Assert.Contains("registration_id=reg-42", query);
    }

    [Fact(Timeout = 20000)]
    public async Task Skips_malformed_frames()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            await session.SendTextAsync("not json at all");
            await session.SendTextAsync(ArticleFrame("a1", "one"));
            await session.HoldAsync(300);
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url, o => o.Reconnect = false));

        var seen = new List<string>();
        await foreach (var response in ws.StreamAsync("reg-1"))
        {
            seen.Add(response.GetArticles()[0].Title!);
            break;
        }

        Assert.Equal(new[] { "one" }, seen);
    }

    [Fact(Timeout = 20000)]
    public async Task Handshake_401_is_permanent_and_not_retried()
    {
        using var server = new MockWebSocketServer(401, (session, n) => Task.CompletedTask);

        using var client = PlainClient();
        // Reconnect stays ON to prove a permanent rejection is not retried.
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url));

        await Assert.ThrowsAsync<NewsdataWebSocketAuthException>(async () =>
        {
            await foreach (var _ in ws.StreamAsync("reg-1")) { }
        });
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact(Timeout = 20000)]
    public async Task Policy_violation_close_is_permanent()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            await session.SendCloseAsync(1008, "quota exhausted");
            await session.HoldAsync(200);
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url));

        var err = await Assert.ThrowsAsync<NewsdataWebSocketAuthException>(async () =>
        {
            await foreach (var _ in ws.StreamAsync("reg-1")) { }
        });
        Assert.Contains("quota exhausted", err.Message);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact(Timeout = 30000)]
    public async Task Reconnects_after_a_transient_drop()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            if (n == 1)
            {
                await session.SendCloseAsync(1011, "server restart"); // transient
                return;
            }
            await session.SendTextAsync(ArticleFrame("a1", "after-reconnect"));
            await session.HoldAsync(300);
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url));

        string? got = null;
        await foreach (var response in ws.StreamAsync("reg-1"))
        {
            got = response.GetArticles()[0].Title;
            break;
        }

        Assert.Equal("after-reconnect", got);
        Assert.True(server.ConnectionCount >= 2, $"connections={server.ConnectionCount}");
    }

    [Fact(Timeout = 20000)]
    public async Task Cancelling_the_token_ends_the_stream()
    {
        using var server = new MockWebSocketServer(101, async (session, n) =>
        {
            await session.HoldAsync(3000); // hold open
        });

        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client, Options(server.Url));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var count = 0;
        await foreach (var _ in ws.StreamAsync("reg-1", cts.Token)) count++;

        Assert.Equal(0, count); // ended without throwing
    }

    [Fact]
    public async Task Rejects_an_empty_registration_id()
    {
        using var client = PlainClient();
        using var ws = new NewsDataApiWebSocket(client);

        await Assert.ThrowsAsync<NewsdataValidationException>(async () =>
        {
            await foreach (var _ in ws.StreamAsync("")) { }
        });
    }

    // ---- query management -------------------------------------------------

    [Fact]
    public async Task WebsocketRegister_posts_with_news_type()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"results\":{\"registration_id\":\"reg-9\"}}"));
        using var client = ClientWith(handler);

        var response = await client.WebsocketRegisterAsync(Params.Of().With("q", "bitcoin"));

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        var url = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("news_type=latest", url);
        Assert.Contains("q=bitcoin", url);
        Assert.Contains("websocket/register", url);
        Assert.Equal("reg-9", response.Results.GetProperty("registration_id").GetString());
    }

    [Fact]
    public async Task WebsocketRegister_does_not_mutate_caller_params()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"results\":{}}"));
        using var client = ClientWith(handler);

        var p = Params.Of().With("q", "bitcoin");
        await client.WebsocketRegisterAsync(p);

        Assert.False(p.ContainsKey("news_type"));
    }

    [Fact]
    public async Task WebsocketFetch_uses_get()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"results\":{\"queries\":[]}}"));
        using var client = ClientWith(handler);

        await client.WebsocketFetchAsync();

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("websocket/fetch", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task WebsocketDelete_uses_delete()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"results\":{\"deleted\":true}}"));
        using var client = ClientWith(handler);

        await client.WebsocketDeleteAsync("reg-9");

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Contains("registration_id=reg-9", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task WebsocketDelete_rejects_an_empty_id()
    {
        using var client = PlainClient();
        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.WebsocketDeleteAsync(""));
    }

    [Fact]
    public async Task Resultless_success_still_succeeds_on_websocket_endpoints()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, "{\"status\":\"success\"}"));
        using var client = ClientWith(handler);

        var response = await client.WebsocketDeleteAsync("reg-9");
        Assert.Equal("success", response.Status);
    }
}
