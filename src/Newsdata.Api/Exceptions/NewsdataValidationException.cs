namespace Newsdata.Api.Exceptions;

/// <summary>
/// A user-provided parameter failed client-side validation. No request was
/// sent.
/// </summary>
public sealed class NewsdataValidationException : NewsdataException
{
    /// <summary>The offending parameter name, when known.</summary>
    public string? Param { get; }

    public NewsdataValidationException(string message, string? param = null) : base(message)
    {
        Param = param;
    }
}
