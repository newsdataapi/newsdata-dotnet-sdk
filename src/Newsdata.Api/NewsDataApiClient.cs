using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Newsdata.Api.Exceptions;

namespace Newsdata.Api;

/// <summary>
/// Synchronous-style async HTTP client for the Newsdata.io REST API.
///
/// <para>Construct with an API key (and optional <see cref="NewsDataApiClientOptions"/>).
/// All endpoint methods are async, take an optional <see cref="CancellationToken"/>,
/// and return a <see cref="NewsdataResponse"/>; failures throw subclasses of
/// <see cref="NewsdataException"/>.</para>
///
/// <para>Safe for concurrent use. Holds a single <see cref="HttpClient"/>
/// internally — disposing the client only closes that handle if the client
/// created it.</para>
/// </summary>
public sealed class NewsDataApiClient : IDisposable
{
    private readonly NewsDataApiClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private static readonly Regex ApiKeyRegex =
        new(@"(apikey=)[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public NewsDataApiClient(string apiKey, Action<NewsDataApiClientOptions>? configure = null)
        : this(BuildOptions(apiKey, configure)) { }

    public NewsDataApiClient(NewsDataApiClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new NewsdataValidationException("apiKey must be a non-empty string", "apiKey");

        if (_options.HttpClient is not null)
        {
            _httpClient = _options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = _options.Timeout };
            _ownsHttpClient = true;
        }

        if (!_options.BaseUrl.EndsWith('/'))
            _options.BaseUrl += "/";
    }

    private static NewsDataApiClientOptions BuildOptions(string apiKey, Action<NewsDataApiClientOptions>? configure)
    {
        var opts = new NewsDataApiClientOptions { ApiKey = apiKey };
        configure?.Invoke(opts);
        return opts;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    // ---- endpoint methods ----------------------------------------------

    /// <summary>Real-time news. GET /1/latest.</summary>
    public Task<NewsdataResponse> LatestAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Latest, p ?? Params.Of(), ct);

