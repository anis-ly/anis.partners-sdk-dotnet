using System.Text.Json;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>What a verified notification carries: typed data for the eight known types, raw JSON for a future one.</summary>
/// <remarks>The notifications are the conformance corpus itself, so the bytes are the ones Anis signs.</remarks>
public sealed class WebhookModelTests
{
    [Fact]
    public async Task SuspendedType_CurrentInProgressData_IsAccepted()
    {
        var envelope = await Verify("WH-046-fixture-circle-suspended-after-recipient-fix");

        var data = Assert.IsType<DirectRecharge>(envelope.Data);

        // The type says what triggered the notice; the data is the later truth and must not be refused for differing.
        Assert.Equal(WebhookEventType.DirectRechargeSuspended, envelope.Type);
        Assert.Equal(DirectRechargeStatus.InProgress, data.Status);
    }

    [Fact]
    public async Task A_succeeded_meter_notification_carries_the_ordered_tokens_and_arabic_taxes()
    {
        var envelope = await Verify("WH-049-fixture-meter-succeeded");

        var data = Assert.IsType<MeterCharge>(envelope.Data);

        Assert.Equal(WebhookEventType.MeterChargeSucceeded, envelope.Type);
        Assert.Equal(MeterChargeStatus.Succeeded, data.Status);
        Assert.Equal([1, 2, 3], data.Receipt!.Tokens.Select(token => token.Sequence));
        Assert.Equal("ضريبة الاستهلاك", data.Receipt.Taxes[0].Description);
        Assert.NotNull(data.CompletedAt);
    }

    [Theory]
    [InlineData("WH-047-fixture-meter-failed-not-charged", MeterRefundStatus.NotCharged)]
    [InlineData("WH-048-fixture-meter-failed-refunded", MeterRefundStatus.Refunded)]
    public async Task A_failed_meter_notification_requires_totals_and_omits_the_completion_time(string id, MeterRefundStatus refund)
    {
        var envelope = await Verify(id);

        var data = Assert.IsType<MeterCharge>(envelope.Data);

        Assert.Equal(MeterChargeStatus.Failed, data.Status);
        Assert.Equal(refund, data.Refund!.Status);
        Assert.Null(data.CompletedAt);
        Assert.True(data.Total.Amount > 0);
        Assert.True(data.PersonalPrice.Amount > 0);
    }

    [Theory]
    [InlineData("WH-040-fixture-circle-completed", WebhookEventType.DirectRechargeCompleted, DirectRechargeStatus.Completed)]
    [InlineData("WH-041-fixture-circle-paid", WebhookEventType.DirectRechargePaid, DirectRechargeStatus.Pending)]
    [InlineData("WH-043-fixture-circle-payment-failed", WebhookEventType.DirectRechargePaymentFailed, DirectRechargeStatus.PaymentFailed)]
    [InlineData("WH-044-fixture-circle-rejected", WebhookEventType.DirectRechargeRejected, DirectRechargeStatus.RejectedWithRefund)]
    [InlineData("WH-045-fixture-circle-suspended", WebhookEventType.DirectRechargeSuspended, DirectRechargeStatus.SuspendedDataError)]
    public async Task A_direct_recharge_notification_reads_the_current_state(string id, WebhookEventType type, DirectRechargeStatus status)
    {
        var envelope = await Verify(id);

        var data = Assert.IsType<DirectRecharge>(envelope.Data);

        Assert.Equal(type, envelope.Type);
        Assert.Equal(status, data.Status);
        Assert.False(string.IsNullOrEmpty(data.Payout.Currency.Name));
    }

    [Fact]
    public async Task A_partial_refund_notification_keeps_its_block_as_a_future_capability()
    {
        var envelope = await Verify("WH-042-fixture-circle-partially-refunded");

        var data = Assert.IsType<DirectRecharge>(envelope.Data);

        Assert.Equal(WebhookEventType.DirectRechargePartiallyRefunded, envelope.Type);
        Assert.NotNull(data.PartialRefund);
    }

    [Fact]
    public async Task A_future_type_is_accepted_after_verification_with_its_raw_type_and_data()
    {
        var envelope = await Verify("WH-034-unknown-event-type");

        Assert.Equal(WebhookEventType.Unknown, envelope.Type);
        Assert.False(string.IsNullOrEmpty(envelope.RawType));

        var raw = Assert.IsType<JsonElement>(envelope.Data);

        Assert.Equal(JsonValueKind.Object, raw.ValueKind);
    }

