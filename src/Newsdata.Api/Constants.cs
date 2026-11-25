using System.Net.Http;

using Newsdata.Api.Exceptions;

namespace Newsdata.Api;

/// <summary>
/// Static configuration: base URL, endpoint paths, HTTP defaults, and the
/// per-endpoint accepted-parameter sets. Mirrors the server-side filter
/// mapping and the official Python/PHP/Node/Go/Dart/Java clients.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// API root (ends with a slash). The paths in <see cref="EndpointPaths"/>
    /// carry the API version, so the v1 and v2 endpoints share one base.
    /// </summary>
    public const string BaseUrl = "https://newsdata.io/api/";

    public static readonly TimeSpan DefaultRequestTimeout    = TimeSpan.FromSeconds(30);
    public const int           DefaultMaxRetries             = 5;
    public static readonly TimeSpan DefaultRetryBackoff      = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultRetryBackoffMax   = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultPaginationDelay   = TimeSpan.FromSeconds(1);

    public const int SizeMin = 1;
    public const int SizeMax = 50;

    /// <summary>Endpoint key → URL path appended to <see cref="BaseUrl"/>.</summary>
    public static readonly IReadOnlyDictionary<string, string> EndpointPaths =
        new Dictionary<string, string>
        {
            // v1
            ["latest"]       = "1/latest",
            ["crypto"]       = "1/crypto",
            ["archive"]      = "1/archive",
            ["sources"]      = "1/sources",
            ["market"]       = "1/market",
            ["count"]        = "1/count",
            ["crypto_count"] = "1/crypto/count",
            ["market_count"] = "1/market/count",
            // v2
            ["news_v2"]          = "2/news",
            ["market_v2"]        = "2/market",
            ["crypto_v2"]        = "2/crypto",
            ["sources_v2"]       = "2/sources",
            ["count_news_v2"]    = "2/count/news",
            ["count_market_v2"]  = "2/count/market",
            ["count_crypto_v2"]  = "2/count/crypto",
            // The real-time service is v2 only.
            ["websocket_register"] = "2/websocket/register",
            ["websocket_fetch"]    = "2/websocket/fetch",
            ["websocket_delete"]   = "2/websocket/delete",
        };

    /// <summary>HTTP method per endpoint; anything absent is a GET.</summary>
    public static readonly IReadOnlyDictionary<string, HttpMethod> EndpointMethods =
        new Dictionary<string, HttpMethod>
        {
            ["websocket_register"] = HttpMethod.Post,
            ["websocket_delete"]   = HttpMethod.Delete,
        };

    /// <summary>
    /// Endpoints whose success envelope may carry no <c>results</c> field, so
    /// they are exempt from the results-present check applied elsewhere.
    /// </summary>
    public static readonly IReadOnlySet<string> ResultsOptional =
        new HashSet<string> { "websocket_register", "websocket_fetch", "websocket_delete" };

    /// <summary>Real-time WebSocket endpoint.</summary>
    public const string WsBaseUrl = "wss://ws.newsdata.io/ws/event";

    /// <summary>
    /// The feed a registered query matches against; only news so far.
    /// </summary>
    public const string WsNewsType = "news";

    /// <summary>Close code the server uses for a permanent connection rejection.</summary>
    public const int WsPolicyViolation = 1008;

    /// <summary>Wait before the first reconnect; doubles after each failure.</summary>
    public static readonly TimeSpan WsReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on the reconnect delay.</summary>
    public static readonly TimeSpan WsReconnectDelayMax = TimeSpan.FromSeconds(30);

    /// <summary>Bound on the opening handshake.</summary>
    public static readonly TimeSpan WsHandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Error codes on a 429 that waiting cannot fix, so they are never retried.
    /// <c>ApiLimitExceeded</c> and <c>ApiKeyLimitExceeded</c> mean the API
    /// credits are exhausted rather than a transient rate limit — backoff
    /// cannot conjure more credits. <c>RegistrationLimitExceeded</c> means
    /// every real-time registration the plan allows is in use; only deleting
    /// one frees a slot.
    /// <para><c>ApiLimitExceeded</c> is the documented code (see the ErrorCode
    /// enum in https://newsdata.io/openapi.json); <c>ApiKeyLimitExceeded</c> is
    /// accepted too because the API has been observed to send it and the spec
    /// is not exhaustive.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> QuotaExhaustedCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ApiLimitExceeded", "ApiKeyLimitExceeded", "RegistrationLimitExceeded",
        };

    /// <summary>
    /// Error code on a 429 → the exception to throw. None of them is worth
    /// retrying, but the remedy differs: <c>ApiLimitExceeded</c> is the
    /// account's whole credit pool, <c>ApiKeyLimitExceeded</c> is this one
    /// key's own cap, and <c>RegistrationLimitExceeded</c> is the real-time
    /// registration slots — it keeps the base exception, since it is not about
    /// credits at all.
    /// </summary>
    public static readonly IReadOnlyDictionary<
            string, Func<string, int, string?, int, NewsdataRateLimitException>>
        QuotaExhaustedErrors =
            new Dictionary<string, Func<string, int, string?, int, NewsdataRateLimitException>>(
                StringComparer.Ordinal)
            {
                ["ApiLimitExceeded"] = (m, s, b, r) =>
                    new NewsdataApiLimitExceededException(m, s, b, r),
                ["ApiKeyLimitExceeded"] = (m, s, b, r) =>
                    new NewsdataApiKeyLimitExceededException(m, s, b, r),
                ["RegistrationLimitExceeded"] = (m, s, b, r) =>
                    new NewsdataRateLimitException(m, s, b, r),
            };

    public static readonly IReadOnlySet<string> RequiresDateRange =
        new HashSet<string> { "count", "crypto_count", "market_count" };

    public static readonly IReadOnlySet<string> BoolParams =
        new HashSet<string>
        {
            "full_content", "image", "video", "removeduplicate",
            // v2 spellings
            "content", "remove_duplicate",
        };

    public static readonly IReadOnlySet<string> IntParams =
        new HashSet<string> { "size" };

    public static readonly IReadOnlySet<string> FloatParams =
        new HashSet<string> { "sentiment_score" };

    /// <summary>
    /// Mutually-exclusive parameter groups. Setting more than one member of a
    /// group is rejected before the request is sent.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> MutexGroups = new[]
    {
        new[] { "q", "qintitle", "qinmeta" },
        new[] { "country", "excludecountry" },
        new[] { "category", "excludecategory" },
        new[] { "language", "excludelanguage" },
        new[] { "domain", "domainurl", "excludedomain", "excludedomainurl" },
        // v2 names
        new[] { "q", "q_in_title", "q_in_meta" },
        new[] { "country", "exclude_country" },
        new[] { "category", "exclude_category" },
        new[] { "language", "exclude_language" },
        new[] { "source_url", "exclude_source_url" },
    };

    /// <summary>Per-endpoint accepted parameters (lowercase API names).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Filters =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["latest"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "country", "excludecountry", "category",
                "excludecategory", "language", "excludelanguage", "domain", "domainurl",
                "excludedomain", "excludedomainurl", "prioritydomain", "timeframe", "timezone",
                "size", "full_content", "image", "video", "page", "tag", "sentiment", "region",
                "excludefield", "removeduplicate", "id", "organization", "url", "sort",
                "creator", "datatype", "sentiment_score",
            },
            ["archive"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "country", "excludecountry", "category",
                "excludecategory", "language", "excludelanguage", "domain", "domainurl",
                "excludedomain", "excludedomainurl", "prioritydomain", "timezone", "size",
                "full_content", "image", "video", "page", "from_date", "to_date",
                "excludefield", "id", "url", "sort", "tag", "sentiment", "sentiment_score",
                "region", "organization", "creator", "datatype", "removeduplicate",
            },
            ["crypto"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "language", "excludelanguage", "domain",
                "domainurl", "excludedomain", "excludedomainurl", "prioritydomain",
                "timeframe", "timezone", "size", "full_content", "image", "video", "page",
                "tag", "sentiment", "coin", "excludefield", "from_date", "to_date",
                "removeduplicate", "id", "url", "sort",
            },
            ["sources"] = new HashSet<string>
            {
                "country", "category", "language", "prioritydomain", "domainurl",
            },
            ["market"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "from_date", "to_date", "country",
                "excludecountry", "domain", "domainurl", "excludedomain", "excludedomainurl",
                "language", "excludelanguage", "prioritydomain", "timezone", "timeframe",
                "size", "full_content", "image", "video", "page", "tag", "sentiment",
                "excludefield", "removeduplicate", "organization", "market_id", "id", "url",
                "sort", "creator", "datatype", "sentiment_score",
            },
            ["count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "country",
                "excludecountry", "category", "excludecategory", "language", "excludelanguage",
                "domain", "domainurl", "excludedomain", "excludedomainurl", "full_content",
                "image", "video", "prioritydomain", "page", "size", "sort", "interval", "tag",
                "sentiment", "sentiment_score", "region", "organization", "creator",
                "datatype", "removeduplicate",
            },
            ["crypto_count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "language",
                "excludelanguage", "coin", "domain", "domainurl", "excludedomain",
                "excludedomainurl", "full_content", "image", "video", "prioritydomain", "page",
                "sentiment", "size", "sort", "tag", "interval", "removeduplicate",
            },
            ["market_count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "country",
                "excludecountry", "domain", "domainurl", "excludedomain", "excludedomainurl",
                "language", "excludelanguage", "full_content", "image", "video",
                "organization", "market_id", "prioritydomain", "page", "sentiment",
                "removeduplicate", "size", "sort", "tag", "interval", "creator", "datatype",
                "sentiment_score",
            },

            // ---- v2 ----------------------------------------------------
            // v2 renames the filter vocabulary to snake_case and folds
            // domain/domainurl/excludedomain/excludedomainurl into
            // source_url/exclude_source_url.
            ["news_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "country", "exclude_country", "category",
                "exclude_category", "language", "exclude_language", "source_url",
                "exclude_source_url", "source_priority", "from_date", "to_date", "timeframe",
                "timezone", "size", "content", "image", "video", "page", "tag", "sentiment",
                "sentiment_score", "region", "organization", "creator", "datatype",
                "exclude_field", "remove_duplicate", "id", "url", "sort",
            },
            ["market_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "country", "exclude_country", "language",
                "exclude_language", "source_url", "exclude_source_url", "source_priority",
                "from_date", "to_date", "timeframe", "timezone", "size", "content", "image",
                "video", "page", "tag", "sentiment", "sentiment_score", "organization",
                "market_id", "creator", "datatype", "exclude_field", "remove_duplicate", "id",
                "url", "sort",
            },
            ["crypto_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "coin", "language", "exclude_language",
                "source_url", "exclude_source_url", "source_priority", "from_date", "to_date",
                "timeframe", "timezone", "size", "content", "image", "video", "page", "tag",
                "sentiment", "exclude_field", "remove_duplicate", "id", "url", "sort",
            },
            ["sources_v2"] = new HashSet<string>
            {
                "country", "category", "language", "source_priority", "source_url",
            },
            ["count_news_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "country", "exclude_country", "category",
                "exclude_category", "language", "exclude_language", "source_url",
                "exclude_source_url", "source_priority", "from_date", "to_date", "timeframe",
                "interval", "size", "content", "image", "video", "page", "tag", "sentiment",
                "sentiment_score", "region", "organization", "creator", "datatype",
                "remove_duplicate", "sort",
            },
            ["count_market_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "country", "exclude_country", "language",
                "exclude_language", "source_url", "exclude_source_url", "source_priority",
                "from_date", "to_date", "timeframe", "interval", "size", "content", "image",
                "video", "page", "tag", "sentiment", "sentiment_score", "organization",
                "market_id", "creator", "datatype", "remove_duplicate", "sort",
            },
            ["count_crypto_v2"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "coin", "language", "exclude_language",
                "source_url", "exclude_source_url", "source_priority", "from_date", "to_date",
                "timeframe", "interval", "size", "content", "image", "video", "page", "tag",
                "sentiment", "remove_duplicate", "sort",
            },

            // Real-time query registration. The real-time service is v2 only,
            // so these are the v2 filter names. No date or paging filters
            // apply — a registered query matches news as it is published.
            // news_type is set by WebsocketRegisterAsync, not by the caller.
            ["websocket_register"] = new HashSet<string>
            {
                "q", "q_in_title", "q_in_meta", "country", "exclude_country", "category",
                "exclude_category", "language", "exclude_language", "source_url",
                "exclude_source_url", "source_priority", "timezone", "content", "image",
                "video", "remove_duplicate", "tag", "sentiment", "sentiment_score",
                "region", "organization", "creator", "datatype", "exclude_field",
                "news_type",
            },
            ["websocket_fetch"]  = new HashSet<string>(),
            ["websocket_delete"] = new HashSet<string> { "registration_id" },
        };
}
