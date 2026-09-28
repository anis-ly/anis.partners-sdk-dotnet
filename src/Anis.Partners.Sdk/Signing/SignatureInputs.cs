using System.Globalization;

namespace Anis.Partners.Sdk.Signing;

/// <summary>The request facts a signature base is built from, already in the exact form they are signed in.</summary>
/// <remarks>
/// The values are canonicalized once, here, and then used twice — to build the base and to write the
/// headers. Canonicalizing at the point of use instead would let the base and the wire disagree, which is
/// the one class of signing bug that produces a valid-looking request and an unexplainable 401.
/// </remarks>
internal sealed record SignatureInputs
{
    /// <summary>The HTTP method, upper-cased. The gateway upper-cases before building its envelope.</summary>
    public required string Method { get; init; }

    /// <summary>
    /// Host and, when it is not the scheme's default, port — LOWER-CASED. Anis lower-cases the authority
    /// it reconstructs, so a request signed over a mixed-case host verifies against nothing.
    /// </summary>
    public required string Authority { get; init; }

    /// <summary>The absolute path, exactly as it appears on the request line.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// The query string WITHOUT its leading question mark, exactly as transmitted — not re-ordered, not
    /// re-encoded, not normalized. The gateway forwards <c>QueryString.Value[1..]</c> verbatim.
    /// </summary>
    public required string CanonicalQuery { get; init; }

    /// <summary>The <c>X-Anis-Date</c> header value, ISO-8601 UTC.</summary>
    public required string AnisDate { get; init; }

    /// <summary>The <c>Content-Digest</c> value, on the two body-bearing profiles.</summary>
    public string? ContentDigest { get; init; }

    /// <summary>The <c>Nonce</c> header value, on the two nonce profiles.</summary>
    public string? Nonce { get; init; }

    /// <summary>The <c>Idempotency-Key</c> value, on the order profile only. Canonical UUID "D" form.</summary>
    public Guid? IdempotencyKey { get; init; }

    /// <summary>
    /// RFC 9421 §2.2.7: the covered value keeps its leading separator, and a request with NO query covers
    /// <c>?</c> rather than an empty string — otherwise "no query" and "query present but empty" would sign
    /// identically, and one could be substituted for the other.
    /// </summary>
    internal string QueryComponentValue => "?" + CanonicalQuery;

    internal string ValueOf(string component) => component switch
    {
        "@method" => Method,
        "@authority" => Authority,
        "@path" => Path,
        "@query" => QueryComponentValue,
        "content-digest" => ContentDigest ?? throw Missing(component),
        "nonce" => Nonce ?? throw Missing(component),
        "idempotency-key" => IdempotencyKey?.ToString("D", CultureInfo.InvariantCulture) ?? throw Missing(component),
        "x-anis-date" => AnisDate,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Not a covered component of any Anis profile."),
    };

    private static InvalidOperationException Missing(string component)
        => new($"The '{component}' component is covered by this profile but no value was supplied. "
             + "Every covered component must have a value: a base naming a component with nothing in it "
             + "verifies against nothing.");
}
