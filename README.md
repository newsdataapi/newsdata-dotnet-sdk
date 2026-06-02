<div align="center">

# Newsdata.io .NET SDK

[![NuGet](https://img.shields.io/nuget/v/Newsdata.Api.svg?logo=nuget&color=004880)](https://www.nuget.org/packages/Newsdata.Api)
[![NuGet downloads](https://img.shields.io/nuget/dt/Newsdata.Api.svg?color=004880)](https://www.nuget.org/packages/Newsdata.Api)
[![CI](https://img.shields.io/github/actions/workflow/status/newsdataapi/newsdata-dotnet-sdk/ci.yml?branch=main&logo=github&label=CI)](https://github.com/newsdataapi/newsdata-dotnet-sdk/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)

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
<PackageReference Include="Newsdata.Api" Version="0.0.1" />
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
│   └── NewsdataServerException             (5xx)
└── NewsdataNetworkException                (.InnerException)
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
    BaseUrl = "https://staging.example/api/1/",
    IncludeHeaders = true,
    HttpClient = myInjectedClient,     // proxies, mTLS, etc.
    Logger = (level, msg) => Console.WriteLine($"{level}: {msg}"),
});
```

Retries cover network errors, HTTP 429, and 5xx. 429 honours the
`Retry-After` header (integer seconds or HTTP-date); otherwise backoff is
exponential. Auth (401/403) and other 4xx errors are never retried. The
API key is redacted from any URL passed to the logger.

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

## License

[MIT](./LICENSE)
