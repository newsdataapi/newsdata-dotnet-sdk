namespace Newsdata.Api;

/// <summary>
/// Endpoint identifiers used by <see cref="NewsDataApiClient"/> pagination
/// helpers.
/// </summary>
public static class Endpoint
{
    /// <summary><c>/1/latest</c> — real-time news.</summary>
    public const string Latest = "latest";

    /// <summary><c>/1/archive</c> — historical news.</summary>
    public const string Archive = "archive";

    /// <summary><c>/1/crypto</c> — cryptocurrency news.</summary>
    public const string Crypto = "crypto";

    /// <summary><c>/1/sources</c> — available news sources (single page).</summary>
    public const string Sources = "sources";

    /// <summary><c>/1/market</c> — market / financial news.</summary>
    public const string Market = "market";

    /// <summary><c>/1/count</c> — aggregate counts (requires <c>from_date</c>/<c>to_date</c>).</summary>
    public const string Count = "count";

    /// <summary><c>/1/crypto/count</c> — aggregate crypto counts.</summary>
    public const string CryptoCount = "crypto_count";

    /// <summary><c>/1/market/count</c> — aggregate market counts.</summary>
    public const string MarketCount = "market_count";

    /// <summary><c>/1/websocket/register</c> — register a real-time query.</summary>
    public const string WebsocketRegister = "websocket_register";

    /// <summary><c>/1/websocket/fetch</c> — list registered real-time queries.</summary>
    public const string WebsocketFetch = "websocket_fetch";

    /// <summary><c>/1/websocket/delete</c> — delete a registered real-time query.</summary>
    public const string WebsocketDelete = "websocket_delete";
}
