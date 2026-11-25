using System.Text.Json;

using Newsdata.Api;

using Xunit;

namespace Newsdata.Api.Tests;

/// <summary>The v2 article layout: field names, groups, and the plan-notice wrinkle.</summary>
public class ArticleV2Tests
{
    /// <summary>
    /// One article exactly as API v2 sends it, every group populated. The
    /// real-time stream sends this same layout.
    /// </summary>
    private const string V2Article = """
        {
          "id": "9f8e7d6c5b4a39281706f5e4d3c2b1a0",
          "title": "Central bank holds rates steady as inflation cools",
          "meta_description": "The central bank kept its key rate unchanged on Wednesday.",
          "content": "Full article text...",
          "url": "https://www.example.com/business/central-bank-holds-rates",
          "published_at": "2026-10-22T06:15:00Z",
          "fetched_at": "2026-10-22T06:18:42Z",
          "datatype": "news",
          "language": "english",
          "country": ["india"],
          "category": ["business"],
          "keyword": ["interest rates", "inflation"],
          "creator": ["Jane Doe"],
          "duplicate": false,
          "media": {"image_url": "https://www.example.com/images/rates.jpg", "video_url": null},
          "source": {
            "name": "Example News",
            "url": "https://www.example.com",
            "icon_url": "https://www.example.com/icon.png",
            "priority": 1520
          },
          "ai": {
            "summary": "The central bank left its policy rate unchanged.",
            "sentiment": {"label": "neutral", "score": {"negative": 4.12, "neutral": 90.33, "positive": 5.55}},
            "tag": ["banking and finance", "economy"],
            "region": ["mumbai,maharashtra,india", "delhi,india"],
            "organization": ["reserve bank of india"]
          }
        }
        """;

    private static ArticleV2 Parse(string json) =>
        JsonSerializer.Deserialize<ArticleV2>(json)!;

    private static NewsdataResponse Envelope(string resultsJson) =>
        new() { Status = "success", Results = JsonDocument.Parse(resultsJson).RootElement.Clone() };

    [Fact]
    public void v2_spells_the_top_level_fields_id_url_published_at()
    {
        var a = Parse(V2Article);

        Assert.Equal("9f8e7d6c5b4a39281706f5e4d3c2b1a0", a.Id);
        Assert.Equal("Central bank holds rates steady as inflation cools", a.Title);
        Assert.Equal("https://www.example.com/business/central-bank-holds-rates", a.Url);
        Assert.Equal("2026-10-22T06:15:00Z", a.PublishedAt);
        Assert.Equal("2026-10-22T06:18:42Z", a.FetchedAt);
        Assert.Equal("The central bank kept its key rate unchanged on Wednesday.", a.MetaDescription);
        Assert.Equal("Full article text...", a.Content);
        Assert.Equal("english", a.Language);
        Assert.Equal("news", a.DataType);
        Assert.False(a.Duplicate);
        Assert.Equal(new[] { "india" }, a.Country);
        Assert.Equal(new[] { "business" }, a.Category);
        Assert.Equal(new[] { "interest rates", "inflation" }, a.Keyword);
        Assert.Equal(new[] { "Jane Doe" }, a.Creator);
    }

    [Fact]
    public void source_media_and_ai_arrive_as_groups()
    {
        var a = Parse(V2Article);

        Assert.Equal("Example News", a.Source!.Name);
        Assert.Equal("https://www.example.com", a.Source.Url);
        Assert.Equal("https://www.example.com/icon.png", a.Source.IconUrl);
        Assert.Equal(1520, a.Source.Priority);

        Assert.Equal("https://www.example.com/images/rates.jpg", a.Media!.ImageUrl);
        Assert.Null(a.Media.VideoUrl);

        Assert.Equal("The central bank left its policy rate unchanged.", a.Ai!.Summary);
        Assert.Equal("neutral", a.Ai.Sentiment!.Label);
        Assert.Equal(90.33, a.Ai.Sentiment.Score["neutral"]);
        Assert.Equal(4.12, a.Ai.Sentiment.Score["negative"]);
        Assert.Equal(new[] { "banking and finance", "economy" }, a.Ai.Tag);
        Assert.Equal(new[] { "reserve bank of india" }, a.Ai.Organization);
    }

    [Fact]
    public void ai_region_entries_contain_commas_and_are_not_split()
    {
        var a = Parse(V2Article);
        Assert.Equal(new[] { "mumbai,maharashtra,india", "delhi,india" }, a.Ai!.Region);
    }

    [Fact]
    public void an_absent_group_is_null()
    {
        var a = Parse("""{"id":"x","title":"hi"}""");

        Assert.Equal("x", a.Id);
        Assert.Null(a.Source);
        Assert.Null(a.Media);
        Assert.Null(a.Ai);
        Assert.Null(a.Duplicate);
        Assert.Null(a.Country);
    }

