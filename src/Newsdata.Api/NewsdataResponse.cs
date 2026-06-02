using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Newsdata.Api;

/// <summary>
/// Top-level envelope returned by every endpoint.
///
/// <para><see cref="Results"/> is held as a raw <see cref="JsonElement"/>
/// because its shape varies by endpoint:</para>
/// <list type="bullet">
///   <item>News endpoints (latest, archive, crypto, market) return an array of articles.</item>
///   <item>Count endpoints return an aggregate object on the final page.</item>
/// </list>
/// Use <see cref="GetArticles"/> / <see cref="GetAggregate"/> to decode it.
/// </summary>
public sealed class NewsdataResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; init; }

    [JsonPropertyName("results")]
    public JsonElement Results { get; init; }

    [JsonPropertyName("nextPage")]
    public string? NextPage { get; init; }

    /// <summary>HTTP response headers, when <c>IncludeHeaders=true</c> was set on the client.</summary>
    [JsonIgnore]
    public HttpResponseHeaders? Headers { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Decode <see cref="Results"/> as a list of articles. Returns an empty
    /// list when <c>Results</c> is empty or not a JSON array.
    /// </summary>
    public IReadOnlyList<Article> GetArticles()
    {
        if (Results.ValueKind != JsonValueKind.Array)
            return Array.Empty<Article>();
        return Results.Deserialize<List<Article>>(JsonOptions) ?? new List<Article>();
    }

    /// <summary>
    /// Decode <see cref="Results"/> as a map (the shape count endpoints return
    /// on the final page). Returns <c>null</c> when <c>Results</c> is not an
    /// object.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? GetAggregate()
    {
        if (Results.ValueKind != JsonValueKind.Object)
            return null;
        var dict = new Dictionary<string, JsonElement>();
        foreach (var prop in Results.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }
}
