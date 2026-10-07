using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>The closed route set this SDK speaks, with each route's signing profile and whether Anis signs its answers.</summary>
/// <remarks>
/// A mirror of the frozen contract, held here so a drift test can diff it against
/// <c>contracts/partner-public-v1.json</c> in both directions — a route the SDK calls that the contract
/// does not publish, and a published route the SDK cannot reach. Without it, "the SDK covers the surface"
/// is an assertion nobody checks.
///
/// It is also what decides, at run time, whether an answer is verified (<see cref="Find"/>). Anis signs the
/// answers that move money, deliver card codes or establish a key, plus the signature self-check — every one of
/// them, success and refusal alike. The information routes answer unsigned. The decision is the route's, never
/// the response's: an answer is not verified because it happens to carry a signature, and a signed route whose
/// answer carries none is refused.
///
/// The server holds the same map and is the authority over it.
/// </remarks>
internal static class PartnerRoutes
{
    /// <summary>Method, path template, request profile and answer signing of every route the SDK calls.</summary>
    public static IReadOnlyList<PartnerRoute> All { get; } =
    [
        // Information: the request is signed, the answer is not.
        new("GET", "/v1/profile", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/catalog/categories", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/cards", SignatureProfile.SafeRead, SignsResponse: false),
        new("GET", "/v1/wallets/{walletId}/cards/{soldCardId}", SignatureProfile.SafeRead, SignsResponse: false),

        // Money, card codes and the self-check: both directions signed.
        new("POST", "/v1/wallets/{walletId}/orders", SignatureProfile.OrderMutation, SignsResponse: true),
        new("GET", "/v1/orders/{operationId}", SignatureProfile.SafeRead, SignsResponse: true),
        new("POST", "/v1/wallets/{walletId}/cards/{soldCardId}/reveal", SignatureProfile.BodylessNonceMutation, SignsResponse: true),
        new("POST", "/v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal", SignatureProfile.BodylessNonceMutation, SignsResponse: true),
        new("POST", "/v1/diagnostics/signature", SignatureProfile.BodylessNonceMutation, SignsResponse: true),

        // Unsigned requests: enrollment carries a bearer-style token, and its answers establish a key, so they are signed.
        new("GET", "/v1/enrollments/{invitationId}", null, SignsResponse: true),
        new("POST", "/v1/enrollments/{invitationId}/keys", null, SignsResponse: true),
        new("POST", "/v1/enrollments/{invitationId}/proof", null, SignsResponse: true),
        new("GET", "/v1/enrollments/{invitationId}/status", null, SignsResponse: true),

        // The key document carries nothing either way: verifying it would need the keys it is fetched to supply.
        new("GET", "/.well-known/partner-signing-keys.json", null, SignsResponse: false),
    ];

    private static readonly Dictionary<(string Method, string Template), PartnerRoute> ByRoute =
        All.ToDictionary(route => (route.Method, route.Template));

    /// <summary>The table's entry for a route the SDK is about to call.</summary>
    /// <exception cref="InvalidOperationException">
    /// The route is not in the table. A defect in this SDK, never a default: guessing would either verify an
    /// unsigned answer and refuse it, or pass a signed one through unverified.
    /// </exception>
    public static PartnerRoute Find(HttpMethod method, string template)
    {
        ArgumentNullException.ThrowIfNull(method);

        return ByRoute.TryGetValue((method.Method, template), out var route)
            ? route
            : throw new InvalidOperationException($"{method.Method} {template} is not in the Partner route table.");
    }
}

/// <summary>One route of the closed public set.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Template">The path template as the contract publishes it.</param>
/// <param name="Profile">The request signing profile, or null on the two unsigned request branches.</param>
/// <param name="SignsResponse">
/// Whether Anis signs this route's answers — every one of them, success and refusal — so the SDK verifies them
/// and refuses one that carries no signature. False: the answer is passed through unverified.
/// </param>
internal sealed record PartnerRoute(string Method, string Template, SignatureProfile? Profile, bool SignsResponse);