    /// <summary>Historical news. GET /1/archive.</summary>
    public Task<NewsdataResponse> ArchiveAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Archive, p ?? Params.Of(), ct);

    /// <summary>Cryptocurrency news. GET /1/crypto.</summary>
    public Task<NewsdataResponse> CryptoAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Crypto, p ?? Params.Of(), ct);

    /// <summary>Available news sources. Single page. GET /1/sources.</summary>
    public Task<NewsdataResponse> SourcesAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Sources, p ?? Params.Of(), ct);

    /// <summary>Market / financial news. GET /1/market.</summary>
    public Task<NewsdataResponse> MarketAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Market, p ?? Params.Of(), ct);

    /// <summary>Aggregate counts. Requires <c>from_date</c> and <c>to_date</c>. GET /1/count.</summary>
    public Task<NewsdataResponse> CountAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.Count, p ?? Params.Of(), ct);

    /// <summary>Aggregate crypto counts. Requires dates. GET /1/crypto/count.</summary>
    public Task<NewsdataResponse> CryptoCountAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.CryptoCount, p ?? Params.Of(), ct);

    /// <summary>Aggregate market counts. Requires dates. GET /1/market/count.</summary>
    public Task<NewsdataResponse> MarketCountAsync(IDictionary<string, object?>? p = null, CancellationToken ct = default)
        => RequestAsync(Endpoint.MarketCount, p ?? Params.Of(), ct);

    // ---- real-time query management ---------------------------------------

    /// <summary>
    /// Register a real-time WebSocket query. <c>POST /1/websocket/register</c>.
    /// <para>Takes the familiar filter names (<c>q</c>, <c>country</c>,
    /// <c>language</c>, <c>domain</c>, …); no date or paging filters apply,
    /// since a registered query matches news as it is published. The new
    /// query's id is at <c>results.registration_id</c> — pass it to
    /// <see cref="NewsDataApiWebSocket.StreamAsync"/>.</para>
    /// <para>Registering an identical query twice throws
    /// <see cref="Exceptions.NewsdataApiException"/> with status 409; the
    /// existing id is in its response body.</para>
    /// </summary>
    public Task<NewsdataResponse> WebsocketRegisterAsync(
        IDictionary<string, object?>? p = null, CancellationToken ct = default)
    {
        var withType = new Dictionary<string, object?>(p ?? Params.Of())
        {
            ["news_type"] = Constants.WsNewsType,
        };
        return RequestAsync(Endpoint.WebsocketRegister, withType, ct);
    }

    /// <summary>
    /// List the account's registered real-time queries.
    /// <c>GET /1/websocket/fetch</c>. One entry per query at
    /// <c>results.queries</c>.
    /// </summary>
    public Task<NewsdataResponse> WebsocketFetchAsync(CancellationToken ct = default)
        => RequestAsync(Endpoint.WebsocketFetch, Params.Of(), ct);

    /// <summary>
    /// Delete a registered real-time query. <c>DELETE /1/websocket/delete</c>.
    /// </summary>
    public Task<NewsdataResponse> WebsocketDeleteAsync(
        string registrationId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(registrationId))
            throw new NewsdataValidationException(
                "registrationId must be a non-empty string", "registration_id");
        var p = new Dictionary<string, object?> { ["registration_id"] = registrationId };
        return RequestAsync(Endpoint.WebsocketDelete, p, ct);
    }

    /// <summary>The API key, for the WebSocket handshake URL.</summary>
    internal string ApiKeyForWebSocket => _options.ApiKey;

    /// <summary>Forward a log line from the WebSocket layer.</summary>
    internal void LogFromWebSocket(string level, string message) => Log(level, message);

    // ---- pagination ----------------------------------------------------

    /// <summary>
    /// Follow <c>nextPage</c> cursors and return one merged response, capped
    /// at <paramref name="maxResults"/> articles (0 = no cap, follow to
    /// exhaustion).
    /// </summary>
    public async Task<NewsdataResponse> ScrollAllAsync(
        string endpoint,
        IDictionary<string, object?> p,
        int maxResults = 0,
        CancellationToken ct = default)
    {
        if (endpoint == Endpoint.Sources)
            throw new NewsdataValidationException("ScrollAllAsync is not supported for the sources endpoint");

        var req = new Params();
        foreach (var kv in p) req[kv.Key] = kv.Value;

        var merged = new List<JsonElement>();
        NewsdataResponse? last = null;
        int total = 0;

        while (true)
        {
            var resp = await RequestAsync(endpoint, req, ct).ConfigureAwait(false);
            last = resp;
            if (resp.TotalResults != 0) total = resp.TotalResults;

            if (resp.Results.ValueKind == JsonValueKind.Array)
                foreach (var item in resp.Results.EnumerateArray())
                    merged.Add(item.Clone());

            var nextPage = resp.NextPage;
            if (maxResults > 0 && merged.Count >= maxResults)
            {
                if (merged.Count > maxResults) merged.RemoveRange(maxResults, merged.Count - maxResults);
                nextPage = null;
            }
            if (string.IsNullOrEmpty(nextPage)) break;
            req["page"] = nextPage;
            await DelayAsync(_options.PaginationDelay, ct).ConfigureAwait(false);
        }

        var resultsJson = merged.Count > 0
            ? JsonSerializer.SerializeToElement(merged)
            : last?.Results ?? default;

        return new NewsdataResponse
        {
            Status = "success",
            TotalResults = total,
            Results = resultsJson,
            NextPage = null,
            Headers = _options.IncludeHeaders ? last?.Headers : null,
        };
    }

    /// <summary>
    /// Asynchronous enumeration over pages. Use with <c>await foreach</c>.
    /// Break out of the loop (or pass a cancelled <see cref="CancellationToken"/>)
    /// to stop early.
    /// </summary>
    public async IAsyncEnumerable<NewsdataResponse> Paginate(
        string endpoint,
        IDictionary<string, object?> p,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (endpoint == Endpoint.Sources)
            throw new NewsdataValidationException("Paginate is not supported for the sources endpoint");

        var req = new Params();
        foreach (var kv in p) req[kv.Key] = kv.Value;

        while (true)
        {
            var resp = await RequestAsync(endpoint, req, ct).ConfigureAwait(false);
            yield return resp;

            // Count endpoints return an object on the final page.
            if (resp.Results.ValueKind == JsonValueKind.Object) yield break;
            if (string.IsNullOrEmpty(resp.NextPage)) yield break;
            req["page"] = resp.NextPage;
            await DelayAsync(_options.PaginationDelay, ct).ConfigureAwait(false);
        }
    }

    // ---- internals -----------------------------------------------------

    private async Task<NewsdataResponse> RequestAsync(string endpoint, IDictionary<string, object?> rawParams, CancellationToken ct)
    {
        var encoded = Validator.ValidateAndEncode(endpoint, rawParams);
        encoded["apikey"] = _options.ApiKey;

        var path = Constants.EndpointPaths[endpoint];
        var url = _options.BaseUrl + path + "?" + BuildQueryString(encoded);
        var logUrl = RedactApiKey(url);
        var method = Constants.EndpointMethods.TryGetValue(endpoint, out var m) ? m : HttpMethod.Get;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= Math.Max(1, _options.MaxRetries); attempt++)
        {
            Log("info", $"{method.Method} {logUrl} (attempt {attempt}/{_options.MaxRetries})");

            HttpResponseMessage response;
            try
            {
                using var requestMessage = new HttpRequestMessage(method, url);
                requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                response = await _httpClient.SendAsync(requestMessage, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new NewsdataNetworkException("request cancelled", new OperationCanceledException());
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt >= _options.MaxRetries)
                    throw new NewsdataNetworkException($"network error: {ex.Message}", ex);
                Log("warn", $"network error: {ex.Message}");
                await DelayAsync(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            string body;
            using (response)
            {
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var status = (int)response.StatusCode;

                JsonDocument? doc = null;
                try
                {
                    doc = string.IsNullOrEmpty(body) ? null : JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    if (status >= 500 && attempt < _options.MaxRetries)
                    {
                        Log("warn", $"non-JSON response (status {status})");
                        await DelayAsync(Backoff(attempt), ct).ConfigureAwait(false);
                        continue;
                    }
                    throw new NewsdataApiException(
                        $"non-JSON response from API (status {status})", status, body);
                }

                using (doc)
                {
                    var hasResults = doc is not null
                        && doc.RootElement.TryGetProperty("results", out var resultsProbe)
                        && resultsProbe.ValueKind != JsonValueKind.Null
                        && resultsProbe.ValueKind != JsonValueKind.Undefined;

                    if (status == 200
                        && doc is not null
                        && doc.RootElement.TryGetProperty("status", out var statusEl)
                        && statusEl.ValueKind == JsonValueKind.String
                        && statusEl.GetString() == "success"
                        && (hasResults || Constants.ResultsOptional.Contains(endpoint)))
                    {
                        var resultsEl = hasResults
                            ? doc.RootElement.GetProperty("results")
                            : default;
                        return new NewsdataResponse
                        {
                            Status = "success",
                            TotalResults = doc.RootElement.TryGetProperty("totalResults", out var trEl)
                                && trEl.TryGetInt32(out var tr) ? tr : 0,
                            Results = hasResults ? resultsEl.Clone() : default,
                            NextPage = doc.RootElement.TryGetProperty("nextPage", out var npEl)
                                && npEl.ValueKind == JsonValueKind.String ? npEl.GetString() : null,
                            Headers = _options.IncludeHeaders ? response.Headers : null,
                        };
                    }

                    var message = ExtractErrorMessage(doc, status);

                    if (status == 429)
                    {
                        var retryAfter = ParseRetryAfter(response.Headers.RetryAfter);
                        if (attempt >= _options.MaxRetries)
                            throw new NewsdataRateLimitException(message, 429, body, retryAfter);
                        var wait = retryAfter > 0 ? TimeSpan.FromSeconds(retryAfter) : Backoff(attempt);
                        Log("warn", $"429 rate limit; sleeping {wait.TotalMilliseconds}ms");
                        await DelayAsync(wait, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (status >= 500)
                    {
                        if (attempt >= _options.MaxRetries)
                            throw new NewsdataServerException(message, status, body);
                        Log("warn", $"{status} server error");
                        await DelayAsync(Backoff(attempt), ct).ConfigureAwait(false);
                        continue;
                    }

                    if (status is 401 or 403)
                        throw new NewsdataAuthException(message, status, body);

                    // Other 4xx — never retried.
                    throw new NewsdataApiException(message, status, body);
                }
            }
        }
        throw new NewsdataException(
            $"request to {endpoint} did not complete (maxRetries={_options.MaxRetries}, lastError={lastError})");
    }

    private TimeSpan Backoff(int attempt)
    {
        var ms = _options.RetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var cappedMs = ms > _options.RetryBackoffMax.TotalMilliseconds || ms <= 0
            ? _options.RetryBackoffMax.TotalMilliseconds
            : ms;
        return TimeSpan.FromMilliseconds(cappedMs);
    }

    private static Task DelayAsync(TimeSpan duration, CancellationToken ct)
        => duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, ct);

    private void Log(string level, string message)
        => _options.Logger?.Invoke(level, $"[newsdataapi] {message}");

    private static string BuildQueryString(IDictionary<string, string> parameters)
    {
        var sb = new System.Text.StringBuilder();
        var first = true;
        foreach (var kv in parameters)
        {
            if (!first) sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }

    /// <summary>Replace the <c>apikey</c> parameter's value with <c>REDACTED</c>.</summary>
    public static string RedactApiKey(string url) =>
        ApiKeyRegex.Replace(url, "${1}REDACTED");

    internal static int ParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null) return 0;
        if (retryAfter.Delta is { } delta)
            return Math.Max(0, (int)delta.TotalSeconds);
        if (retryAfter.Date is { } date)
        {
            var seconds = (int)(date - DateTimeOffset.UtcNow).TotalSeconds;
            return Math.Max(0, seconds);
        }
        return 0;
    }

    private static string ExtractErrorMessage(JsonDocument? doc, int status)
    {
        if (doc is not null)
        {
            if (doc.RootElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Object
                && results.TryGetProperty("message", out var nestedMsg)
                && nestedMsg.ValueKind == JsonValueKind.String)
            {
                return nestedMsg.GetString() ?? $"API request failed with HTTP {status}";
            }
            if (doc.RootElement.TryGetProperty("message", out var rootMsg)
                && rootMsg.ValueKind == JsonValueKind.String)
            {
                return rootMsg.GetString() ?? $"API request failed with HTTP {status}";
            }
        }
        return $"API request failed with HTTP {status}";
    }
}

