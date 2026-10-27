using System.Net;
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
/// answers that move money, deliver card codes or establish or stop access to a key or application, plus the signature self-check — every one of
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
        new("GET", "/v1/profile", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.profile.read", RequiredScope: "profile:read", WalletBound: false),
        new("GET", "/v1/wallets", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.wallets.list", RequiredScope: "wallets:read", WalletBound: false),
        new("GET", "/v1/wallets/{walletId}", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.wallets.read", RequiredScope: "wallets:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/catalog/categories", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.catalogue.categories.list", RequiredScope: "catalogue:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.catalogue.category-subcategories.list", RequiredScope: "catalogue:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.catalogue.subcategory.read", RequiredScope: "catalogue:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.catalogue.subcategory-cards.list", RequiredScope: "catalogue:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/catalog/cards/{cardId}", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.catalogue.card.read", RequiredScope: "catalogue:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/owned-cards", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.cards.list", RequiredScope: "cards:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/owned-cards/{soldCardId}", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.cards.read", RequiredScope: "cards:read", WalletBound: true),

        // Money, card codes, access stops and the self-check: both directions signed.
        new("POST", "/v1/application/suspend", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.access.application.suspend", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/keys/{keyId}/revoke", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.access.key.revoke", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/wallets/{walletId}/orders", SignatureProfile.OrderMutation, SignsResponse: true, RouteId: "partner.orders.create", RequiredScope: "orders:create", WalletBound: true),
        new("GET", "/v1/orders/{operationId}", SignatureProfile.SafeRead, SignsResponse: true, RouteId: "partner.orders.read", RequiredScope: "orders:read", WalletBound: false),
        new("POST", "/v1/wallets/{walletId}/owned-cards/{soldCardId}/reveal", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.cards.reveal", RequiredScope: "cards:reveal", WalletBound: true),
        new("POST", "/v1/wallets/{walletId}/invoices/{invoiceId}/owned-cards/reveal", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.invoices.cards.reveal", RequiredScope: "cards:reveal", WalletBound: true),
        new("POST", "/v1/diagnostics/signature", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.diagnostics.signature", RequiredScope: "diagnostics:use", WalletBound: false),

        // Unsigned requests: enrollment carries a bearer-style token, and its answers establish a key, so they are signed.
        new("GET", "/v1/enrollments/{invitationId}", null, SignsResponse: true, RouteId: "partner.enrollments.read", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/enrollments/{invitationId}/keys", null, SignsResponse: true, RouteId: "partner.enrollments.keys.submit", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/enrollments/{invitationId}/proof", null, SignsResponse: true, RouteId: "partner.enrollments.proof.submit", RequiredScope: null, WalletBound: false),
        new("GET", "/v1/enrollments/{invitationId}/status", null, SignsResponse: true, RouteId: "partner.enrollments.status.read", RequiredScope: null, WalletBound: false),

        // The key document carries nothing either way: verifying it would need the keys it is fetched to supply.
        new("GET", "/.well-known/partner-signing-keys.json", null, SignsResponse: false, RouteId: "partner.signing-keys.read", RequiredScope: null, WalletBound: false),

        new("GET", "/v1/wallets/{walletId}/meter-packages", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.meter-packages.getMeterPackages", RequiredScope: "meter_charges:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/meter-charges", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.meter-charges.getMeterCharges", RequiredScope: "meter_charges:read", WalletBound: true),
        new("POST", "/v1/wallets/{walletId}/meter-charges", SignatureProfile.OrderMutation, SignsResponse: true, RouteId: "partner.meter-charges.create", RequiredScope: "meter_charges:create", WalletBound: true),
        new("GET", "/v1/meter-charges/{operationId}", SignatureProfile.SafeRead, SignsResponse: true, RouteId: "partner.meter-charges.read", RequiredScope: "meter_charges:read", WalletBound: false),
        new("GET", "/v1/wallets/{walletId}/direct-recharge-products", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.direct-recharge-products.getDirectRechargeProducts", RequiredScope: "direct_recharges:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.direct-recharge-products.read", RequiredScope: "direct_recharges:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}/locations", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.direct-recharge-products.locations.getDirectRechargeProductLocations", RequiredScope: "direct_recharges:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}/quote", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.direct-recharge-products.quote", RequiredScope: "direct_recharges:read", WalletBound: true),
        new("GET", "/v1/wallets/{walletId}/direct-recharges", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.direct-recharges.getDirectRecharges", RequiredScope: "direct_recharges:read", WalletBound: true),
        new("POST", "/v1/wallets/{walletId}/direct-recharges", SignatureProfile.OrderMutation, SignsResponse: true, RouteId: "partner.direct-recharges.create", RequiredScope: "direct_recharges:create", WalletBound: true),
        new("GET", "/v1/direct-recharges/{operationId}", SignatureProfile.SafeRead, SignsResponse: true, RouteId: "partner.direct-recharges.read", RequiredScope: "direct_recharges:read", WalletBound: false),
        new("PATCH", "/v1/direct-recharges/{operationId}/recipient", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.direct-recharges.recipient.update", RequiredScope: "direct_recharges:create", WalletBound: false, SuccessStatus: HttpStatusCode.Accepted),
        new("GET", "/v1/webhook-endpoint", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.webhook-endpoint.read", RequiredScope: null, WalletBound: false),
        new("PUT", "/v1/webhook-endpoint", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.webhook-endpoint.update", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/webhook-endpoint/test", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.webhook-endpoint.test", RequiredScope: null, WalletBound: false),
        new("GET", "/v1/webhook-events", SignatureProfile.SafeRead, SignsResponse: false, RouteId: "partner.webhook-events.getWebhookEvents", RequiredScope: null, WalletBound: false),
        new("POST", "/v1/webhook-events/{eventId}/redeliver", SignatureProfile.BodylessNonceMutation, SignsResponse: true, RouteId: "partner.webhook-events.redeliver", RequiredScope: null, WalletBound: false),
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
/// <param name="RouteId">The stable gateway route identifier.</param>
/// <param name="RequiredScope">The required scope, or null for scope-free routes.</param>
/// <param name="WalletBound">Whether admission binds the request to an authorized wallet.</param>
/// <param name="SuccessStatus">The sole success status where the contract requires one.</param>
internal sealed record PartnerRoute(string Method, string Template, SignatureProfile? Profile, bool SignsResponse, string? RouteId = null, string? RequiredScope = null, bool WalletBound = false, HttpStatusCode? SuccessStatus = null);
