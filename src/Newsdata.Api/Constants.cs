using System.Net.Http;

namespace Newsdata.Api;

/// <summary>
/// Static configuration: base URL, endpoint paths, HTTP defaults, and the
/// per-endpoint accepted-parameter sets. Mirrors the server-side filter
/// mapping and the official Python/PHP/Node/Go/Dart/Java clients.
/// </summary>
internal static class Constants
{
    public const string BaseUrl = "https://newsdata.io/api/1/";

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
            ["latest"]       = "latest",
            ["crypto"]       = "crypto",
            ["archive"]      = "archive",
            ["sources"]      = "sources",
            ["market"]       = "market",
            ["count"]        = "count",
            ["crypto_count"] = "crypto/count",
            ["market_count"] = "market/count",
            ["websocket_register"] = "websocket/register",
            ["websocket_fetch"]    = "websocket/fetch",
            ["websocket_delete"]   = "websocket/delete",
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

    /// <summary>The feed a registered query matches against.</summary>
    public const string WsNewsType = "latest";

    /// <summary>Close code the server uses for a permanent connection rejection.</summary>
    public const int WsPolicyViolation = 1008;

    /// <summary>Wait before the first reconnect; doubles after each failure.</summary>
    public static readonly TimeSpan WsReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on the reconnect delay.</summary>
    public static readonly TimeSpan WsReconnectDelayMax = TimeSpan.FromSeconds(30);

    /// <summary>Bound on the opening handshake.</summary>
    public static readonly TimeSpan WsHandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Error codes on a 429 meaning the account's API credits are exhausted
    /// rather than a transient rate limit. These are never retried — waiting
    /// out the backoff cannot conjure more credits.
    /// <para><c>ApiLimitExceeded</c> is the documented code (see the ErrorCode
    /// enum in https://newsdata.io/openapi.json); <c>ApiKeyLimitExceeded</c> is
    /// accepted too because the API has been observed to send it and the spec
    /// is not exhaustive.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> QuotaExhaustedCodes =
        new HashSet<string>(StringComparer.Ordinal) { "ApiLimitExceeded", "ApiKeyLimitExceeded" };

    public static readonly IReadOnlySet<string> RequiresDateRange =
        new HashSet<string> { "count", "crypto_count", "market_count" };

    public static readonly IReadOnlySet<string> BoolParams =
        new HashSet<string> { "full_content", "image", "video", "removeduplicate" };

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
        new[] { "domain", "domainurl", "excludedomain" },
    };

    /// <summary>Per-endpoint accepted parameters (lowercase API names).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Filters =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["latest"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "country", "excludecountry", "category",
                "excludecategory", "language", "excludelanguage", "domain", "domainurl",
                "excludedomain", "prioritydomain", "timeframe", "timezone", "size",
                "full_content", "image", "video", "page", "tag", "sentiment", "region",
                "excludefield", "removeduplicate", "id", "organization", "url", "sort",
                "creator", "datatype", "sentiment_score",
            },
            ["archive"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "country", "excludecountry", "category",
                "excludecategory", "language", "excludelanguage", "domain", "domainurl",
                "excludedomain", "prioritydomain", "timezone", "size", "full_content",
                "image", "video", "page", "from_date", "to_date", "excludefield", "id",
                "url", "sort", "tag", "sentiment", "sentiment_score", "region",
                "organization", "creator", "datatype", "removeduplicate",
            },
            ["crypto"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "language", "excludelanguage", "domain",
                "domainurl", "excludedomain", "prioritydomain", "timeframe", "timezone",
                "size", "full_content", "image", "video", "page", "tag", "sentiment",
                "coin", "excludefield", "from_date", "to_date", "removeduplicate", "id",
                "url", "sort",
            },
            ["sources"] = new HashSet<string>
            {
                "country", "category", "language", "prioritydomain", "domainurl",
            },
            ["market"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "from_date", "to_date", "country",
                "excludecountry", "domain", "domainurl", "excludedomain", "language",
                "excludelanguage", "prioritydomain", "timezone", "timeframe", "size",
                "full_content", "image", "video", "page", "tag", "sentiment",
                "excludefield", "removeduplicate", "organization", "market_id", "id", "url",
                "sort", "creator", "datatype", "sentiment_score",
            },
            ["count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "country",
                "excludecountry", "category", "excludecategory", "language",
                "excludelanguage", "domain", "domainurl", "excludedomain", "full_content",
                "image", "video", "prioritydomain", "page", "size", "sort", "interval",
                "tag", "sentiment", "sentiment_score", "region", "organization", "creator",
                "datatype", "removeduplicate",
            },
            ["crypto_count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "language",
                "excludelanguage", "coin", "domain", "domainurl", "excludedomain",
                "full_content", "image", "video", "prioritydomain", "page", "sentiment",
                "size", "sort", "tag", "interval", "removeduplicate",
            },
            ["market_count"] = new HashSet<string>
            {
                "from_date", "to_date", "q", "qintitle", "qinmeta", "country",
                "excludecountry", "domain", "domainurl", "excludedomain", "language",
                "excludelanguage", "full_content", "image", "video", "organization",
                "market_id", "prioritydomain", "page", "sentiment", "removeduplicate", "size",
                "sort", "tag", "interval", "creator", "datatype", "sentiment_score",
            },
            // Real-time query registration. No date/paging filters — a
            // registered query matches news as it is published. news_type is
            // set by WebsocketRegisterAsync, not by the caller.
            ["websocket_register"] = new HashSet<string>
            {
                "q", "qintitle", "qinmeta", "country", "excludecountry", "category",
                "excludecategory", "language", "excludelanguage", "domain", "domainurl",
                "excludedomain", "prioritydomain", "timezone", "full_content", "image",
                "video", "removeduplicate", "tag", "sentiment", "sentiment_score",
                "region", "organization", "creator", "datatype", "excludefield",
                "news_type",
            },
            ["websocket_fetch"]  = new HashSet<string>(),
            ["websocket_delete"] = new HashSet<string> { "registration_id" },
        };
}