    [Fact]
    public void an_unknown_field_is_ignored()
    {
        var a = Parse("""{"id":"x","something_new":42}""");
        Assert.Equal("x", a.Id);
    }

    // ---- plan notices ----------------------------------------------------

    [Fact]
    public void a_plan_notice_where_a_list_is_documented_does_not_fail_decoding()
    {
        var a = Parse("""{"id":"x","ai":{"tag":"ONLY AVAILABLE IN PROFESSIONAL AND CORPORATE PLANS"}}""");

        Assert.Single(a.Ai!.Tag!);
        Assert.True(ArticleV2.IsPlanNotice(a.Ai.Tag![0]));
    }

    [Fact]
    public void a_plan_notice_replacing_the_sentiment_object_lands_in_its_label()
    {
        var a = Parse("""{"id":"x","ai":{"sentiment":"ONLY AVAILABLE IN PAID PLANS"}}""");

        Assert.True(ArticleV2.IsPlanNotice(a.Ai!.Sentiment!.Label));
        Assert.Empty(a.Ai.Sentiment.Score);
    }

    [Fact]
    public void a_plan_notice_inside_the_sentiment_label_is_recognised_too()
    {
        var a = Parse("""{"id":"x","ai":{"sentiment":{"label":"ONLY AVAILABLE IN PAID PLANS"}}}""");
        Assert.True(ArticleV2.IsPlanNotice(a.Ai!.Sentiment!.Label));
    }

    [Theory]
    [InlineData("ONLY AVAILABLE IN PAID PLANS")]
    [InlineData("ONLY AVAILABLE IN PROFESSIONAL AND CORPORATE PLANS")]
    [InlineData("  only available in corporate plans  ")]
    public void plan_notices_are_recognised(string value) =>
        Assert.True(ArticleV2.IsPlanNotice(value));

    [Theory]
    [InlineData("")]
    [InlineData("india")]
    [InlineData("neutral")]
    [InlineData("available in paid plans")]
    [InlineData(null)]
    public void real_values_are_not_plan_notices(string? value) =>
        Assert.False(ArticleV2.IsPlanNotice(value));

    // ---- lenient numbers -------------------------------------------------

    [Theory]
    [InlineData("""{"source":{"priority":1520}}""", 1520)]
    [InlineData("""{"source":{"priority":"1520"}}""", 1520)]
    [InlineData("""{"source":{"priority":null}}""", null)]
    [InlineData("""{"source":{"priority":"ONLY AVAILABLE IN PAID PLANS"}}""", null)]
    public void source_priority_accepts_a_number_a_quoted_number_or_neither(string json, int? expected) =>
        Assert.Equal(expected, Parse(json).Source!.Priority);

    // ---- the sentiment score map ----------------------------------------

    [Fact]
    public void a_class_the_platform_adds_later_is_still_decoded()
    {
        var a = Parse("""{"ai":{"sentiment":{"label":"mixed","score":{"mixed":51.0}}}}""");
        Assert.Equal(51.0, a.Ai!.Sentiment!.Score["mixed"]);
    }

    [Fact]
    public void a_sentiment_without_a_score_still_has_an_empty_one()
    {
        var a = Parse("""{"ai":{"sentiment":{"label":"neutral"}}}""");
        Assert.Equal("neutral", a.Ai!.Sentiment!.Label);
        Assert.Empty(a.Ai.Sentiment.Score);
    }

    // ---- through the envelope -------------------------------------------

    [Fact]
    public void GetArticlesV2_decodes_the_v2_envelopes_results()
    {
        var response = Envelope($"[{V2Article}]");

        var articles = response.GetArticlesV2();

        Assert.Single(articles);
        Assert.Equal("Example News", articles[0].Source!.Name);
        Assert.Equal("https://www.example.com/business/central-bank-holds-rates", articles[0].Url);
    }

    [Fact]
    public void GetArticlesV2_on_an_aggregate_page_is_empty()
    {
        var response = Envelope("""{"2026-10-22":5}""");
        Assert.Empty(response.GetArticlesV2());
    }

    [Fact]
    public void GetArticlesV2_with_no_results_at_all_is_empty()
    {
        var response = new NewsdataResponse { Status = "success" };
        Assert.Empty(response.GetArticlesV2());
    }

    // ---- round trip ------------------------------------------------------

    [Fact]
    public void an_article_survives_a_serialize_deserialize_round_trip()
    {
        var original = Parse(V2Article);

        var round = JsonSerializer.Deserialize<ArticleV2>(JsonSerializer.Serialize(original))!;

        Assert.Equal(original.Id, round.Id);
        Assert.Equal(original.Url, round.Url);
        Assert.Equal(original.Source!.Priority, round.Source!.Priority);
        Assert.Equal(original.Ai!.Sentiment!.Label, round.Ai!.Sentiment!.Label);
        Assert.Equal(original.Ai.Sentiment.Score["neutral"], round.Ai.Sentiment.Score["neutral"]);
        Assert.Equal(original.Ai.Region, round.Ai.Region);
    }
}
