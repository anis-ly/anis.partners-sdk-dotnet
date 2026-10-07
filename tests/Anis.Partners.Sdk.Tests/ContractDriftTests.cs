using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Operations;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Holds the SDK against the vendored contracts, in both directions.</summary>
/// <remarks>
/// Hand-written models and a hand-written route table read better than anything a generator emits, and
/// they are only safe if something checks them. This is that something: it is the same trick the gateway
/// already uses to hold its own route catalogue against the frozen OpenAPI.
/// </remarks>
public sealed class ContractDriftTests
{
    private static JsonElement OpenApi => Load("partner-public-v1.json");

    private static JsonElement Catalogue => Load("error-catalogue.json");

    [Fact]
    public void The_sdk_route_table_matches_the_published_surface_exactly()
    {
        var published = new List<string>();

        foreach (var path in OpenApi.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is "get" or "post" or "put" or "patch" or "delete")
                    published.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        var sdk = PartnerRoutes.All.Select(route => $"{route.Method} {route.Template}").ToList();

        // Both directions. A route the SDK invented is as much a defect as a published one it cannot reach.
        Assert.Empty(sdk.Except(published, StringComparer.Ordinal));
        Assert.Empty(published.Except(sdk, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_route_signs_under_the_profile_the_contract_assigns_it()
    {
        foreach (var path in OpenApi.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("x-anis-route", out var route))
                    continue;

                var expected = route.GetProperty("requestKind").GetString() switch
                {
                    "safeRead" => (SignatureProfile?)SignatureProfile.SafeRead,
                    "bodylessNonceMutation" => SignatureProfile.BodylessNonceMutation,
                    "orderMutation" => SignatureProfile.OrderMutation,
                    _ => null,
                };

                var actual = PartnerRoutes.All.Single(candidate =>
                    candidate.Method == operation.Name.ToUpperInvariant() && candidate.Template == path.Name);

                Assert.Equal(expected, actual.Profile);
            }
        }
    }

    [Fact]
    public void Every_route_verifies_its_answers_exactly_where_the_contract_signs_them()
    {
        foreach (var path in OpenApi.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("x-anis-route", out var route))
                    continue;

                var actual = PartnerRoutes.All.Single(candidate =>
                    candidate.Method == operation.Name.ToUpperInvariant() && candidate.Template == path.Name);

                Assert.Equal(route.GetProperty("signsResponse").GetBoolean(), actual.SignsResponse);
            }
        }
    }

    [Fact]
    public void The_signed_answers_are_exactly_the_ones_that_move_money_deliver_codes_or_establish_a_key()
    {
        // The owner's ruling, written out so a change to it is a change to this test and not only to a table.
        string[] signed =
        [
            "POST /v1/wallets/{walletId}/orders",
            "GET /v1/orders/{operationId}",
            "POST /v1/wallets/{walletId}/cards/{soldCardId}/reveal",
            "POST /v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal",
            "GET /v1/enrollments/{invitationId}",
            "POST /v1/enrollments/{invitationId}/keys",
            "POST /v1/enrollments/{invitationId}/proof",
            "GET /v1/enrollments/{invitationId}/status",
            "POST /v1/diagnostics/signature",
        ];

        string[] unsigned =
        [
            "GET /v1/profile",
            "GET /v1/wallets",
            "GET /v1/wallets/{walletId}",
            "GET /v1/wallets/{walletId}/catalog/categories",
            "GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories",
            "GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}",
            "GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards",
            "GET /v1/wallets/{walletId}/cards",
            "GET /v1/wallets/{walletId}/cards/{soldCardId}",
            "GET /.well-known/partner-signing-keys.json",
        ];

        static string Name(PartnerRoute route) => $"{route.Method} {route.Template}";

        Assert.Equal(signed.Order(StringComparer.Ordinal), PartnerRoutes.All.Where(route => route.SignsResponse).Select(Name).Order(StringComparer.Ordinal));
        Assert.Equal(unsigned.Order(StringComparer.Ordinal), PartnerRoutes.All.Where(route => !route.SignsResponse).Select(Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_public_error_code_in_the_catalogue_is_known_to_the_sdk()
    {
        var published = Catalogue.GetProperty("representations").EnumerateArray()
            .Where(representation => representation.TryGetProperty("publicDocumentation", out var flag) && flag.GetBoolean())
            .Select(representation => representation.GetProperty("publicCode").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(published.Except(PartnerErrorCodes.All, StringComparer.Ordinal));
        Assert.Empty(PartnerErrorCodes.All.Except(published, StringComparer.Ordinal));
    }

    [Fact]
    public void The_key_submission_answer_model_carries_exactly_the_published_members()
    {
        var published = OpenApi.GetProperty("components").GetProperty("schemas").GetProperty("EnrollmentKeyResult")
            .GetProperty("properties").EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal);

        var modelled = typeof(EnrollmentKeyResult).GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(published, modelled);
        Assert.Contains("safetyCode", published);
    }

    [Fact]
    public void A_released_error_code_keeps_its_number()
    {
        // A partner's compiled switch holds these numbers. 1-36 shipped in 1.0.0; a new code only ever appends.
        var released = new Dictionary<PartnerErrorCode, int>
        {
            [PartnerErrorCode.Unknown] = 0,
            [PartnerErrorCode.AccountInactive] = 1,
            [PartnerErrorCode.AllowedDebtConsentRequired] = 2,
            [PartnerErrorCode.BindingNotAuthorized] = 3,
            [PartnerErrorCode.BusinessSubscriptionRequired] = 4,
            [PartnerErrorCode.CardNotFound] = 5,
            [PartnerErrorCode.CardUnavailable] = 6,
            [PartnerErrorCode.ChallengeExpired] = 7,
            [PartnerErrorCode.CurrencyNotSupported] = 8,
            [PartnerErrorCode.DailyLimitExceeded] = 9,
            [PartnerErrorCode.DependencyUnavailable] = 10,
            [PartnerErrorCode.IdempotencyConflict] = 11,
            [PartnerErrorCode.InsufficientBalance] = 12,
            [PartnerErrorCode.InsufficientScope] = 13,
            [PartnerErrorCode.InternalError] = 14,
            [PartnerErrorCode.InvalidContentDigest] = 15,
            [PartnerErrorCode.InvalidCredentials] = 16,
            [PartnerErrorCode.InvitationInvalid] = 17,
            [PartnerErrorCode.InvoiceRevealLimitExceeded] = 18,
            [PartnerErrorCode.KeyDuplicate] = 19,
            [PartnerErrorCode.KeyProofInvalid] = 20,
            [PartnerErrorCode.OperationProcessing] = 21,
            [PartnerErrorCode.OwnerLimitExceeded] = 22,
            [PartnerErrorCode.PriceChanged] = 23,
            [PartnerErrorCode.PurchaseNotAllowed] = 24,
            [PartnerErrorCode.QuantityUnavailable] = 25,
            [PartnerErrorCode.RateLimited] = 26,
            [PartnerErrorCode.ReplayDetected] = 27,
            [PartnerErrorCode.RequestTimeout] = 28,
            [PartnerErrorCode.ResourceNotFound] = 29,
            [PartnerErrorCode.RevealNotAllowed] = 30,
            [PartnerErrorCode.SignatureExpired] = 31,
            [PartnerErrorCode.SourceIpNotAllowed] = 32,
            [PartnerErrorCode.ValidationFailed] = 33,
            [PartnerErrorCode.WalletDisabled] = 34,
            [PartnerErrorCode.WalletExpired] = 35,
            [PartnerErrorCode.WalletNotGranted] = 36,
            [PartnerErrorCode.MalformedSignedRequest] = 37,
        };

        Assert.All(released, pair => Assert.Equal(pair.Value, (int)pair.Key));
        Assert.Equal(released.Count, Enum.GetValues<PartnerErrorCode>().Length);
    }

    [Fact]
    public void The_covered_component_profiles_match_the_contracts_description()
    {
        // The contract states the three orders; the corpus proves the bytes. This guards the one thing a
        // corpus cannot: that the SDK still ASKS for the right profile after a refactor.
        Assert.Equal(["@method", "@authority", "@path", "@query", "x-anis-date"], SignatureProfiles.SafeRead);

        Assert.Equal(
            ["@method", "@authority", "@path", "@query", "content-digest", "nonce", "x-anis-date"],
            SignatureProfiles.BodylessNonceMutation);

        Assert.Equal(
            ["@method", "@authority", "@path", "@query", "content-digest", "nonce", "idempotency-key", "x-anis-date"],
            SignatureProfiles.OrderMutation);
    }

    private static JsonElement Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contracts", name);

        Assert.True(File.Exists(path), $"The vendored contract {name} is missing from the test output.");

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }
}
