using System.Text.Json;
using Anis.Partners.Sdk.Errors;
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
