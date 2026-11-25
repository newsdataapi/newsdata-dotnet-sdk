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
        => $"{{\"status\":\"success\",\"total_results\":1,\"results\":"
           + $"[{{\"id\":\"{id}\",\"title\":\"{title}\"}}]}}";

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
        Assert.Contains("news_type=news", url);
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
            "{\"status\":\"success\",\"total_results\":0,\"results\":[]}"));
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

    // ---- API version 2 ----------------------------------------------------
    // The real-time service moved to API version 2: the management endpoints
    // sit under /2/, news_type names the v2 news feed, and a registration takes
    // the v2 filter vocabulary only.

    [Theory]
    [InlineData(Endpoint.WebsocketRegister, "2/websocket/register")]
    [InlineData(Endpoint.WebsocketFetch, "2/websocket/fetch")]
    [InlineData(Endpoint.WebsocketDelete, "2/websocket/delete")]
    public void Websocket_endpoints_are_under_api_version_2(string endpoint, string path)
        => Assert.Equal(path, Constants.EndpointPaths[endpoint]);

    [Fact]
    public void WsNewsType_names_the_v2_news_feed()
        => Assert.Equal("news", Constants.WsNewsType);

    [Theory]
    [InlineData("q_in_title")]
    [InlineData("q_in_meta")]
    [InlineData("exclude_country")]
    [InlineData("exclude_category")]
    [InlineData("exclude_language")]
    [InlineData("source_url")]
    [InlineData("exclude_source_url")]
    [InlineData("source_priority")]
    [InlineData("content")]
    [InlineData("remove_duplicate")]
    [InlineData("exclude_field")]
    public void Register_takes_the_v2_filter_names(string name)
        => Assert.Contains(name, Constants.Filters[Endpoint.WebsocketRegister]);

    [Theory]
    [InlineData("qintitle")]
    [InlineData("qinmeta")]
    [InlineData("excludecountry")]
    [InlineData("excludecategory")]
    [InlineData("excludelanguage")]
    [InlineData("domain")]
    [InlineData("domainurl")]
    [InlineData("excludedomain")]
    [InlineData("excludedomainurl")]
    [InlineData("prioritydomain")]
    [InlineData("full_content")]
    [InlineData("removeduplicate")]
    [InlineData("excludefield")]
    // Still no date or paging filters on a standing query.
    [InlineData("from_date")]
    [InlineData("page")]
    [InlineData("size")]
    [InlineData("sort")]
    public void Register_no_longer_takes_the_v1_filter_names(string name)
        => Assert.DoesNotContain(name, Constants.Filters[Endpoint.WebsocketRegister]);

    [Fact]
    public async Task Register_rejects_a_v1_filter_name()
    {
        using var client = PlainClient();
        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.WebsocketRegisterAsync(Params.Of().With("qInTitle", "pizza")));
    }

    [Fact]
    public async Task Register_keeps_the_v2_source_url_mutex_group()
    {
        using var client = PlainClient();
        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.WebsocketRegisterAsync(Params.Of()
                .With("source_url", "a.com")
                .With("exclude_source_url", "b.com")));
    }

    [Fact]
    public async Task WebsocketFetch_returns_one_flat_entry_per_query()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"total_results\":1,\"results\":[{"
            + "\"registration_id\":\"reg-9\",\"active_clients\":2,"
            + "\"last_used\":\"2026-11-20T10:15:00Z\","
            + "\"news_type\":\"news\",\"query\":{\"q\":\"pizza\"}}]}"));
        using var client = ClientWith(handler);

        var response = await client.WebsocketFetchAsync();

        Assert.Equal(1, response.TotalResults);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, response.Results.ValueKind);
        var entry = response.Results[0];
        Assert.Equal("reg-9", entry.GetProperty("registration_id").GetString());
        Assert.Equal(2, entry.GetProperty("active_clients").GetInt32());
        Assert.Equal("2026-11-20T10:15:00Z", entry.GetProperty("last_used").GetString());
        Assert.Equal("pizza", entry.GetProperty("query").GetProperty("q").GetString());
    }

    // RegistrationLimitExceeded means every registration slot the plan allows
    // is in use. It is never retried, but keeps the base exception: it is about
    // slots, not credits, so there is no dedicated subclass.
    [Fact]
    public async Task RegistrationLimitExceeded_keeps_the_base_exception()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.TooManyRequests,
            "{\"status\":\"error\",\"results\":{\"message\":\"limit\","
            + "\"code\":\"RegistrationLimitExceeded\"}}"));
        using var client = ClientWith(handler);

        var err = await Assert.ThrowsAnyAsync<NewsdataRateLimitException>(
            () => client.WebsocketRegisterAsync(Params.Of().With("q", "x")));

        Assert.IsType<NewsdataRateLimitException>(err);
        Assert.Single(handler.Requests);
    }
}
