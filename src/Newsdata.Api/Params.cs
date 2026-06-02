namespace Newsdata.Api;

/// <summary>
/// Fluent builder for endpoint parameter dictionaries.
///
/// <see cref="With"/> drops null values for ergonomic chaining, so optional
/// fields can be assigned without if-guards. The type inherits from
/// <see cref="Dictionary{TKey, TValue}"/> so an instance can be passed
/// anywhere a <c>IDictionary&lt;string, object&gt;</c> is expected.
/// </summary>
/// <example>
/// <code>
/// var resp = await client.LatestAsync(Params.Of()
///     .With("q", "bitcoin")
///     .With("country", new[] { "us", "gb" })
///     .With("language", new[] { "en" }));
/// </code>
/// </example>
public class Params : Dictionary<string, object?>
{
    /// <summary>Empty params.</summary>
    public static Params Of() => new();

    /// <summary>Params seeded from an existing dictionary.</summary>
    public static Params From(IDictionary<string, object?>? source)
    {
        var p = new Params();
        if (source is null) return p;
        foreach (var kv in source) p[kv.Key] = kv.Value;
        return p;
    }

    /// <summary>
    /// Add a key/value. Returns this for chaining. If <paramref name="value"/>
    /// is null, the key is left out (no-op).
    /// </summary>
    public Params With(string key, object? value)
    {
        if (value is not null) this[key] = value;
        return this;
    }
}