    [Fact]
    public async Task The_failed_verification_of_a_changed_body_never_produces_an_envelope()
    {
        var subject = Load("WH-003-body-byte-changed", out var verifier);

        var failure = await Assert.ThrowsAsync<UnverifiableWebhookException>(
            async () => await verifier.VerifyAndParseAsync(subject, TestContext.Current.CancellationToken));

        Assert.Equal(WebhookVerificationFailure.ContentDigestMismatch, failure.Failure);
        Assert.DoesNotContain("Anis", failure.Message.Replace("The Anis webhook could not be verified", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raw_bytes_survive_verification_with_whitespace_and_arabic()
    {
        var subject = Load("WH-049-fixture-meter-succeeded", out var verifier);

        var envelope = await verifier.VerifyAndParseAsync(subject, TestContext.Current.CancellationToken);

        Assert.Contains("ضريبة", System.Text.Encoding.UTF8.GetString(subject.Body), StringComparison.Ordinal);
        Assert.Equal(WebhookEventType.MeterChargeSucceeded, envelope.Type);
    }

    [Fact]
    public async Task A_query_in_the_configured_url_is_preserved()
    {
        var subject = Load("WH-038-query-target", out var verifier);

        Assert.Contains('?', subject.TargetUri);

        await verifier.VerifyAsync(subject, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_diagnostic_view_of_the_envelope_and_the_verifier_does_not_hold_the_body()
    {
        var subject = Load("WH-049-fixture-meter-succeeded", out var verifier);

        Assert.DoesNotContain("5821", verifier.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("5821", subject.ToString(), StringComparison.Ordinal);
        var envelope = await verifier.VerifyAndParseAsync(subject, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("5821", envelope.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("260020103744", envelope.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", envelope.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("directRecharge.completed")]
    [InlineData("future.secret-type")]
    public async Task Parsed_known_and_unknown_envelope_diagnostics_redact_the_data(string type)
    {
        using var fixture = new WebhookFixture();
        var data = type == "directRecharge.completed"
            ? System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.DirectRecharge("completed"))!
            : System.Text.Json.Nodes.JsonNode.Parse("""{"recipient":"01012345678","challenge":"never-log-this-challenge","nested":{"token":"sensitive-token"}}""")!;
        var envelope = await fixture.Verifier.VerifyAndParseAsync(fixture.Create(type, data), TestContext.Current.CancellationToken);

        if (type == "directRecharge.completed") Assert.IsType<DirectRecharge>(envelope.Data);
        else Assert.IsType<JsonElement>(envelope.Data);
        var diagnostic = envelope.ToString();
        foreach (var secret in new[] { "01012345678", "never-log-this-challenge", "sensitive-token", "secret-type", "Family support" })
            Assert.DoesNotContain(secret, diagnostic, StringComparison.Ordinal);
        Assert.Contains("<redacted>", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_webhooks_accept_optional_receipt_message_and_recipient_names(bool explicitNull)
    {
        using var fixture = new WebhookFixture();
        var meter = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.MeterSucceeded())!;
        foreach (var member in new[] { "receiptNumber", "creditedAmount" })
        {
            if (explicitNull) meter["receipt"]![member] = null;
            else meter["receipt"]!.AsObject().Remove(member);
        }
        var envelope = await fixture.Verifier.VerifyAndParseAsync(fixture.Create("meterCharge.succeeded", meter), TestContext.Current.CancellationToken);
        Assert.Null(Assert.IsType<MeterCharge>(envelope.Data).Receipt!.CreditedAmount);

        meter = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.MeterFailed("charge_failed", "refunded", true))!;
        if (explicitNull) meter["failure"]!["message"] = null;
        else meter["failure"]!.AsObject().Remove("message");
        envelope = await fixture.Verifier.VerifyAndParseAsync(fixture.Create("meterCharge.failed", meter), TestContext.Current.CancellationToken);
        Assert.Null(Assert.IsType<MeterCharge>(envelope.Data).Failure!.Message);

        var recharge = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.DirectRecharge("completed"))!;
        foreach (var recipient in recharge["recipient"]!.AsArray())
        {
            if (explicitNull) recipient!["name"] = null;
            else recipient!.AsObject().Remove("name");
        }
        envelope = await fixture.Verifier.VerifyAndParseAsync(fixture.Create("directRecharge.completed", recharge), TestContext.Current.CancellationToken);
        Assert.All(Assert.IsType<DirectRecharge>(envelope.Data).Recipient, recipient => Assert.Null(recipient.Name));
    }

    private static async Task<WebhookEnvelope> Verify(string id)
    {
        var subject = Load(id, out var verifier);

        return await verifier.VerifyAndParseAsync(subject, TestContext.Current.CancellationToken);
    }

    private static VerifiableWebhook Load(string id, out PartnerWebhookVerifier verifier)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "vectors", "webhook", $"{id}.json");

        Assert.True(File.Exists(path), $"The conformance vector {id} is missing.");

        var vector = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var request = vector.GetProperty("request");
        var receiver = vector.GetProperty("receiver");
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var header in request.GetProperty("headers").EnumerateObject())
            headers[header.Name] = header.Value.GetString()!;

        verifier = new PartnerWebhookVerifier(
            new StaticSigningKeySource(vector.GetProperty("signingKeys").Deserialize<PartnerSigningKeySet>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!),
            new Uri(receiver.GetProperty("configuredUrl").GetString()!),
            Guid.Parse(receiver.GetProperty("applicationId").GetString()!),
            new FixedClock(DateTimeOffset.FromUnixTimeSeconds(vector.GetProperty("verifyAt").GetInt64())));

        return new VerifiableWebhook(
            request.GetProperty("method").GetString()!,
            request.GetProperty("targetUri").GetString()!,
            headers,
            Convert.FromBase64String(request.GetProperty("bodyBase64").GetString()!));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
