using System.Reflection;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    public void Every_vendored_owner_file_matches_its_recorded_sha256_and_revision()
    {
        var files = Load("sources.json").GetProperty("files").EnumerateArray().ToArray();
        Assert.NotEmpty(files);
        Assert.Equal(files.Length, files.Select(file => file.GetProperty("target").GetString()).Distinct().Count());
        foreach (var file in files)
        {
            Assert.Matches("^[0-9a-f]{40}$", file.GetProperty("revision").GetString()!);
            Assert.Contains(file.GetProperty("repository").GetString(), new[] { "anis.partners-consumers-gateway", "anis.partners-sdk-conformance" });
            var target = file.GetProperty("target").GetString()!;
            var output = target.StartsWith("contracts/", StringComparison.Ordinal)
                ? target : target.Replace("tests/Anis.Partners.Sdk.Tests/", string.Empty, StringComparison.Ordinal);
            var path = Path.Combine(AppContext.BaseDirectory, output);
            Assert.True(File.Exists(path), target);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
        foreach (var folder in new[] { "contracts", "vectors" })
        {
            var actual = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, folder), "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/'))
                .Where(path => path != "contracts/sources.json").Order(StringComparer.Ordinal);
            var pinned = files.Select(file => file.GetProperty("target").GetString()!
                    .Replace("tests/Anis.Partners.Sdk.Tests/", string.Empty, StringComparison.Ordinal))
                .Where(path => path.StartsWith(folder + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
            Assert.Equal(pinned, actual);
        }
    }

    [Fact]
    public void Product_model_members_and_requiredness_match_the_complete_owner_schemas()
    {
        Type[] models =
        [
            typeof(MeterPackage), typeof(MeterCharge), typeof(MeterChargeSummary), typeof(MeterChargePackage),
            typeof(MeterReceipt), typeof(MeterToken), typeof(MeterTax), typeof(MeterFailure), typeof(MeterRefund),
            typeof(CreateMeterChargeRequest), typeof(MeterChargeAccepted), typeof(MeterChargesQuery),
            typeof(DirectRechargeProduct), typeof(DirectRechargeProductSummary), typeof(DirectRechargeCategory),
            typeof(DirectRechargeCurrency), typeof(DirectRechargeLimits), typeof(DirectRechargeRecipientField),
            typeof(DirectRechargeLocation), typeof(DirectRechargePayout), typeof(DirectRechargeQuote),
            typeof(DirectRechargeProductsQuery), typeof(DirectRechargeLocationsQuery),
            typeof(DirectRecharge), typeof(DirectRechargeSummary), typeof(DirectRechargeProductReference),
            typeof(DirectRechargeRecipientInput), typeof(DirectRechargeRecipientValue), typeof(DirectRechargePartialRefund),
            typeof(CreateDirectRechargeRequest), typeof(UpdateDirectRechargeRecipientRequest), typeof(DirectRechargeAccepted),
            typeof(DirectRechargesQuery), typeof(WebhookEndpoint), typeof(SetWebhookEndpointRequest),
            typeof(WebhookEvent), typeof(WebhookEventAccepted), typeof(WebhookEventsQuery),
        ];
        var schemas = OpenApi.GetProperty("components").GetProperty("schemas");
        foreach (var model in models)
        {
            var schema = schemas.GetProperty(model.Name);
            var members = model.GetProperties().Where(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null).ToArray();
            static string Name(PropertyInfo property) => property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;
            Assert.Equal(schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal),
                members.Select(Name).Order(StringComparer.Ordinal));
            var required = schema.TryGetProperty("required", out var names)
                ? names.EnumerateArray().Select(name => name.GetString()!).ToArray() : [];
            Assert.Equal(required.Order(StringComparer.Ordinal), members
                .Where(property => property.IsDefined(typeof(RequiredMemberAttribute)) || property.SetMethod is null)
                .Select(Name).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The gateway publishes these three operations for staging tests of the allowed-network rules only. They are
    /// never part of the SDK, so the comparison with the contract skips exactly these and nothing else; any other
    /// difference still fails. Their two error codes stay known to the SDK.
    /// </summary>
    private static readonly string[] StagingTestOnlyRoutes =
    [
        "GET /v1/application/networks",
        "POST /v1/application/networks",
        "POST /v1/application/networks/remove",
    ];

    private static bool IsStagingTestOnly(string method, string template)
        => StagingTestOnlyRoutes.Contains($"{method.ToUpperInvariant()} {template}", StringComparer.Ordinal);

    [Fact]
    public void Only_the_named_network_exclusions_have_staging_only_availability()
    {
        var staging = OpenApi.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Value.ValueKind == JsonValueKind.Object
                    && operation.Value.TryGetProperty("x-anis-availability", out var availability)
                    && availability.GetString() == "staging-only")
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"));
        Assert.Equal(StagingTestOnlyRoutes.Order(StringComparer.Ordinal), staging.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_recipient_patch_requires_the_published_signed_202_acknowledgement_and_location()
    {
        const string Template = "/v1/direct-recharges/{operationId}/recipient";
        var operation = OpenApi.GetProperty("paths").GetProperty(Template).GetProperty("patch");
        var responses = operation.GetProperty("responses");
        var route = PartnerRoutes.Find(HttpMethod.Patch, Template);
        Assert.Equal(HttpStatusCode.Accepted, route.SuccessStatus);
        Assert.True(route.SignsResponse);
        Assert.Equal(["202"], responses.EnumerateObject().Where(response => response.Name.StartsWith('2')).Select(response => response.Name));
        var accepted = responses.GetProperty("202");
        Assert.Equal("#/components/schemas/DirectRechargeAccepted", accepted.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.True(accepted.GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(accepted.GetProperty("headers").TryGetProperty("Signature", out _));
        Assert.True(accepted.GetProperty("headers").TryGetProperty("Signature-Input", out _));
        var create = OpenApi.GetProperty("paths").GetProperty("/v1/wallets/{walletId}/direct-recharges").GetProperty("post")
            .GetProperty("responses").GetProperty("202");
        Assert.Equal(create.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString(),
            accepted.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        var schema = OpenApi.GetProperty("components").GetProperty("schemas").GetProperty(nameof(DirectRechargeAccepted));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["operationId", "operationType", "status"], schema.GetProperty("required").EnumerateArray().Select(member => member.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(["directRecharge"], schema.GetProperty("properties").GetProperty("operationType").GetProperty("enum").EnumerateArray().Select(member => member.GetString()));
        Assert.Equal(["processing"], schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(member => member.GetString()));
        Assert.Equal(typeof(Task<DirectRechargeProcessing>), typeof(IDirectRechargeOperations).GetMethod(nameof(IDirectRechargeOperations.UpdateRecipientAsync))!.ReturnType);
    }

    [Fact]
    public void Signing_key_purpose_is_optional_on_read_in_the_final_contract()
    {
        var schema = OpenApi.GetProperty("components").GetProperty("schemas").GetProperty("PublishedSigningKey");
        Assert.DoesNotContain("purpose", schema.GetProperty("required").EnumerateArray().Select(member => member.GetString()));
        Assert.Equal(["answer", "webhook"], schema.GetProperty("properties").GetProperty("purpose").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void The_sdk_route_table_matches_the_published_surface_exactly()
    {
        var published = new List<string>();

        foreach (var path in OpenApi.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is "get" or "post" or "put" or "patch" or "delete" && !IsStagingTestOnly(operation.Name, path.Name))
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
                if (!operation.Value.TryGetProperty("x-anis-route", out var route) || IsStagingTestOnly(operation.Name, path.Name))
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
                if (!operation.Value.TryGetProperty("x-anis-route", out var route) || IsStagingTestOnly(operation.Name, path.Name))
                    continue;

                var actual = PartnerRoutes.All.Single(candidate =>
                    candidate.Method == operation.Name.ToUpperInvariant() && candidate.Template == path.Name);

                Assert.Equal(route.GetProperty("signsResponse").GetBoolean(), actual.SignsResponse);
            }
        }
    }

    [Fact]
    public void The_signed_answers_are_exactly_the_security_sensitive_routes()
    {
        // The owner's ruling, written out so a change to it is a change to this test and not only to a table.
        string[] signed =
        [
            "POST /v1/application/suspend",
            "POST /v1/keys/{keyId}/revoke",
            "POST /v1/wallets/{walletId}/orders",
            "GET /v1/orders/{operationId}",
            "POST /v1/wallets/{walletId}/owned-cards/{soldCardId}/reveal",
            "POST /v1/wallets/{walletId}/invoices/{invoiceId}/owned-cards/reveal",
            "GET /v1/enrollments/{invitationId}",
            "POST /v1/enrollments/{invitationId}/keys",
            "POST /v1/enrollments/{invitationId}/proof",
            "GET /v1/enrollments/{invitationId}/status",
            "POST /v1/diagnostics/signature",
            "POST /v1/wallets/{walletId}/meter-charges",
            "GET /v1/meter-charges/{operationId}",
            "POST /v1/wallets/{walletId}/direct-recharges",
            "GET /v1/direct-recharges/{operationId}",
            "PATCH /v1/direct-recharges/{operationId}/recipient",
            "PUT /v1/webhook-endpoint",
            "POST /v1/webhook-endpoint/test",
            "POST /v1/webhook-events/{eventId}/redeliver",
        ];

        string[] unsigned =
        [
            "GET /v1/profile",
            "GET /v1/wallets",
            "GET /v1/wallets/{walletId}",
            "GET /v1/wallets/{walletId}/catalog/cards/{cardId}",
            "GET /v1/wallets/{walletId}/catalog/categories",
            "GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories",
            "GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}",
            "GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards",
            "GET /v1/wallets/{walletId}/owned-cards",
            "GET /v1/wallets/{walletId}/owned-cards/{soldCardId}",
            "GET /.well-known/partner-signing-keys.json",
            "GET /v1/wallets/{walletId}/meter-packages",
            "GET /v1/wallets/{walletId}/meter-charges",
            "GET /v1/wallets/{walletId}/direct-recharge-products",
            "GET /v1/wallets/{walletId}/direct-recharge-products/{productId}",
            "GET /v1/wallets/{walletId}/direct-recharge-products/{productId}/locations",
            "GET /v1/wallets/{walletId}/direct-recharge-products/{productId}/quote",
            "GET /v1/wallets/{walletId}/direct-recharges",
            "GET /v1/webhook-endpoint",
            "GET /v1/webhook-events",
        ];

        static string Name(PartnerRoute route) => $"{route.Method} {route.Template}";

        Assert.Equal(signed.Order(StringComparer.Ordinal), PartnerRoutes.All.Where(route => route.SignsResponse).Select(Name).Order(StringComparer.Ordinal));
        Assert.Equal(unsigned.Order(StringComparer.Ordinal), PartnerRoutes.All.Where(route => !route.SignsResponse).Select(Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_route_carries_the_contracts_route_id_scope_and_wallet_binding()
    {
        foreach (var path in OpenApi.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("x-anis-route", out var route) || IsStagingTestOnly(operation.Name, path.Name))
                    continue;

                var actual = PartnerRoutes.All.Single(candidate =>
                    candidate.Method == operation.Name.ToUpperInvariant() && candidate.Template == path.Name);

                if (route.TryGetProperty("routeId", out var routeId))
                    Assert.Equal(routeId.GetString(), actual.RouteId);

                if (route.TryGetProperty("requiredScope", out var scope))
                    Assert.Equal(scope.ValueKind == JsonValueKind.Null ? null : scope.GetString(), actual.RequiredScope);

                if (route.TryGetProperty("walletBound", out var walletBound))
                    Assert.Equal(walletBound.GetBoolean(), actual.WalletBound);
            }
        }
    }

    [Fact]
    public void The_product_routes_use_the_owner_ruled_route_ids_scopes_and_names()
    {
        (string Method, string Template, string RouteId, string? Scope, bool WalletBound, bool Signed)[] added =
        [
            ("GET", "/v1/wallets/{walletId}/meter-packages", "partner.meter-packages.getMeterPackages", "meter_charges:read", true, false),
            ("GET", "/v1/wallets/{walletId}/meter-charges", "partner.meter-charges.getMeterCharges", "meter_charges:read", true, false),
            ("POST", "/v1/wallets/{walletId}/meter-charges", "partner.meter-charges.create", "meter_charges:create", true, true),
            ("GET", "/v1/meter-charges/{operationId}", "partner.meter-charges.read", "meter_charges:read", false, true),
            ("GET", "/v1/wallets/{walletId}/direct-recharge-products", "partner.direct-recharge-products.getDirectRechargeProducts", "direct_recharges:read", true, false),
            ("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}", "partner.direct-recharge-products.read", "direct_recharges:read", true, false),
            ("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}/locations", "partner.direct-recharge-products.locations.getDirectRechargeProductLocations", "direct_recharges:read", true, false),
            ("GET", "/v1/wallets/{walletId}/direct-recharge-products/{productId}/quote", "partner.direct-recharge-products.quote", "direct_recharges:read", true, false),
            ("GET", "/v1/wallets/{walletId}/direct-recharges", "partner.direct-recharges.getDirectRecharges", "direct_recharges:read", true, false),
            ("POST", "/v1/wallets/{walletId}/direct-recharges", "partner.direct-recharges.create", "direct_recharges:create", true, true),
            ("GET", "/v1/direct-recharges/{operationId}", "partner.direct-recharges.read", "direct_recharges:read", false, true),
            ("PATCH", "/v1/direct-recharges/{operationId}/recipient", "partner.direct-recharges.recipient.update", "direct_recharges:create", false, true),
            ("GET", "/v1/webhook-endpoint", "partner.webhook-endpoint.read", null, false, false),
            ("PUT", "/v1/webhook-endpoint", "partner.webhook-endpoint.update", null, false, true),
            ("POST", "/v1/webhook-endpoint/test", "partner.webhook-endpoint.test", null, false, true),
            ("GET", "/v1/webhook-events", "partner.webhook-events.getWebhookEvents", null, false, false),
            ("POST", "/v1/webhook-events/{eventId}/redeliver", "partner.webhook-events.redeliver", null, false, true),
        ];

        // The seventeen rows are the product and notification routes; the wallet statement is not a route.
        Assert.Equal(17, added.Length);
        Assert.DoesNotContain(PartnerRoutes.All, route => route.Template.Contains("statement", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PartnerRoutes.All, route => route.RequiredScope == "statement:read");

        foreach (var row in added)
        {
            var actual = PartnerRoutes.All.Single(route => route.Method == row.Method && route.Template == row.Template);

            Assert.Equal(row.RouteId, actual.RouteId);
            Assert.Equal(row.Scope, actual.RequiredScope);
            Assert.Equal(row.WalletBound, actual.WalletBound);
            Assert.Equal(row.Signed, actual.SignsResponse);
        }
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
        // A partner's compiled switch holds these numbers. A new code only ever appends.
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
            [PartnerErrorCode.KeyNotRevocable] = 38,
            [PartnerErrorCode.SpendingLimitExceeded] = 39,
            [PartnerErrorCode.AmountOutOfRange] = 40,
            [PartnerErrorCode.DirectRechargeNotSuspended] = 41,
            [PartnerErrorCode.DirectRechargeProductUnavailable] = 42,
            [PartnerErrorCode.MeterRejected] = 43,
            [PartnerErrorCode.PackageUnavailable] = 44,
            [PartnerErrorCode.WebhookEndpointInvalid] = 45,
            [PartnerErrorCode.WebhookEndpointUnverified] = 46,
            [PartnerErrorCode.NetworkNotRemovable] = 47,
            [PartnerErrorCode.PartnerNetworkLimitReached] = 48,
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
