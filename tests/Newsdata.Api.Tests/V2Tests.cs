using System.Net;
using System.Text;
using Newsdata.Api;
using Newsdata.Api.Exceptions;
using Xunit;

namespace Newsdata.Api.Tests;

/// <summary>
/// The v2 endpoints: versioned paths, the snake_case filter vocabulary, the
/// snake_case envelope keys, and the quota-exhausted exception split.
/// </summary>
public class V2Tests
{
    // ---- helpers --------------------------------------------------------

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri> CalledUris { get; } = new();

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null) CalledUris.Add(request.RequestUri);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Resp(HttpStatusCode status, string body,
        params (string Name, string Value)[] headers)
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

    private static NewsDataApiClient ClientWith(MockHandler handler)
        => new(new NewsDataApiClientOptions
        {
            ApiKey = "key",
            HttpClient = new HttpClient(handler),
            RetryBackoff = TimeSpan.FromMilliseconds(1),
            RetryBackoffMax = TimeSpan.FromMilliseconds(1),
            PaginationDelay = TimeSpan.Zero,
        });

    // ---- paths ----------------------------------------------------------

    public static TheoryData<string, string> V2Paths => new()
    {
        { "NewsV2Async", "/api/2/news" },
        { "MarketV2Async", "/api/2/market" },
        { "CryptoV2Async", "/api/2/crypto" },
        { "SourcesV2Async", "/api/2/sources" },
        { "CountNewsV2Async", "/api/2/count/news" },
        { "CountMarketV2Async", "/api/2/count/market" },
        { "CountCryptoV2Async", "/api/2/count/crypto" },
    };

    [Theory]
    [MemberData(nameof(V2Paths))]
    public async Task Each_v2_method_hits_its_versioned_path(string method, string path)
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        // sources takes no `q`, so send a filter every v2 endpoint accepts.
        var call = (Task<NewsdataResponse>)client.GetType()
            .GetMethod(method)!
            .Invoke(client, new object?[] { Params.Of().With("language", "en"), default(CancellationToken) })!;
        await call;

        Assert.Equal(path, handler.CalledUris[0].AbsolutePath);
    }

    [Fact]
    public async Task V1_methods_keep_their_versioned_path()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await client.LatestAsync(Params.Of().With("q", "x"));

        Assert.Equal("/api/1/latest", handler.CalledUris[0].AbsolutePath);
    }

    // ---- the filter vocabulary ------------------------------------------

    [Fact]
    public async Task V2_accepts_the_snake_case_vocabulary()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await client.NewsV2Async(Params.Of()
            .With("q_in_title", "tesla")
            .With("exclude_country", "cn")
            .With("source_url", "bbc.com")
            .With("source_priority", "top")
            .With("content", true)
            .With("remove_duplicate", true)
            .With("exclude_field", "description"));

        var query = handler.CalledUris[0].Query;
        foreach (var expected in new[]
                 {
                     "q_in_title=tesla", "exclude_country=cn", "source_url=bbc.com",
                     "source_priority=top", "content=1", "remove_duplicate=1",
                     "exclude_field=description",
                 })
        {
            Assert.Contains(expected, query);
        }
    }

    public static TheoryData<string, object> ReplacedV1Spellings => new()
    {
        { "qInTitle", "x" },
        { "domain", "bbc.com" },
        { "domainurl", "bbc.com" },
        { "excludedomain", "bbc.com" },
        { "excludedomainurl", "bbc.com" },
        { "prioritydomain", "top" },
        { "full_content", true },
        { "removeduplicate", true },
        { "excludefield", "description" },
    };

    [Theory]
    [MemberData(nameof(ReplacedV1Spellings))]
    public async Task V2_rejects_the_v1_spellings_it_replaced(string name, object value)
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.NewsV2Async(Params.Of().With(name, value)));
    }

    [Fact]
    public async Task V1_still_accepts_the_v1_spellings()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        var resp = await client.LatestAsync(Params.Of()
            .With("qInTitle", "x").With("domain", "bbc.com").With("full_content", true));

        Assert.Equal("success", resp.Status);
    }

    public static TheoryData<string, string, string, string> V2MutexGroups => new()
    {
        { "source_url", "a.com", "exclude_source_url", "b.com" },
        { "q", "a", "q_in_title", "b" },
        { "country", "us", "exclude_country", "cn" },
        { "category", "top", "exclude_category", "sports" },
        { "language", "en", "exclude_language", "de" },
    };

    [Theory]
    [MemberData(nameof(V2MutexGroups))]
    public async Task V2_mutex_groups_are_enforced(string a, string av, string b, string bv)
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.NewsV2Async(Params.Of().With(a, av).With(b, bv)));
    }

    // ---- the envelope ---------------------------------------------------

    [Fact]
    public async Task Response_reads_both_envelope_spellings()
    {
        var v2Handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"total_results\":42,\"results\":[],\"next_page\":\"abc\"}"));
        using var v2Client = ClientWith(v2Handler);
        var v2 = await v2Client.NewsV2Async(Params.Of().With("q", "x"));
        Assert.Equal(42, v2.TotalResults);
        Assert.Equal("abc", v2.NextPage);

        var v1Handler = new MockHandler(_ => Resp(HttpStatusCode.OK,
            "{\"status\":\"success\",\"totalResults\":42,\"results\":[],\"nextPage\":\"abc\"}"));
        using var v1Client = ClientWith(v1Handler);
        var v1 = await v1Client.LatestAsync(Params.Of().With("q", "x"));
        Assert.Equal(42, v1.TotalResults);
        Assert.Equal("abc", v1.NextPage);
    }

    [Fact]
    public async Task ScrollAll_follows_the_v2_cursor()
    {
        var calls = 0;
        var handler = new MockHandler(_ =>
        {
            calls++;
            return calls == 1
                ? Resp(HttpStatusCode.OK,
                    "{\"status\":\"success\",\"total_results\":2," +
                    "\"results\":[{\"article_id\":\"1\"}],\"next_page\":\"p2\"}")
                : Resp(HttpStatusCode.OK,
                    "{\"status\":\"success\",\"total_results\":2," +
                    "\"results\":[{\"article_id\":\"2\"}]}");
        });
        using var client = ClientWith(handler);

        var merged = await client.ScrollAllAsync(Endpoint.NewsV2, Params.Of().With("q", "x"));

        Assert.Equal(2, merged.GetArticles().Count);
        Assert.Equal(2, calls);
        Assert.Contains("page=p2", handler.CalledUris[1].Query);
    }

    [Fact]
    public async Task ScrollAll_rejects_the_v2_sources_endpoint()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.ScrollAllAsync(Endpoint.SourcesV2, Params.Of()));
    }

    // ---- the quota split ------------------------------------------------

    [Theory]
    [InlineData("ApiLimitExceeded", typeof(NewsdataApiLimitExceededException))]
    [InlineData("ApiKeyLimitExceeded", typeof(NewsdataApiKeyLimitExceededException))]
    public async Task Each_quota_code_raises_its_own_exception(string code, Type expected)
    {
        var calls = 0;
        var handler = new MockHandler(_ =>
        {
            calls++;
            return Resp(HttpStatusCode.TooManyRequests,
                $"{{\"status\":\"error\",\"results\":{{\"message\":\"limit\",\"code\":\"{code}\"}}}}");
        });
        using var client = ClientWith(handler);

        var err = await Assert.ThrowsAnyAsync<NewsdataRateLimitException>(
            () => client.NewsV2Async(Params.Of().With("q", "x")));

        Assert.IsType(expected, err);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transient_429_stays_the_base_exception_and_retries()
    {
        var calls = 0;
        var handler = new MockHandler(_ =>
        {
            calls++;
            return Resp(HttpStatusCode.TooManyRequests,
                "{\"status\":\"error\",\"results\":{\"message\":\"slow\",\"code\":\"RateLimitExceeded\"}}");
        });
        using var client = ClientWith(handler);

        var err = await Assert.ThrowsAnyAsync<NewsdataRateLimitException>(
            () => client.NewsV2Async(Params.Of().With("q", "x")));

        Assert.IsType<NewsdataRateLimitException>(err);
        Assert.True(calls > 1, "a transient rate limit should still retry");
    }

    // ---- excludedomainurl (v1) ------------------------------------------

    [Theory]
    [InlineData("latest")]
    [InlineData("archive")]
    [InlineData("crypto")]
    [InlineData("market")]
    [InlineData("count")]
    [InlineData("crypto_count")]
    [InlineData("market_count")]
    public void ExcludeDomainUrl_is_accepted_on_the_v1_endpoints(string endpoint)
        => Assert.Contains("excludedomainurl", Constants.Filters[endpoint]);

    [Fact]
    public void ExcludeDomainUrl_is_not_a_sources_filter()
        => Assert.DoesNotContain("excludedomainurl", Constants.Filters["sources"]);

    [Fact]
    public async Task ExcludeDomainUrl_joins_the_v1_domain_mutex_group()
    {
        var handler = new MockHandler(_ => Resp(HttpStatusCode.OK, SuccessBody("[]")));
        using var client = ClientWith(handler);

        await Assert.ThrowsAsync<NewsdataValidationException>(
            () => client.LatestAsync(Params.Of()
                .With("domain", "a.com").With("excludedomainurl", "b.com")));
    }
}
