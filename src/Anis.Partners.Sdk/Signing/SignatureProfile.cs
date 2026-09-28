namespace Anis.Partners.Sdk.Signing;

/// <summary>Which covered-component profile a request signs under.</summary>
/// <remarks>
/// The public surface has exactly three signed shapes. Which one a route uses is decided by the route,
/// never negotiated: Anis holds a closed method-plus-route-template map and refuses a caller-supplied
/// request kind that disagrees with it.
/// </remarks>
internal enum SignatureProfile
{
    /// <summary>Every GET. No body, no nonce, no idempotency key.</summary>
    SafeRead = 1,

    /// <summary>
    /// The two reveals and the signature diagnostic: a POST with a nonce, a <c>Content-Digest</c> and no
    /// operation identity. Reveals send zero body bytes; the diagnostic sends exactly <c>{}</c>.
    /// </summary>
    BodylessNonceMutation = 2,

    /// <summary>Order creation: the nonce mutation plus the caller-chosen operation identity.</summary>
    OrderMutation = 3,
}

/// <summary>The frozen covered-component orders, transcribed from the contract and verified against Anis's own route map.</summary>
/// <remarks>
/// These arrays are exhaustive and exact. No alternate order, label, algorithm, unquoted parameter form or
/// duplicate member is accepted anywhere, so there is nothing here to configure and nothing to negotiate.
///
/// Cross-checked against Anis's own route map, which carries the same three orders and is the authority,
/// because it is what refuses a signature.
///
/// Note where <c>idempotency-key</c> sits: BEFORE <c>x-anis-date</c>, not after. The contract keeps
/// <c>x-anis-date</c> last in every listing and defines the order profile as the nonce profile plus the
/// key, so the added component lands second-to-last.
/// </remarks>
internal static class SignatureProfiles
{
    /// <summary>RFC 9421 requires lower-cased field names, so these are the exact identifiers, ordinal.</summary>
    public static IReadOnlyList<string> SafeRead { get; } =
        ["@method", "@authority", "@path", "@query", "x-anis-date"];

    /// <inheritdoc cref="SafeRead"/>
    public static IReadOnlyList<string> BodylessNonceMutation { get; } =
        ["@method", "@authority", "@path", "@query", "content-digest", "nonce", "x-anis-date"];

    /// <inheritdoc cref="SafeRead"/>
    public static IReadOnlyList<string> OrderMutation { get; } =
        ["@method", "@authority", "@path", "@query", "content-digest", "nonce", "idempotency-key", "x-anis-date"];

    /// <exception cref="ArgumentOutOfRangeException">The profile is not one of the three signed shapes.</exception>
    public static IReadOnlyList<string> For(SignatureProfile profile) => profile switch
    {
        SignatureProfile.SafeRead => SafeRead,
        SignatureProfile.BodylessNonceMutation => BodylessNonceMutation,
        SignatureProfile.OrderMutation => OrderMutation,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
    };
}
