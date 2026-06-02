using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newsdata.Api;
using Newsdata.Api.Exceptions;
using Xunit;

namespace Newsdata.Api.Tests;

/// <summary>
/// Client tests using a simple HttpMessageHandler stub — no WireMock, no
/// network. Each test wires up a handler that returns canned responses
/// and asserts on the client's behaviour.
/// </summary>
public class NewsDataApiClientTests
{
    // ---- helpers --------------------------------------------------------

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri> CalledUris { get; } = new();

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null) CalledUris.Add(request.RequestUri);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Resp(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var msg = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            if (!msg.Headers.TryAddWithoutValidation(name, value))
                msg.Content.Headers.TryAddWithoutValidation(name, value);
        }
        return msg;
    }

    private static string SuccessBody(string resultsJson)
        => $"{{\"status\":\"success\",\"results\":{resultsJson}}}";

    private static NewsDataApiClient ClientWith(MockHandler handler, Action<NewsDataApiClientOptions>? cfg = null)
    {
        return new NewsDataApiClient(new NewsDataApiClientOptions
        {
            ApiKey = "key",
            HttpClient = new HttpClient(handler),
            RetryBackoff = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(1),
            PaginationDelay = TimeSpan.Zero,
        }.Apply(cfg));
    }

    // ---- tests ----------------------------------------------------------

    [Fact]
    public async Task Successful_request_returns_response()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            SuccessBody("[{\"article_id\":\"1\",\"title\":\"a\"}]")));
        using var client = ClientWith(handler);

        var resp = await client.LatestAsync(Params.Of().With("q", "x"));
        var articles = resp.GetArticles();
        Assert.Single(articles);
        Assert.Equal("a", articles[0].Title);
        Assert.Contains("apikey=key", handler.CalledUris[0].Query);
        Assert.Contains("q=x", handler.CalledUris[0].Query);
    }

    [Fact]
    public async Task Status_401_throws_NewsdataAuthException()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.Unauthorized,
            "{\"status\":\"error\",\"results\":{\"message\":\"bad key\"}}"));
        using var client = ClientWith(handler);

        var ex = await Assert.ThrowsAsync<NewsdataAuthException>(
            () => client.LatestAsync(Params.Of().With("q", "x")));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Status_429_retries_then_throws_RateLimit()
    {
        var calls = 0;
        var handler = new MockHandler(_ =>
        {
            calls++;
            var retryAfter = calls == 2 ? "7" : "0";
            return Resp(HttpStatusCode.TooManyRequests, "{\"status\":\"error\"}",
                ("Retry-After", retryAfter));
        });
        using var client = ClientWith(handler, o => o.MaxRetries = 2);

        var ex = await Assert.ThrowsAsync<NewsdataRateLimitException>(
            () => client.LatestAsync(Params.Of().With("q", "x")));
        Assert.Equal(7, ex.RetryAfter);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Status_503_retried_and_then_succeeds()
    {
        var calls = 0;
        var handler = new MockHandler(_ =>
        {
            if (++calls == 1)
                return Resp(HttpStatusCode.ServiceUnavailable, "{\"status\":\"error\"}");
            return Resp(HttpStatusCode.OK,
                SuccessBody("[{\"article_id\":\"1\",\"title\":\"recovered\"}]"));
        });
        using var client = ClientWith(handler, o => o.MaxRetries = 3);

        var resp = await client.LatestAsync(Params.Of().With("q", "x"));
        Assert.Equal("recovered", resp.GetArticles()[0].Title);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ScrollAll_merges_results_across_pages()
    {
        var pages = new[]
        {
            "{\"status\":\"success\",\"totalResults\":3,\"nextPage\":\"p2\",\"results\":[{\"article_id\":\"1\",\"title\":\"a\"},{\"article_id\":\"2\",\"title\":\"b\"}]}",
            "{\"status\":\"success\",\"totalResults\":3,\"results\":[{\"article_id\":\"3\",\"title\":\"c\"}]}",
        };
        var calls = 0;
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, pages[calls++]));
        using var client = ClientWith(handler);

        var merged = await client.ScrollAllAsync(Endpoint.Latest, Params.Of().With("q", "x"));
        Assert.Equal(3, merged.GetArticles().Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ScrollAll_honors_maxResults()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"nextPage\":\"p2\",\"results\":[{\"article_id\":\"1\",\"title\":\"a\"},{\"article_id\":\"2\",\"title\":\"b\"}]}"));
        using var client = ClientWith(handler);

        var merged = await client.ScrollAllAsync(Endpoint.Latest, Params.Of().With("q", "x"), maxResults: 1);
        Assert.Single(merged.GetArticles());
    }

    [Fact]
    public async Task Paginate_yields_each_page()
    {
        var pages = new[]
        {
            "{\"status\":\"success\",\"nextPage\":\"p2\",\"results\":[{\"article_id\":\"1\",\"title\":\"a\"}]}",
            "{\"status\":\"success\",\"results\":[{\"article_id\":\"2\",\"title\":\"b\"}]}",
        };
        var calls = 0;
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, pages[calls++]));
        using var client = ClientWith(handler);

        var titles = new List<string?>();
        await foreach (var page in client.Paginate(Endpoint.Latest, Params.Of().With("q", "x")))
        {
            foreach (var a in page.GetArticles()) titles.Add(a.Title);
        }
        Assert.Equal(new[] { "a", "b" }, titles);
    }

    [Fact]
    public async Task Paginate_stops_on_cancellation_via_break()
    {
        var pages = new[]
        {
            "{\"status\":\"success\",\"nextPage\":\"p2\",\"results\":[{\"article_id\":\"1\",\"title\":\"a\"}]}",
            "{\"status\":\"success\",\"nextPage\":\"p3\",\"results\":[{\"article_id\":\"2\",\"title\":\"b\"}]}",
            "{\"status\":\"success\",\"nextPage\":\"p4\",\"results\":[{\"article_id\":\"3\",\"title\":\"c\"}]}",
        };
        var calls = 0;
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, pages[calls++]));
        using var client = ClientWith(handler);

        var count = 0;
        await foreach (var page in client.Paginate(Endpoint.Latest, Params.Of().With("q", "x")))
        {
            count++;
            if (count == 2) break;
        }
        Assert.Equal(2, count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Empty_apikey_rejected()
    {
        Assert.Throws<NewsdataValidationException>(
            () => new NewsDataApiClient(""));
    }

    [Fact]
    public void RedactApiKey_hides_key()
    {
        Assert.Equal(
            "https://newsdata.io/api/1/latest?apikey=REDACTED&q=foo",
            NewsDataApiClient.RedactApiKey("https://newsdata.io/api/1/latest?apikey=SECRET&q=foo"));
    }

    [Fact]
    public async Task Typed_errors_form_a_catchable_hierarchy()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.Unauthorized, "{\"status\":\"error\"}"));
        using var client = ClientWith(handler);

        try
        {
            await client.LatestAsync(Params.Of().With("q", "x"));
            Assert.Fail("expected exception");
        }
        catch (NewsdataException e)
        {
            Assert.IsType<NewsdataAuthException>(e);
            Assert.IsAssignableFrom<NewsdataApiException>(e);
        }
    }

    [Fact]
    public async Task Article_decodes_snake_case_json()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            SuccessBody("[{\"article_id\":\"a1\",\"title\":\"t\",\"link\":\"l\","
                + "\"ai_tag\":[\"x\",\"y\"],\"sentiment\":\"positive\","
                + "\"source_priority\":1}]")));
        using var client = ClientWith(handler);

        var resp = await client.LatestAsync(Params.Of().With("q", "x"));
        var art = resp.GetArticles()[0];
        Assert.Equal("a1", art.ArticleId);
        Assert.Equal("t", art.Title);
        Assert.Equal("l", art.Link);
        Assert.Equal(new[] { "x", "y" }, art.AiTag!);
        Assert.Equal("positive", art.Sentiment);
        Assert.Equal(1, art.SourcePriority);
    }

    [Fact]
    public async Task Count_returns_aggregate_map()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"results\":{\"total\":42,\"hour\":{\"00\":1}}}"));
        using var client = ClientWith(handler);

        var resp = await client.CountAsync(Params.Of()
            .With("from_date", "2024-01-01")
            .With("to_date", "2024-01-02"));
        var agg = resp.GetAggregate();
        Assert.NotNull(agg);
        Assert.Equal(42, agg!["total"].GetInt32());
        Assert.Empty(resp.GetArticles());
    }

    [Fact]
    public void ParseRetryAfter_handles_integer_seconds()
    {
        Assert.Equal(7, NewsDataApiClient.ParseRetryAfter(
            RetryConditionHeaderValue.Parse("7")));
        Assert.Equal(0, NewsDataApiClient.ParseRetryAfter(null));
    }
}

internal static class OptionsExtensions
{
    public static NewsDataApiClientOptions Apply(this NewsDataApiClientOptions o, Action<NewsDataApiClientOptions>? cfg)
    {
        cfg?.Invoke(o);
        return o;
    }
}