/// <summary>Configuration for <see cref="NewsDataApiClient"/>.</summary>
public sealed class NewsDataApiClientOptions
{
    /// <summary>Your Newsdata.io API key (required).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>API base URL. Default: <c>https://newsdata.io/api/1/</c>.</summary>
    public string BaseUrl { get; set; } = Constants.BaseUrl;

    /// <summary>Per-request timeout. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = Constants.DefaultRequestTimeout;

    /// <summary>Total attempts (1 = no retry). Default: 5.</summary>
    public int MaxRetries { get; set; } = Constants.DefaultMaxRetries;

    /// <summary>Base for exponential backoff between retries. Default: 2s.</summary>
    public TimeSpan RetryBackoff { get; set; } = Constants.DefaultRetryBackoff;

    /// <summary>Cap on any single backoff sleep. Default: 60s.</summary>
    public TimeSpan RetryBackoffMax { get; set; } = Constants.DefaultRetryBackoffMax;

    /// <summary>Delay between pages in scroll / paginate. Default: 1s.</summary>
    public TimeSpan PaginationDelay { get; set; } = Constants.DefaultPaginationDelay;

    /// <summary>If true, the response headers are attached to each <see cref="NewsdataResponse"/>.</summary>
    public bool IncludeHeaders { get; set; }

    /// <summary>
    /// Inject a custom <see cref="HttpClient"/> (proxies, mTLS, etc.).
    /// The SDK will not dispose it (the caller owns it).
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Optional callback for log lines: <c>(level, message)</c>. Levels are
    /// <c>"info"</c> / <c>"warn"</c>. The API key is redacted from logged URLs.
    /// </summary>
    public Action<string, string>? Logger { get; set; }
}
