using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>The closed route set this SDK speaks, with each route's signing profile.</summary>
/// <remarks>
/// A mirror of the frozen contract, held here so a drift test can diff it against
/// <c>contracts/partner-public-v1.json</c> in both directions — a route the SDK calls that the contract
/// does not publish, and a published route the SDK cannot reach. Without it, "the SDK covers the surface"
/// is an assertion nobody checks.
///
/// The server holds the same map and is the authority over it.
/// </remarks>
internal static class PartnerRoutes
{
    /// <summary>Method, path template and profile of every route the SDK calls.</summary>
    public static IReadOnlyList<PartnerRoute> All { get; } =
    [
        new("GET", "/v1/profile", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/catalog/categories", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards", SignatureProfile.SafeRead),
        new("POST", "/v1/wallets/{walletId}/orders", SignatureProfile.OrderMutation),
        new("GET", "/v1/orders/{operationId}", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/cards", SignatureProfile.SafeRead),
        new("GET", "/v1/wallets/{walletId}/cards/{soldCardId}", SignatureProfile.SafeRead),
        new("POST", "/v1/wallets/{walletId}/cards/{soldCardId}/reveal", SignatureProfile.BodylessNonceMutation),
        new("POST", "/v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal", SignatureProfile.BodylessNonceMutation),
        new("POST", "/v1/diagnostics/signature", SignatureProfile.BodylessNonceMutation),

        // Unsigned branches: enrollment carries a bearer-style token, the key document carries nothing.
        new("GET", "/v1/enrollments/{invitationId}", null),
        new("POST", "/v1/enrollments/{invitationId}/keys", null),
        new("POST", "/v1/enrollments/{invitationId}/proof", null),
        new("GET", "/v1/enrollments/{invitationId}/status", null),
        new("GET", "/.well-known/partner-signing-keys.json", null),
    ];
}

/// <summary>One route of the closed public set.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Template">The path template as the contract publishes it.</param>
/// <param name="Profile">The signing profile, or null on the two unsigned branches.</param>
internal sealed record PartnerRoute(string Method, string Template, SignatureProfile? Profile);
