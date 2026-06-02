using System.Collections;
using System.Globalization;
using System.Text;
using Newsdata.Api.Exceptions;

namespace Newsdata.Api;

/// <summary>
/// Client-side parameter validation and normalization, mirroring the
/// Python/PHP/Node/Go/Dart/Java clients:
///
/// <list type="bullet">
///   <item>keys are lowercased (the API is case-insensitive);</item>
///   <item>null values are dropped;</item>
///   <item>enumerables are comma-joined; booleans become "1"/"0";</item>
///   <item><c>size</c> must be an integer within bounds;</item>
///   <item><c>sentiment_score</c> must be numeric and requires <c>sentiment</c>;</item>
///   <item>mutually-exclusive groups are rejected;</item>
///   <item>unknown parameters for the endpoint are rejected;</item>
///   <item><c>rawQuery</c>, when present, must be the only parameter and is
///         parsed and checked against the endpoint's allowed keys.</item>
/// </list>
/// </summary>
internal static class Validator
{
    public static Dictionary<string, string> ValidateAndEncode(string endpoint, IDictionary<string, object?> raw)
    {
        if (!Constants.Filters.TryGetValue(endpoint, out var allowed))
            throw new NewsdataValidationException($"unknown endpoint: {endpoint}");

        // Lowercase keys; drop nulls.
        var lowered = new Dictionary<string, object>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            if (value is null) continue;
            lowered[key.ToLowerInvariant()] = value;
        }

        // rawQuery is mutually exclusive with every other parameter.
        if (lowered.TryGetValue("rawquery", out var rawQueryValue))
        {
            lowered.Remove("rawquery");
            if (lowered.Count > 0)
            {
                var keys = lowered.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                throw new NewsdataValidationException(
                    $"rawQuery cannot be combined with other parameters; got rawQuery and [{string.Join(", ", keys)}]",
                    "rawQuery");
            }
            if (rawQueryValue is not string s)
                throw new NewsdataValidationException("rawQuery must be a string", "rawQuery");
            return ParseRawQuery(s, allowed);
        }

        // Count endpoints require an explicit date range.
        if (Constants.RequiresDateRange.Contains(endpoint))
        {
            foreach (var required in new[] { "from_date", "to_date" })
            {
                if (!lowered.TryGetValue(required, out var v) || (v is string str && str.Length == 0))
                    throw new NewsdataValidationException(
                        $"{required} is required for the {endpoint} endpoint",
                        required);
            }
        }

        // Mutually-exclusive groups.
        foreach (var group in Constants.MutexGroups)
        {
            var present = group.Where(lowered.ContainsKey).ToList();
            if (present.Count > 1)
                throw new NewsdataValidationException(
                    $"these parameters are mutually exclusive: [{string.Join(", ", present)}]",
                    present[0]);
        }

        // sentiment_score requires sentiment.
        if (lowered.ContainsKey("sentiment_score") && !lowered.ContainsKey("sentiment"))
            throw new NewsdataValidationException(
                "sentiment_score requires sentiment to be set",
                "sentiment_score");

        // Per-param validation + coercion.
        var output = new Dictionary<string, string>(lowered.Count);
        foreach (var (name, value) in lowered)
        {
            if (!allowed.Contains(name))
                throw new NewsdataValidationException(
                    $"unsupported parameter for the {endpoint} endpoint: {name}",
                    name);
            output[name] = Coerce(name, value);
        }
        return output;
    }

    private static string Coerce(string name, object value)
    {
        if (Constants.BoolParams.Contains(name)) return CoerceBool(name, value);
        if (Constants.IntParams.Contains(name)) return CoerceInt(name, value);
        if (Constants.FloatParams.Contains(name)) return CoerceFloat(name, value);
        return CoerceString(name, value);
    }

    private static string CoerceBool(string name, object value)
    {
        switch (value)
        {
            case bool b: return b ? "1" : "0";
            case int i when i is 0 or 1: return i.ToString(CultureInfo.InvariantCulture);
            case string s:
                var v = s.Trim().ToLowerInvariant();
                if (v is "1" or "true" or "yes") return "1";
                if (v is "0" or "false" or "no") return "0";
                break;
        }
        throw new NewsdataValidationException($"{name} must be a boolean", name);
    }

    private static string CoerceInt(string name, object value)
    {
        int n;
        if (value is int i) n = i;
        else if (value is long l) n = checked((int)l);
        else if (value is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            n = parsed;
        else
            throw new NewsdataValidationException($"{name} must be an integer", name);

        if (name == "size" && (n < Constants.SizeMin || n > Constants.SizeMax))
            throw new NewsdataValidationException(
                $"size must be between {Constants.SizeMin} and {Constants.SizeMax} (got {n})",
                "size");
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static string CoerceFloat(string name, object value)
    {
        switch (value)
        {
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case decimal dec: return dec.ToString(CultureInfo.InvariantCulture);
            case int i: return i.ToString(CultureInfo.InvariantCulture);
            case long l: return l.ToString(CultureInfo.InvariantCulture);
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                return s;
        }
        throw new NewsdataValidationException($"{name} must be a number", name);
    }

    private static string CoerceString(string name, object value)
    {
        if (value is string s) return s;
        if (value is IEnumerable enumerable && value is not IDictionary)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var item in enumerable)
            {
                if (item is null)
                    throw new NewsdataValidationException($"all items in {name} must be non-null", name);
                if (!first) sb.Append(',');
                first = false;
                if (item is string si) sb.Append(si);
                else if (item is IFormattable fmt) sb.Append(fmt.ToString(null, CultureInfo.InvariantCulture));
                else sb.Append(item);
            }
            return sb.ToString();
        }
        if (value is IFormattable formatable)
            return formatable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString() ?? throw new NewsdataValidationException($"{name} could not be converted to string", name);
    }

    private static Dictionary<string, string> ParseRawQuery(string rawQuery, IReadOnlySet<string> allowed)
    {
        if (string.IsNullOrEmpty(rawQuery))
            throw new NewsdataValidationException("rawQuery must be a non-empty string", "rawQuery");

        // Pull just the query string out if it's a full URL.
        var query = rawQuery;
        if (Uri.TryCreate(rawQuery, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
            query = uri.Query;
        if (query.StartsWith('?')) query = query[1..];

        var result = new Dictionary<string, string>();
        if (query.Length == 0) return result;

        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            var eq = pair.IndexOf('=');
            string keyRaw, valueRaw;
            if (eq < 0) { keyRaw = pair; valueRaw = ""; }
            else { keyRaw = pair[..eq]; valueRaw = pair[(eq + 1)..]; }

            var key = Uri.UnescapeDataString(keyRaw).Trim().ToLowerInvariant();
            var value = Uri.UnescapeDataString(valueRaw);
            if (key.Length == 0) continue;
            if (key == "apikey") continue; // supplied by the client
            if (!allowed.Contains(key))
                throw new NewsdataValidationException($"unknown parameter in rawQuery: {key}", key);
            if (value.Length == 0)
                throw new NewsdataValidationException($"parameter {key} in rawQuery must have a value", key);
            result[key] = value;
        }
        return result;
    }
}
