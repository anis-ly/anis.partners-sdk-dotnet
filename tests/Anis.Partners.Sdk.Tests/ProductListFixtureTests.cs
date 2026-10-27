using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The list models against the owner-published list answers.</summary>
public sealed class ProductListFixtureTests
{
    [Fact]
    public void The_meter_list_answer_parses_into_summaries_without_receipt_or_tokens()
    {
        var page = JsonSerializer.Deserialize<Page<MeterChargeSummary>>(Read("meter-charges.json"), AnisJson.Options)!;

        var summary = Assert.Single(page.Items);

        Assert.Equal(MeterChargeStatus.Succeeded, summary.Status);
        Assert.Equal("0268000710262", summary.MeterNumber);
        Assert.Equal("PKG_100", summary.Package.GecolPackageName);
        Assert.Equal(97.500m, summary.Total.Amount);
        Assert.NotNull(summary.CompletedAt);
        Assert.Null(page.NextCursor);
        Assert.Equal(
            ["completedAt", "createdAt", "externalReference", "meterNumber", "operationId", "package", "status", "total"],
            JsonDocument.Parse(JsonSerializer.Serialize(summary, AnisJson.Options)).RootElement.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_direct_recharge_list_answer_parses_with_a_required_partial_refund_flag()
    {
        var page = JsonSerializer.Deserialize<Page<DirectRechargeSummary>>(Read("direct-recharges.json"), AnisJson.Options)!;

        var summary = Assert.Single(page.Items);

        Assert.Equal(DirectRechargeStatus.Completed, summary.Status);
        Assert.False(summary.HasPartialRefund);
        Assert.Equal("جنيه مصري", summary.Payout.Currency.Name);
        Assert.Equal(30412, summary.Number);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void A_direct_recharge_summary_without_the_partial_refund_flag_is_rejected()
    {
        const string body = """
            {"items":[{"operationId":"7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81","status":"completed","number":1,
            "product":{"id":"e7c2a9d4-1b3f-4a85-9c60-8d2e4f7b1a93","title":"t"},
            "payout":{"amount":"1.000","currency":{"name":"n"}},"total":{"amount":"1.000","currency":"LYD"},
            "requestedAt":"2026-10-20T09:00:00Z"}],"nextCursor":null}
            """;

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Page<DirectRechargeSummary>>(body, AnisJson.Options));
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "lists", name));
}
