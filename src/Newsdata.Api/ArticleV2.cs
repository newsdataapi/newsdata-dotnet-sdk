using System.Text.Json;
using System.Text.Json.Serialization;

namespace Newsdata.Api;

/// <summary>
/// A single news article returned by API v2.
///
/// <para>v2 renamed and regrouped the v1 fields: <c>article_id</c> became
/// <c>id</c>, <c>link</c> became <c>url</c>, <c>pubDate</c> became
/// <c>published_at</c>, <c>description</c> became <c>meta_description</c>, and
/// the source, media and AI fields moved into the <c>Source</c>, <c>Media</c>
/// and <c>Ai</c> groups. Use <see cref="ArticleV2"/> for the v2 endpoints and
/// the real-time stream; use <see cref="Article"/> for the v1 endpoints.</para>
///
/// <para>Record type — immutable; access fields via property syntax. JSON keys
/// are the API's own field paths, the same names <c>exclude_field</c> accepts
/// and the same names the NewsData documentation uses, so one vocabulary
/// covers parameters, results and docs.</para>
///
/// <para>A field your plan does not include holds the API's notice string
/// instead of data; see <see cref="IsPlanNotice"/>.</para>
/// </summary>
/// <param name="Id">The article's stable identifier (v1: <c>article_id</c>).</param>
/// <param name="Title">Headline.</param>
/// <param name="Url">Address of the article (v1: <c>link</c>).</param>
/// <param name="MetaDescription">The publisher's summary (v1: <c>description</c>).</param>
/// <param name="Content">Full text, when the plan and the request include it.</param>
/// <param name="PublishedAt">Publication time, UTC ISO-8601 (v1: <c>pubDate</c>).</param>
/// <param name="FetchedAt">When the platform fetched the article, UTC ISO-8601.</param>
/// <param name="Language">Language name, e.g. <c>"english"</c>.</param>
/// <param name="DataType">Feed the article came from, e.g. <c>"news"</c>.</param>
/// <param name="Duplicate">Whether the platform considers the article a duplicate.</param>
/// <param name="Country">Countries the article relates to.</param>
/// <param name="Category">Categories the article was classified into.</param>
/// <param name="Keyword">Publisher keywords (v1: <c>keywords</c>).</param>
/// <param name="Creator">Bylines.</param>
/// <param name="Coin">Cryptocurrencies mentioned (crypto endpoints).</param>
/// <param name="Ticker">Stock tickers mentioned (v1: <c>symbol</c>).</param>
/// <param name="MarketId">Market identifiers (market endpoints).</param>
/// <param name="Source">The publisher the article came from.</param>
/// <param name="Media">Image and video addresses.</param>
/// <param name="Ai">The platform's AI enrichment.</param>
public sealed record ArticleV2(
    [property: JsonPropertyName("id")]               string? Id,
    [property: JsonPropertyName("title")]            string? Title,
    [property: JsonPropertyName("url")]              string? Url,
    [property: JsonPropertyName("meta_description")] string? MetaDescription,
    [property: JsonPropertyName("content")]          string? Content,
    [property: JsonPropertyName("published_at")]     string? PublishedAt,
    [property: JsonPropertyName("fetched_at")]        string? FetchedAt,
    [property: JsonPropertyName("language")]          string? Language,
    [property: JsonPropertyName("datatype")]          string? DataType,
    [property: JsonPropertyName("duplicate")]         bool? Duplicate,
    [property: JsonPropertyName("country"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Country,
    [property: JsonPropertyName("category"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Category,
    [property: JsonPropertyName("keyword"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Keyword,
    [property: JsonPropertyName("creator"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Creator,
    [property: JsonPropertyName("coin"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Coin,
    [property: JsonPropertyName("ticker"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Ticker,
    [property: JsonPropertyName("market_id"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? MarketId,
    [property: JsonPropertyName("source")]            ArticleV2Source? Source,
    [property: JsonPropertyName("media")]             ArticleV2Media? Media,
    [property: JsonPropertyName("ai")]                ArticleV2Ai? Ai)
{
    /// <summary>
    /// Prefix of the string the API sends in place of a field your plan does
    /// not include (<c>"ONLY AVAILABLE IN PAID PLANS"</c>, <c>"ONLY AVAILABLE
    /// IN PROFESSIONAL AND CORPORATE PLANS"</c>, <c>"ONLY AVAILABLE IN
    /// CORPORATE PLANS"</c>).
    /// </summary>
    internal const string PlanNoticePrefix = "ONLY AVAILABLE IN";

    /// <summary>
    /// Whether a value is the API's "not in your plan" notice rather than real
    /// data. The notice arrives in the field itself, so a withheld list field
    /// holds the single notice string and a withheld sentiment carries it in
    /// <see cref="ArticleV2Sentiment.Label"/>.
    /// </summary>
    /// <param name="value">A field value, possibly <c>null</c>.</param>
    /// <returns><c>true</c> when the value is a plan notice.</returns>
    public static bool IsPlanNotice(string? value) =>
        value is not null &&
        value.Trim().StartsWith(PlanNoticePrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The publisher an article came from — the v2 <c>source</c> group. It
/// replaces v1's <c>source_id</c>, <c>source_name</c>, <c>source_url</c>,
/// <c>source_icon</c> and <c>source_priority</c> fields.
/// </summary>
/// <param name="Name">Publisher name, e.g. <c>"Example News"</c>.</param>
/// <param name="Url">The publisher's home page.</param>
/// <param name="IconUrl">The publisher's icon.</param>
/// <param name="Priority">
/// The platform's source-priority rank (lower ranks first), or <c>null</c>
/// when the API did not send a usable number.
/// </param>
public sealed record ArticleV2Source(
    [property: JsonPropertyName("name")]     string? Name,
    [property: JsonPropertyName("url")]      string? Url,
    [property: JsonPropertyName("icon_url")] string? IconUrl,
    [property: JsonPropertyName("priority"), JsonConverter(typeof(LenientIntConverter))]
    int? Priority);

/// <summary>
/// An article's media addresses — the v2 <c>media</c> group. It replaces v1's
/// <c>image_url</c> and <c>video_url</c> fields. Either property is
/// <c>null</c> on an article without that medium.
/// </summary>
/// <param name="ImageUrl">Lead image.</param>
/// <param name="VideoUrl">Lead video.</param>
public sealed record ArticleV2Media(
    [property: JsonPropertyName("image_url")] string? ImageUrl,
    [property: JsonPropertyName("video_url")] string? VideoUrl);

/// <summary>
/// The platform's AI enrichment of an article — the v2 <c>ai</c> group. It
/// replaces v1's <c>sentiment</c>, <c>sentiment_stats</c>, <c>ai_tag</c>,
/// <c>ai_region</c> and <c>ai_org</c> fields, and adds a summary.
///
/// <para>Properties are <c>null</c> or empty on articles the platform did not
/// enrich. When your plan does not include a field, it holds the API's notice
/// string instead of data — see <see cref="ArticleV2.IsPlanNotice"/>.</para>
/// </summary>
/// <param name="Summary">A one-or-two-sentence summary of the article.</param>
/// <param name="Sentiment">Sentiment label and per-class confidence.</param>
/// <param name="Tag">Topic tags, e.g. <c>["banking and finance"]</c>.</param>
/// <param name="Region">
/// Places the article relates to, each a comma-separated path from the
/// narrowest to the widest (<c>"mumbai,maharashtra,india"</c>) — entries
/// contain commas themselves, so do not split them.
/// </param>
/// <param name="Organization">Organisations mentioned.</param>
public sealed record ArticleV2Ai(
    [property: JsonPropertyName("summary")]   string? Summary,
    [property: JsonPropertyName("sentiment")] ArticleV2Sentiment? Sentiment,
    [property: JsonPropertyName("tag"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Tag,
    [property: JsonPropertyName("region"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Region,
    [property: JsonPropertyName("organization"), JsonConverter(typeof(StringListConverter))]
    IReadOnlyList<string>? Organization);

/// <summary>
/// An article's sentiment classification: a label (<c>"positive"</c>,
/// <c>"neutral"</c>, <c>"negative"</c>) and the per-class confidence score.
///
/// <para><c>Score</c> is a dictionary rather than three properties so a class
/// the platform adds later is still decoded; the keys today are
/// <c>"negative"</c>, <c>"neutral"</c> and <c>"positive"</c>.</para>
///
/// <para>When your plan does not include sentiment, the API sends its notice
/// string in place of the object. It is decoded into <c>Label</c> with an
/// empty score, where <see cref="ArticleV2.IsPlanNotice"/> recognises
/// it.</para>
/// </summary>
/// <param name="Label">The winning class, or a plan notice.</param>
/// <param name="Score">Confidence per class, as percentages. Never <c>null</c>.</param>
[JsonConverter(typeof(ArticleV2SentimentConverter))]
public sealed record ArticleV2Sentiment(
    string? Label,
    IReadOnlyDictionary<string, double> Score);

/// <summary>
/// Reads an <see cref="ArticleV2Sentiment"/> from either the sentiment object
/// or the bare notice string the API sends when the plan does not include
/// sentiment.
/// </summary>
internal sealed class ArticleV2SentimentConverter : JsonConverter<ArticleV2Sentiment?>
{
    private static readonly IReadOnlyDictionary<string, double> EmptyScore =
        new Dictionary<string, double>();

    /// <inheritdoc/>
    public override ArticleV2Sentiment? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new ArticleV2Sentiment(reader.GetString(), EmptyScore);

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        string? label = root.TryGetProperty("label", out var labelNode) &&
                        labelNode.ValueKind == JsonValueKind.String
            ? labelNode.GetString()
            : null;

        var score = new Dictionary<string, double>();
        if (root.TryGetProperty("score", out var scoreNode) &&
            scoreNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in scoreNode.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number &&
                    entry.Value.TryGetDouble(out var number))
                {
                    score[entry.Name] = number;
                }
            }
        }

        return new ArticleV2Sentiment(label, score);
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer, ArticleV2Sentiment? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (value.Label is not null)
            writer.WriteString("label", value.Label);
        if (value.Score.Count > 0)
        {
            writer.WriteStartObject("score");
            foreach (var entry in value.Score)
                writer.WriteNumber(entry.Key, entry.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// Reads a string list that the API may also send as a single string.
///
/// <para>The v2 list fields (<c>country</c>, <c>keyword</c>, <c>ai.tag</c>, …)
/// are JSON arrays, but the API replaces the whole field with one "not in your
/// plan" notice string when the plan does not include it. Plain list
/// deserialization fails on that; this yields a one-element list instead,
/// which <see cref="ArticleV2.IsPlanNotice"/> recognises.</para>
/// </summary>
internal sealed class StringListConverter : JsonConverter<IReadOnlyList<string>?>
{
    /// <inheritdoc/>
    public override IReadOnlyList<string>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var single = reader.GetString();
            return string.IsNullOrEmpty(single)
                ? Array.Empty<string>()
                : new[] { single };
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return null;
        }

        var items = new List<string>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return items;
                case JsonTokenType.String:
                    var item = reader.GetString();
                    if (item is not null) items.Add(item);
                    break;
                case JsonTokenType.Null:
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return items;
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<string>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Reads an <c>int?</c> the API may send as a number or as a quoted number. A
/// value that is neither (a plan notice, say) reads as <c>null</c> rather than
/// failing the whole article.
/// </summary>
internal sealed class LenientIntConverter : JsonConverter<int?>
{
    /// <inheritdoc/>
    public override int? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var number)
                    ? number
                    : reader.TryGetDouble(out var real) ? (int)real : null;
            case JsonTokenType.String:
                var text = reader.GetString();
                return int.TryParse(text?.Trim(), out var parsed) ? parsed : null;
            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}
