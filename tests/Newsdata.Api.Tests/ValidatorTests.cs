using Newsdata.Api;
using Newsdata.Api.Exceptions;
using Xunit;

namespace Newsdata.Api.Tests;

public class ValidatorTests
{
    private static Dictionary<string, object?> P(params (string k, object? v)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [Fact]
    public void Arrays_are_comma_joined()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("country", new[] { "us", "gb" })));
        Assert.Equal("us,gb", out_["country"]);
    }

    [Fact]
    public void Booleans_are_coerced_to_flag()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("full_content", true), ("image", false)));
        Assert.Equal("1", out_["full_content"]);
        Assert.Equal("0", out_["image"]);
    }

    [Fact]
    public void Keys_are_lowercased()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("qInTitle", "hi")));
        Assert.Equal("hi", out_["qintitle"]);
    }

    [Fact]
    public void Null_values_are_dropped()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("q", "x"), ("country", null)));
        Assert.Equal("x", out_["q"]);
        Assert.False(out_.ContainsKey("country"));
    }

    [Fact]
    public void Size_upper_bound_rejected()
    {
        var ex = Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("size", Constants.SizeMax + 1))));
        Assert.Equal("size", ex.Param);
    }

    [Fact]
    public void Size_within_bounds_accepted()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("size", 50)));
        Assert.Equal("50", out_["size"]);
    }

    [Fact]
    public void Mutually_exclusive_params_rejected()
    {
        Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("q", "a"), ("qInTitle", "b"))));
    }

    [Fact]
    public void Unknown_parameter_rejected()
    {
        var ex = Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("nope", "x"))));
        Assert.Equal("nope", ex.Param);
    }

    [Fact]
    public void Crypto_rejects_country()
    {
        Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("crypto", P(("country", "us"))));
    }

    [Fact]
    public void SentimentScore_requires_sentiment()
    {
        var ex = Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("sentiment_score", 0.5))));
        Assert.Equal("sentiment_score", ex.Param);
    }

    [Fact]
    public void SentimentScore_with_sentiment_accepted()
    {
        var out_ = Validator.ValidateAndEncode("latest",
            P(("sentiment", "positive"), ("sentiment_score", 0.5)));
        Assert.Equal("positive", out_["sentiment"]);
        Assert.Equal("0.5", out_["sentiment_score"]);
    }

    [Fact]
    public void Count_requires_date_range()
    {
        Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("count", P(("q", "x"))));
    }

    [Fact]
    public void Count_with_dates_accepted()
    {
        var out_ = Validator.ValidateAndEncode("count",
            P(("from_date", "2024-01-01"), ("to_date", "2024-01-02")));
        Assert.Equal("2024-01-01", out_["from_date"]);
        Assert.Equal("2024-01-02", out_["to_date"]);
    }

    [Fact]
    public void RawQuery_is_parsed_and_validated()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("rawQuery", "q=foo&country=us")));
        Assert.Equal("foo", out_["q"]);
        Assert.Equal("us", out_["country"]);
    }

    [Fact]
    public void RawQuery_rejects_other_params()
    {
        Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest",
                P(("rawQuery", "q=foo"), ("country", "us"))));
    }

    [Fact]
    public void RawQuery_rejects_unknown_keys()
    {
        Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("rawQuery", "bogus=1"))));
    }

    [Fact]
    public void RawQuery_ignores_embedded_apikey()
    {
        var out_ = Validator.ValidateAndEncode("latest", P(("rawQuery", "apikey=secret&q=foo")));
        Assert.Equal("foo", out_["q"]);
        Assert.False(out_.ContainsKey("apikey"));
    }

    [Fact]
    public void RawQuery_accepts_full_url()
    {
        var out_ = Validator.ValidateAndEncode("latest",
            P(("rawQuery", "https://newsdata.io/api/1/latest?q=foo&language=en")));
        Assert.Equal("foo", out_["q"]);
        Assert.Equal("en", out_["language"]);
    }

    [Fact]
    public void Validation_error_exposes_param_name()
    {
        var ex = Assert.Throws<NewsdataValidationException>(
            () => Validator.ValidateAndEncode("latest", P(("size", 999))));
        Assert.Equal("size", ex.Param);
    }
}
