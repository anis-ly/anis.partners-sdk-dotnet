using System.Net;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Observability;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>What a partner can actually see, and what they must never see.</summary>
public sealed class ObservabilityTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid SoldCard = Guid.Parse("4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046");
    private static readonly Guid Operation = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");

    private const string Voucher = "VOUCHER-1234-5678-9012";
    private const string Serial = "SERIAL-000123";

    private static CreateOrderRequest Order => new()
    {
        CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
        Quantity = 2,
        ExpectedUnitPrice = new Money(10.500m, "LYD"),
        ExpectedTotal = new Money(21.000m, "LYD"),
    };

    [Fact]
    public async Task A_call_produces_a_span_tagged_with_the_route_TEMPLATE_not_the_path()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Body = """{"id":"2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26","name":"Main","currency":"LYD","balance":{"amount":"1.000","currency":"LYD"}}""";

        await fixture.Client.Wallets.GetAsync(Wallet, TestContext.Current.CancellationToken);

        var span = Assert.Single(capture.Spans);

        // The template, not the concrete path — tagging the path gives a metrics backend one time series
        // per wallet, which is how an observability bill becomes a surprise.
        Assert.Equal("/v1/wallets/{walletId}", span.GetTagItem(AnisPartnersTelemetry.Tags.Route));
        Assert.Equal("GET", span.GetTagItem(AnisPartnersTelemetry.Tags.Method));
        Assert.Equal(200, span.GetTagItem(AnisPartnersTelemetry.Tags.StatusCode));
        Assert.NotNull(span.GetTagItem(AnisPartnersTelemetry.Tags.RequestId));
        Assert.DoesNotContain(Wallet.ToString("D"), span.GetTagItem(AnisPartnersTelemetry.Tags.Route)!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duration_and_signing_cost_are_measured_separately()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Body = """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":[]}}""";

        await fixture.Client.Profile.GetAsync(TestContext.Current.CancellationToken);

        Assert.Contains(capture.Measurements, m => m.Instrument == "anis.partners.request.duration");

        var signature = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.signature.duration");

        // Separate, because a vault-backed signer's round trip is invisible inside a total.
        Assert.Equal("SafeRead", signature.Tags[AnisPartnersTelemetry.Tags.SignatureProfile]);
    }

    [Fact]
    public async Task An_order_reports_its_outcome_and_carries_the_operation_id_on_the_span()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"completed","soldCards":[{"soldCardId":"{{SoldCard:D}}","voucher":"{{Voucher}}","serialNumber":"{{Serial}}"}]}""";

        await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");

        Assert.Equal("completed", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);

        // The caller's own id: not a secret, and the one tag that lets a purchase be traced end to end.
        var span = Assert.Single(capture.Spans);
        Assert.Equal(Operation.ToString("D"), span.GetTagItem(AnisPartnersTelemetry.Tags.OperationId));
    }

    [Fact]
    public async Task A_refusal_is_one_warning_carrying_the_code_and_the_request_id()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"insufficient_balance","requestId":"01J9"}""";

        Assert.IsType<OrderNotPlaced>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));

        Assert.Contains(capture.LogLines, line => line.Contains("Warning", StringComparison.Ordinal) && line.Contains("insufficient_balance", StringComparison.Ordinal));

        var span = Assert.Single(capture.Spans);
        Assert.Equal("insufficient_balance", span.GetTagItem(AnisPartnersTelemetry.Tags.ErrorCode));
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, span.Status);

        // The duration metric carries the public code too, so a dashboard can chart refusals by reason.
        var duration = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.request.duration");
        Assert.Equal("insufficient_balance", duration.Tags[AnisPartnersTelemetry.Tags.ErrorCode]);
    }

    [Fact]
    public async Task A_discarded_response_is_counted_by_the_rule_that_refused_it()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Body = """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":[]}}""";
        fixture.Stub.SignedAt = PipelineFixture.Now.AddHours(-2);   // far outside the freshness window

        await Assert.ThrowsAsync<UnverifiableResponseException>(
            () => fixture.Client.Profile.GetAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.response.verification.failures");

        Assert.Equal("CreatedOutOfWindow", failure.Tags[AnisPartnersTelemetry.Tags.VerificationFailure]);
        Assert.Contains(capture.LogLines, line => line.Contains("Error", StringComparison.Ordinal) && line.Contains("DISCARDED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_timed_out_order_is_a_failed_span_a_measured_call_and_an_unknown_outcome()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        // What HttpClient throws when its own timeout fires: a cancellation the caller did not ask for.
        fixture.Stub.Failure = () => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

        var unknown = Assert.IsType<OrderOutcomeUnknown>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.IsType<TaskCanceledException>(unknown.Cause);

        // The span is marked failed and says why, and still names the purchase.
        var span = Assert.Single(capture.Spans);
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, span.Status);
        Assert.Equal("timeout", span.GetTagItem(AnisPartnersTelemetry.Tags.ErrorType));
        Assert.Equal(Operation.ToString("D"), span.GetTagItem(AnisPartnersTelemetry.Tags.OperationId));

        // The call is on the duration chart, not missing from it.
        var duration = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.request.duration");
        Assert.Equal("timeout", duration.Tags[AnisPartnersTelemetry.Tags.ErrorType]);

        // And the one order outcome that needs a resume is counted and logged with the id to resume.
        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("timeout", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
        Assert.Contains(capture.LogLines, line => line.StartsWith("Warning 1008", StringComparison.Ordinal) && line.Contains(Operation.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lost_connection_on_an_order_is_counted_unknown()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Failure = () => new HttpRequestException("Connection reset by peer.");

        var unknown = Assert.IsType<OrderOutcomeUnknown>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.IsType<HttpRequestException>(unknown.Cause);

        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("connection", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
    }

    [Fact]
    public async Task An_unverifiable_order_answer_is_counted_unknown()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"completed"}""";
        fixture.Stub.SignedAt = PipelineFixture.Now.AddHours(-2);   // far outside the freshness window

        var unknown = Assert.IsType<OrderOutcomeUnknown>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.IsType<UnverifiableResponseException>(unknown.Cause);

        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("unverifiable", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
    }

    [Fact]
    public async Task A_refusal_that_reached_no_decision_is_counted_unknown_and_a_final_one_is_not()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.ServiceUnavailable;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":503,"code":"dependency_unavailable","requestId":"01J9"}""";

        var unknown = Assert.IsType<OrderOutcomeUnknown>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.IsType<Errors.DependencyUnavailableException>(unknown.Cause);

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"insufficient_balance","requestId":"01J9"}""";

        var notPlaced = Assert.IsType<OrderNotPlaced>(await fixture.Client.Orders.CreateAsync(Wallet, Guid.NewGuid(), Order, TestContext.Current.CancellationToken));
        Assert.IsType<Errors.InsufficientBalanceException>(notPlaced.Refusal);

        // Only the open one: a final refusal placed nothing and needs no resume.
        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("dependency_unavailable", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
    }

    [Fact]
    public async Task A_refusal_of_access_on_create_is_counted_unknown_and_logged()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.Unauthorized;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":401,"code":"invalid_credentials"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        Assert.IsType<OrderOutcomeUnknown>(result);

        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("invalid_credentials", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
        Assert.Contains(capture.LogLines, line => line.StartsWith("Warning 1008", StringComparison.Ordinal) && line.Contains(Operation.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_signer_is_named_as_such_and_is_not_an_unknown_order()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers, new UnreachableVaultSigner());

        var failure = await Assert.ThrowsAsync<Signing.RequestSigningException>(
            () => fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));

        // The vault's own exception is kept inside, and nothing reached the wire.
        Assert.IsType<HttpRequestException>(failure.InnerException);
        Assert.Empty(fixture.Stub.Requests);

        var duration = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.request.duration");
        Assert.Equal("signing", duration.Tags[AnisPartnersTelemetry.Tags.ErrorType]);

        // Not placed, so not an order to resume: a vault outage must not page anyone about Anis orders.
        Assert.DoesNotContain(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.DoesNotContain(capture.LogLines, line => line.StartsWith("Warning 1008", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verified_success_with_an_empty_body_is_counted_unknown()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = "null";

        var unknown = Assert.IsType<OrderOutcomeUnknown>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.Equal(Errors.PartnerErrorCode.InternalError, Assert.IsType<Errors.AnisApiException>(unknown.Cause).Code);

        var outcome = Assert.Single(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
        Assert.Equal("unknown", outcome.Tags[AnisPartnersTelemetry.Tags.OrderOutcome]);
        Assert.Equal("empty_body", outcome.Tags[AnisPartnersTelemetry.Tags.ErrorType]);
    }

    [Fact]
    public async Task A_replayed_refusal_is_not_counted_as_an_unknown_order()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Status = HttpStatusCode.InternalServerError;
        fixture.Stub.NoStore = true;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":500,"code":"internal_error","requestId":"01J9"}""";

        var notPlaced = Assert.IsType<OrderNotPlaced>(await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken));
        Assert.IsType<Errors.DependencyUnavailableException>(notPlaced.Refusal);

        Assert.DoesNotContain(capture.Measurements, m => m.Instrument == "anis.partners.order.outcomes");
    }

    private sealed class UnreachableVaultSigner : Signing.IRequestSigner
    {
        public Guid KeyId { get; } = Guid.NewGuid();

        public ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signatureBase, CancellationToken cancellationToken)
            => throw new HttpRequestException("vault.partner.example: connection refused");
    }

    [Fact]
    public async Task No_secret_reaches_any_telemetry_signal()
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        // A reveal: the one response shape that carries credential plaintext.
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"soldCardId":"{{SoldCard:D}}","voucher":"{{Voucher}}","serialNumber":"{{Serial}}"}""";

        await fixture.Client.OwnedCards.RevealAsync(Wallet, SoldCard, TestContext.Current.CancellationToken);

        // An order that also releases credentials.
        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"completed","soldCards":[{"soldCardId":"{{SoldCard:D}}","voucher":"{{Voucher}}","serialNumber":"{{Serial}}"}]}""";

        await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var emitted = capture.Everything;
        var request = fixture.Stub.LastRequest!;

        // Credential plaintext.
        Assert.DoesNotContain(Voucher, emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(Serial, emitted, StringComparison.Ordinal);

        // The signature, and the base that would let an attacker test candidates offline.
        Assert.DoesNotContain(request.Headers.GetValues("Signature").Single(), emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Headers.GetValues("Signature-Input").Single(), emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("@signature-params", emitted, StringComparison.Ordinal);

        // The nonce.
        Assert.DoesNotContain(request.Headers.GetValues("Nonce").Single(), emitted, StringComparison.Ordinal);

        // And the telemetry is not empty, or the assertions above would pass vacuously.
        Assert.NotEmpty(capture.Spans);
        Assert.NotEmpty(capture.Measurements);
        Assert.NotEmpty(capture.LogLines);
    }
}
