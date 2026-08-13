using System.Text.Json.Serialization;

namespace Newsdata.Api;

/// <summary>
/// A single news article returned by the Newsdata.io API.
///
/// <para>Record type — immutable; access fields via property syntax.
/// JSON keys use the API's snake_case; property names are PascalCase.</para>
/// </summary>
public sealed record Article(
    [property: JsonPropertyName("article_id")]    string ArticleId,
    [property: JsonPropertyName("title")]         string? Title,
    [property: JsonPropertyName("link")]          string? Link,
    [property: JsonPropertyName("description")]   string? Description,
    [property: JsonPropertyName("content")]       string? Content,
    [property: JsonPropertyName("keywords")]      IReadOnlyList<string>? Keywords,
    [property: JsonPropertyName("creator")]       IReadOnlyList<string>? Creator,
    [property: JsonPropertyName("video_url")]     string? VideoUrl,
    [property: JsonPropertyName("image_url")]     string? ImageUrl,
    [property: JsonPropertyName("pubDate")]       string? PubDate,
    [property: JsonPropertyName("pubDateTZ")]     string? PubDateTZ,
    [property: JsonPropertyName("source_id")]     string? SourceId,
    [property: JsonPropertyName("source_priority")] int? SourcePriority,
    [property: JsonPropertyName("source_url")]    string? SourceUrl,
    [property: JsonPropertyName("source_icon")]   string? SourceIcon,
    [property: JsonPropertyName("source_name")]   string? SourceName,
    [property: JsonPropertyName("language")]      string? Language,
    [property: JsonPropertyName("country")]       IReadOnlyList<string>? Country,
    [property: JsonPropertyName("category")]      IReadOnlyList<string>? Category,
    [property: JsonPropertyName("ai_tag")]        IReadOnlyList<string>? AiTag,
    [property: JsonPropertyName("ai_region")]     IReadOnlyList<string>? AiRegion,
    [property: JsonPropertyName("ai_org")]        IReadOnlyList<string>? AiOrg,
    [property: JsonPropertyName("sentiment")]     string? Sentiment,
    [property: JsonPropertyName("sentiment_stats")] System.Text.Json.JsonElement? SentimentStats,
    [property: JsonPropertyName("datatype")]      string? DataType,
    [property: JsonPropertyName("symbol")]        IReadOnlyList<string>? Symbol,
    [property: JsonPropertyName("market_id")]     IReadOnlyList<string>? MarketId);
