<div align="center">

![Newsdata.io logo](https://raw.githubusercontent.com/newsdataapi/newsdata-dotnet-sdk/main/newsdata-logo.png)

# Newsdata.io .NET SDK

[![NuGet](https://img.shields.io/nuget/v/Newsdata.Api.svg?logo=nuget&color=004880)](https://www.nuget.org/packages/Newsdata.Api)
[![NuGet downloads](https://img.shields.io/nuget/dt/Newsdata.Api.svg?color=004880)](https://www.nuget.org/packages/Newsdata.Api)
[![CI](https://img.shields.io/github/actions/workflow/status/newsdataapi/newsdata-dotnet-sdk/ci.yml?branch=main&logo=github&label=CI)](https://github.com/newsdataapi/newsdata-dotnet-sdk/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)
[![OpenAPI](https://img.shields.io/badge/OpenAPI-3.1-85EA2D)](https://newsdata.io/openapi.json)

</div>

Official .NET client (SDK) for the [Newsdata.io](https://newsdata.io) News
API. Async methods for every endpoint (`latest`, `archive`, `sources`,
`crypto`, `market`, `count`, `crypto/count`, `market/count`) with
client-side parameter validation, retries with exponential backoff,
`IAsyncEnumerable`-based pagination, and a typed exception hierarchy.

Built on .NET 8's `HttpClient` and `System.Text.Json` — **zero runtime
dependencies**. Thread-safe.

## Installation

```bash
dotnet add package Newsdata.Api
```

Or `csproj`:
```xml
<PackageReference Include="Newsdata.Api" Version="0.2.1" />
```

## Quickstart

```csharp
using Newsdata.Api;
using Newsdata.Api.Exceptions;

using var client = new NewsDataApiClient(
    Environment.GetEnvironmentVariable("NEWSDATA_API_KEY")!);

var resp = await client.LatestAsync(Params.Of()
    .With("q", "bitcoin")
    .With("country", new[] { "us", "gb" })
    .With("language", new[] { "en" }));

foreach (var article in resp.GetArticles())
{
    Console.WriteLine($"{article.Title} — {article.Link}");
}
```

## Endpoints

The API has two versions. v2 is the current one; the v1 methods keep working
unchanged.

### v2

| Method | Endpoint | Notes |
|--------|----------|-------|
| `client.NewsV2Async(params)` | `/2/news` | Latest news, or historical with `from_date` / `to_date` |
| `client.MarketV2Async(params)` | `/2/market` | Market / financial news |
| `client.CryptoV2Async(params)` | `/2/crypto` | Cryptocurrency news |
| `client.SourcesV2Async(params)` | `/2/sources` | Available sources (single page) |
| `client.CountNewsV2Async(params)` | `/2/count/news` | Aggregate news counts |
| `client.CountMarketV2Async(params)` | `/2/count/market` | Aggregate market counts |
| `client.CountCryptoV2Async(params)` | `/2/count/crypto` | Aggregate crypto counts |
| `client.WebsocketRegisterAsync(params)` | `/2/websocket/register` | Register a real-time query |
| `client.WebsocketFetchAsync()` | `/2/websocket/fetch` | List registered queries |
| `client.WebsocketDeleteAsync(id)` | `/2/websocket/delete` | Delete a registered query |

v2 renames the filter vocabulary to snake_case and folds the four domain
filters into two:

| v1 | v2 |
|----|----|
| `qInTitle`, `qInMeta` | `q_in_title`, `q_in_meta` |
| `excludecountry`, `excludecategory`, `excludelanguage` | `exclude_country`, `exclude_category`, `exclude_language` |
| `domain`, `domainurl`, `excludedomain`, `excludedomainurl` | `source_url`, `exclude_source_url` |
| `prioritydomain` | `source_priority` |
| `full_content` | `content` |
| `removeduplicate`, `excludefield` | `remove_duplicate`, `exclude_field` |

`coin` and `market_id` keep their names.

```csharp
var resp = await client.NewsV2Async(Params.Of()
    .With("q_in_title", "tesla")
    .With("source_url", "bbc.com")
    .With("content", true));
```

The v2 envelope spells its total and cursor `total_results` / `next_page`
where v1 spells them `totalResults` / `nextPage`. `NewsdataResponse` reads
either, so `TotalResults`, `NextPage`, `ScrollAllAsync` and `PaginateAsync`
behave the same whichever version answered. Pass `Endpoint.NewsV2` and
friends to the pagination helpers.

There is no `/2/archive` — `NewsV2Async` covers historical news through
`from_date` / `to_date`.

#### v2 articles

v2 renamed and regrouped the article fields, so the v2 endpoints and the
real-time stream decode with `GetArticlesV2()` and `ArticleV2`, while the v1
endpoints keep `GetArticles()` and `Article`:

```csharp
var resp = await client.NewsV2Async(Params.Of().With("q", "bitcoin"));

foreach (var a in resp.GetArticlesV2())
{
    Console.WriteLine($"{a.Title} {a.Url} {a.PublishedAt}");
    Console.WriteLine($"{a.Source?.Name} {a.Source?.Priority}");
    Console.WriteLine(a.Media?.ImageUrl);
    Console.WriteLine($"{a.Ai?.Summary} {a.Ai?.Sentiment?.Label}");
}
```

| v1 (`Article`) | v2 (`ArticleV2`) |
|----------------|------------------|
| `ArticleId` | `Id` |
| `Link` | `Url` |
| `PubDate` | `PublishedAt` |
| `Description` | `MetaDescription` |
| `Keywords` | `Keyword` |
| `Symbol` | `Ticker` |
| `SourceName`, `SourceUrl`, `SourcePriority`, `SourceIcon` | `Source.Name`, `Source.Url`, `Source.Priority`, `Source.IconUrl` |
| `ImageUrl`, `VideoUrl` | `Media.ImageUrl`, `Media.VideoUrl` |
| `Sentiment`, `SentimentStats` | `Ai.Sentiment.Label`, `Ai.Sentiment.Score` |
| `AiTag`, `AiRegion`, `AiOrg` | `Ai.Tag`, `Ai.Region`, `Ai.Organization` |
| — | `Ai.Summary`, `FetchedAt`, `Duplicate`, `Coin`, `MarketId` |

A group the API did not send (`Source`, `Media`, `Ai`) is `null`, as is a list
field it did not send. `Ai.Region` entries are comma-separated paths from the
narrowest place to the widest (`"mumbai,maharashtra,india"`), so do not split
them on commas.

A field your plan does not include arrives as the API's notice string in place
of the data (`"ONLY AVAILABLE IN PAID PLANS"`). Decoding tolerates it — a list
field holds the single notice, and a withheld sentiment carries it in `Label` —
and `ArticleV2.IsPlanNotice(value)` tells it apart from real data.

### v1

| Method | Endpoint | Notes |
|--------|----------|-------|
| `client.LatestAsync(params)` | `/1/latest` | Real-time news |
| `client.ArchiveAsync(params)` | `/1/archive` | Historical news |
| `client.SourcesAsync(params)` | `/1/sources` | Available sources (single page) |
| `client.CryptoAsync(params)` | `/1/crypto` | Cryptocurrency news |
| `client.MarketAsync(params)` | `/1/market` | Market / financial news |
| `client.CountAsync(params)` | `/1/count` | Aggregate counts (requires `from_date`, `to_date`) |
| `client.CryptoCountAsync(params)` | `/1/crypto/count` | Aggregate crypto counts |
| `client.MarketCountAsync(params)` | `/1/market/count` | Aggregate market counts |

The real-time service is v2 only — there are no v1 WebSocket methods.

`params` is `IDictionary<string, object?>` — pass a `Params` instance for
fluent construction. Values may be `string`, `int`, `double`, `bool`, or
`IEnumerable<string>` (sent comma-joined). Parameter names are
case-insensitive — `qInTitle` and `qintitle` are equivalent.

Every method accepts an optional `CancellationToken` as the last argument.

## Pagination

```csharp
// 1) ScrollAllAsync: follow nextPage cursors and return one merged response.
var all = await client.ScrollAllAsync(
    Endpoint.Latest,
    Params.Of().With("q", "news"),
    maxResults: 200);   // 0 = no cap

// 2) Paginate: idiomatic IAsyncEnumerable.
await foreach (var page in client.Paginate(
    Endpoint.Latest, Params.Of().With("q", "news")))
{
    foreach (var article in page.GetArticles())
        Process(article);
}
```

Use `break` (or a cancelled `CancellationToken`) to stop the `await foreach`
early.

## Raw query

```csharp
await client.LatestAsync(Params.Of()
    .With("rawQuery", "q=bitcoin&country=us&language=en"));
```

`rawQuery` is mutually exclusive with every other parameter and is parsed
and validated against the endpoint's allowed keys before the request leaves.

## Client-side validation

A `NewsdataValidationException` is thrown — before any HTTP request — when:

- a parameter is not accepted by that endpoint;
- mutually-exclusive parameters are set together — `q`/`qInTitle`/`qInMeta`,
  `country`/`excludecountry`, `category`/`excludecategory`,
  `language`/`excludelanguage`, `domain`/`domainurl`/`excludedomain`;
- `size` is outside 1–50;
- `sentiment_score` is set without `sentiment`;
- a count endpoint is missing `from_date` or `to_date`.

Booleans for `full_content`, `image`, `video`, and `removeduplicate` are
coerced to `"1"`/`"0"`.

## Real-time news (WebSocket)

The real-time service is part of API v2. Register a query first — the returned
`registration_id` identifies it from then on:

```csharp
var registered = await client.WebsocketRegisterAsync(
    Params.Of().With("q", "bitcoin").With("language", "en"));
var registrationId = registered.Results.GetProperty("registration_id").GetString()!;
```

`WebsocketRegisterAsync` takes the same filter names as `NewsV2Async` (`q`,
`q_in_title`, `country`, `exclude_country`, `source_url`, `content`,
`exclude_field`, `timezone`, …) — the date, paging and sorting filters, `id`
and `url` don't apply to a standing query. Registering an identical query twice
throws `NewsdataApiException` with status 409; the existing id is in its
response body.

`WebsocketFetchAsync()` lists every registered query: `Results` is an array
with one entry per query, carrying its `registration_id`, the filters it was
registered with (`query`), how many devices are streaming it now
(`active_clients`) and when one last connected (`last_used`, UTC).
`WebsocketDeleteAsync(id)` removes one. Once every registration the plan allows
is in use, registering another throws a 429 `NewsdataRateLimitException`
carrying the code `RegistrationLimitExceeded`; it is not retried, since only
deleting a registration frees a slot.

Then stream. Each response carries `Status`, `TotalResults` and `Results`, and
its articles use the v2 layout, so decode them with `GetArticlesV2()`:

```csharp
using var ws = new NewsDataApiWebSocket(client);

await foreach (var response in ws.StreamAsync(registrationId))
{
    foreach (var article in response.GetArticlesV2())
        Console.WriteLine($"{article.Title} - {article.Url} ({article.Source?.Name})");
}
```

Break out of the loop, cancel the token, or dispose the instance to stop; the
connection is closed either way.

Transient drops (network errors, server restarts, abnormal closes) are
reconnected automatically with a capped exponential backoff. Set
`Reconnect = false` to stop on the first disconnect instead. A permanent
rejection — bad API key or unknown
`registration_id`, exhausted API credits, or too many simultaneous devices — throws
`NewsdataWebSocketAuthException` and is **not** retried.

The server always accepts the handshake and then closes with code **1008** when
the connection is refused, carrying one of three reasons: `invalid credentials
or registration not found`, `api limit reached`, or `device limit reached` (more
than 5 devices on one `registration_id`). Every other close code — including
`1013` (`send timeout`, meaning the client read too slowly) — is transient and
reconnects.

**Each delivered article consumes 1 API credit per connected device.**

Catch it like any other client error:

```csharp
try
{
    await foreach (var response in ws.StreamAsync(registrationId)) { /* ... */ }
}
catch (NewsdataWebSocketAuthException e) { Console.Error.WriteLine($"rejected: {e.Message}"); }
catch (NewsdataWebSocketException e)     { Console.Error.WriteLine($"stream error: {e.Message}"); }
```

All connection options are optional:

```csharp
using var ws = new NewsDataApiWebSocket(client, new NewsDataApiWebSocketOptions
{
    BaseUrl           = "wss://ws.newsdata.io/ws/event",  // staging / self-hosted / proxied
    Reconnect         = true,                             // default true
    ReconnectDelay    = TimeSpan.FromSeconds(1),          // first delay; doubles each retry
    ReconnectDelayMax = TimeSpan.FromSeconds(30),         // cap on the delay
    HandshakeTimeout  = TimeSpan.FromSeconds(10),         // Zero disables
    Headers           = new Dictionary<string, string> { ["X-Trace"] = "abc" },
});
```

## Error handling

```csharp
using Newsdata.Api.Exceptions;

try
{
    await client.LatestAsync(Params.Of().With("q", "news"));
}
catch (NewsdataValidationException e) { /* bad param — e.Param */ }
catch (NewsdataAuthException e)       { /* 401 / 403 */ }
catch (NewsdataRateLimitException e)  { /* 429 — e.RetryAfter */ }
catch (NewsdataServerException e)     { /* 5xx */ }
catch (NewsdataApiException e)        { /* other API errors — e.StatusCode */ }
catch (NewsdataNetworkException e)    { /* transport — e.InnerException */ }
```

Hierarchy:

```
NewsdataException (extends Exception)
├── NewsdataValidationException             (.Param)
├── NewsdataApiException                    (.StatusCode, .ResponseBody)
│   ├── NewsdataAuthException               (401 / 403)
│   ├── NewsdataRateLimitException          (429; .RetryAfter)
│   │   ├── NewsdataApiLimitExceededException     (account credits gone)
│   │   └── NewsdataApiKeyLimitExceededException  (this key's cap gone)
│   │   (RegistrationLimitExceeded stays on NewsdataRateLimitException —
│   │    real-time registration slots, not credits)
│   └── NewsdataServerException             (5xx)
├── NewsdataNetworkException                (.InnerException)
└── NewsdataWebSocketException              (real-time stream)
    └── NewsdataWebSocketAuthException      (policy-violation close 1008)
```

## Configuration

```csharp
using var client = new NewsDataApiClient(new NewsDataApiClientOptions
{
    ApiKey = apiKey,
    Timeout = TimeSpan.FromSeconds(30),
    MaxRetries = 5,
    RetryBackoff = TimeSpan.FromSeconds(2),
    RetryBackoffMax = TimeSpan.FromSeconds(60),
    PaginationDelay = TimeSpan.FromSeconds(1),
    BaseUrl = "https://staging.example/api/",   // root, no version
    IncludeHeaders = true,
    HttpClient = myInjectedClient,     // proxies, mTLS, etc.
    Logger = (level, msg) => Console.WriteLine($"{level}: {msg}"),
});
```

`BaseUrl` is the API root **without** a version segment; the default is
`https://newsdata.io/api/` and the client adds `1/…` or `2/…` itself. Before
0.3.0 it included the version (`https://newsdata.io/api/1/`), so drop the
trailing `1/` if you override it.

Retries cover network errors, HTTP 429, and 5xx. 429 honours the
`Retry-After` header (integer seconds or HTTP-date); otherwise backoff is
exponential. Auth (401/403) and other 4xx errors are never retried. The
API key is redacted from any URL passed to the logger.

A 429 that waiting cannot fix is never retried.
`NewsdataApiLimitExceededException` means the account's whole credit pool is
exhausted, `NewsdataApiKeyLimitExceededException` means this one key hit its
own cap while the account may still have credits; both derive from
`NewsdataRateLimitException`, so an existing `catch` on that type keeps
working. `RegistrationLimitExceeded` is also never retried, but keeps the plain
`NewsdataRateLimitException` — it is about real-time registration slots, not
credits, and is fixed by deleting a registration.

## Concurrency

`NewsDataApiClient` is thread-safe — share one instance across your
application. Dispose it on shutdown (or use `using var`); the SDK only
disposes the underlying `HttpClient` if it created it.

## Development

```bash
dotnet build
dotnet test                        # 33 tests, no API key needed
dotnet pack -c Release             # produces .nupkg + .snupkg
```

Tests use a custom `HttpMessageHandler` mock — no WireMock, no network,
no external test dep.

## Releasing

Tag-push driven. Bump `<Version>` in `src/Newsdata.Api/Newsdata.Api.csproj`,
push a `v`-prefixed tag, and `publish.yml` does the rest:

```bash
git tag -a v0.0.2 -m "v0.0.2"
git push origin v0.0.2
```

See [`.github/workflows/publish.yml`](.github/workflows/publish.yml) for
the NuGet.org publishing flow.

## Related libraries

Official Newsdata.io clients across languages and runtimes:

- **Python** — [newsdataapi/python-client](https://github.com/newsdataapi/python-client) ([PyPI](https://pypi.org/project/newsdataapi/))
- **Node.js** — [newsdataapi/newsdata-nodejs-client](https://github.com/newsdataapi/newsdata-nodejs-client) ([npm](https://www.npmjs.com/package/newsdata-nodejs-client))
- **React (hooks)** — [newsdataapi/newsdata-reactjs-client](https://github.com/newsdataapi/newsdata-reactjs-client) ([npm](https://www.npmjs.com/package/newsdataapi))
- **PHP** — [newsdataapi/php-client](https://github.com/newsdataapi/php-client) ([Packagist](https://packagist.org/packages/newsdataio/newsdataapi))
- **Java** — [newsdataapi/newsdata-java-sdk](https://github.com/newsdataapi/newsdata-java-sdk) ([Maven Central](https://central.sonatype.com/artifact/io.newsdata/newsdataapi))
- **Go** — [newsdataapi/newsdata-go-client](https://github.com/newsdataapi/newsdata-go-client) ([pkg.go.dev](https://pkg.go.dev/github.com/newsdataapi/newsdata-go-client))
- **Dart / Flutter** — [newsdataapi/newsdata-flutter-client](https://github.com/newsdataapi/newsdata-flutter-client) ([pub.dev](https://pub.dev/packages/newsdataapi))
- **MCP Server (AI assistants)** — [newsdataapi/newsdata.io-mcp](https://github.com/newsdataapi/newsdata.io-mcp) ([PyPI](https://pypi.org/project/newsdata-mcp/))

Also see [free news datasets](https://github.com/newsdataapi/newsdata.io-free-datasets) for ML / NLP work.

## License

[MIT](./LICENSE)
