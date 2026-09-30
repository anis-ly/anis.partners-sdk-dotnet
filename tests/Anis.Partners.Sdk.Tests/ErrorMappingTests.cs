using System.Net;
using Anis.Partners.Sdk.Errors;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Every public code has one deliberate exception type and one deliberate order outcome.</summary>
/// <remarks>
/// The table is the decision record. A code added to the catalogue fails
/// <see cref="Every_public_code_has_a_decision"/> until someone decides what a partner should catch and
/// whether the order happened — rather than falling silently into the base type.
/// </remarks>
public sealed class ErrorMappingTests
{
    private static readonly Dictionary<string, (Type Type, OrderRefusalOutcome Outcome)> Decisions = new(StringComparer.Ordinal)
    {
        // Owner business verdicts on an order: final, nothing charged.
        ["insufficient_balance"] = (typeof(InsufficientBalanceException), OrderRefusalOutcome.NotPlaced),
        ["price_changed"] = (typeof(PriceChangedException), OrderRefusalOutcome.NotPlaced),
        ["quantity_unavailable"] = (typeof(OutOfStockException), OrderRefusalOutcome.NotPlaced),
        ["card_unavailable"] = (typeof(OutOfStockException), OrderRefusalOutcome.NotPlaced),
        ["owner_limit_exceeded"] = (typeof(LimitExceededException), OrderRefusalOutcome.NotPlaced),
        ["daily_limit_exceeded"] = (typeof(LimitExceededException), OrderRefusalOutcome.NotPlaced),
        ["allowed_debt_consent_required"] = (typeof(AnisApiException), OrderRefusalOutcome.NotPlaced),
        ["purchase_not_allowed"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["wallet_disabled"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["wallet_expired"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["business_subscription_required"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["account_inactive"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["binding_not_authorized"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["currency_not_supported"] = (typeof(ValidationFailedException), OrderRefusalOutcome.NotPlaced),
        ["idempotency_conflict"] = (typeof(IdempotencyConflictException), OrderRefusalOutcome.NotPlaced),

        // Refused at the door, before any order exists.
        ["insufficient_scope"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["source_ip_not_allowed"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["invalid_credentials"] = (typeof(InvalidCredentialsException), OrderRefusalOutcome.NotPlaced),
        ["signature_expired"] = (typeof(InvalidCredentialsException), OrderRefusalOutcome.NotPlaced),
        ["invalid_content_digest"] = (typeof(AnisApiException), OrderRefusalOutcome.NotPlaced),
        ["validation_failed"] = (typeof(ValidationFailedException), OrderRefusalOutcome.NotPlaced),
        ["wallet_not_granted"] = (typeof(ResourceNotFoundException), OrderRefusalOutcome.NotPlaced),
        ["resource_not_found"] = (typeof(ResourceNotFoundException), OrderRefusalOutcome.NotPlaced),
        ["card_not_found"] = (typeof(ResourceNotFoundException), OrderRefusalOutcome.NotPlaced),

        // No owner decision was reached: the order may still complete.
        ["dependency_unavailable"] = (typeof(DependencyUnavailableException), OrderRefusalOutcome.Unknown),
        ["request_timeout"] = (typeof(DependencyUnavailableException), OrderRefusalOutcome.Unknown),
        ["internal_error"] = (typeof(DependencyUnavailableException), OrderRefusalOutcome.Unknown),
        ["operation_processing"] = (typeof(AnisApiException), OrderRefusalOutcome.Unknown),

        // Refused at the door, but only because an identical signed copy got in first — and that copy may
        // have placed the order. Resume the same id; a new id could buy twice.
        ["replay_detected"] = (typeof(ReplayDetectedException), OrderRefusalOutcome.Unknown),

        // Refused at the door, but the call may be a resend of an attempt that is still selling (a host
        // retry handler, or a create sent again after a timeout). A first-attempt rate limit placed nothing,
        // but the SDK cannot tell the two apart: resume the same id.
        ["rate_limited"] = (typeof(RateLimitedException), OrderRefusalOutcome.Unknown),

        // Reveals.
        ["reveal_not_allowed"] = (typeof(AuthorizationException), OrderRefusalOutcome.NotPlaced),
        ["invoice_reveal_limit_exceeded"] = (typeof(AnisApiException), OrderRefusalOutcome.NotPlaced),

        // Enrollment.
        ["invitation_invalid"] = (typeof(EnrollmentRefusedException), OrderRefusalOutcome.NotPlaced),
        ["challenge_expired"] = (typeof(EnrollmentRefusedException), OrderRefusalOutcome.NotPlaced),
        ["key_proof_invalid"] = (typeof(EnrollmentRefusedException), OrderRefusalOutcome.NotPlaced),
        ["key_duplicate"] = (typeof(EnrollmentRefusedException), OrderRefusalOutcome.NotPlaced),
    };

    public static TheoryData<string> Codes => new(Decisions.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void Every_public_code_has_a_decision()
    {
        Assert.Empty(PartnerErrorCodes.All.Except(Decisions.Keys, StringComparer.Ordinal));
        Assert.Empty(Decisions.Keys.Except(PartnerErrorCodes.All, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task A_code_becomes_its_decided_exception_and_outcome(string code)
    {
        var failure = await RefuseWith(HttpStatusCode.Conflict, $$"""{"type":"about:blank","title":"t","status":409,"code":"{{code}}","requestId":"01J9"}""");

        var (type, outcome) = Decisions[code];

        Assert.IsType(type, failure);
        Assert.Equal(outcome, failure.OrderOutcome);
        Assert.Equal(code, failure.RawCode);
        Assert.Equal("01J9", failure.RequestId);
    }

    [Fact]
    public async Task A_code_this_version_does_not_know_is_treated_as_an_open_order()
    {
        var failure = await RefuseWith(HttpStatusCode.Conflict, """{"type":"about:blank","title":"t","status":409,"code":"a_code_from_the_future"}""");

        // Resuming is always safe; a new operation id is not.
        Assert.Equal(PartnerErrorCode.Unknown, failure.Code);
        Assert.Equal("a_code_from_the_future", failure.RawCode);
        Assert.Equal(OrderRefusalOutcome.Unknown, failure.OrderOutcome);
        Assert.False(failure.IsRetryable);
    }

    [Theory]
    [InlineData("internal_error", 500)]              // what Anis replays when a recorded code left its catalogue
    [InlineData("a_code_from_the_future", 409)]      // a code this SDK version does not know
    [InlineData("replay_detected", 409)]             // open when fresh — closed once replayed
    public async Task A_replayed_refusal_is_a_closed_order_whatever_its_code(string code, int status)
    {
        var failure = await RefuseWith(
            (HttpStatusCode)status,
            $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}","requestId":"01J9"}""",
            replayed: true);

        // Anis marks a refusal replayed only when the order is already closed on it; resuming would return
        // this same answer forever.
        Assert.True(failure.IsReplayed);
        Assert.Equal(OrderRefusalOutcome.NotPlaced, failure.OrderOutcome);
    }

    [Fact]
    public async Task A_signed_body_that_is_not_a_problem_is_still_a_refusal()
    {
        var failure = await RefuseWith(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        Assert.IsType<DependencyUnavailableException>(failure);
        Assert.Equal(HttpStatusCode.BadGateway, failure.Status);
        Assert.Equal(OrderRefusalOutcome.Unknown, failure.OrderOutcome);
    }

    // Through the whole public pipeline: signed on the way out, verified on the way back, then mapped into the outcome.
    private static async Task<AnisApiException> RefuseWith(HttpStatusCode status, string body, bool replayed = false)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = status;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = body;
        fixture.Stub.IdempotencyReplayed = replayed;

        var outcome = await fixture.Client.Orders.CreateAsync(
            Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26"),
            Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34"),
            new Models.CreateOrderRequest
            {
                CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
                Quantity = 1,
                ExpectedUnitPrice = new Models.Money(10.500m, "LYD"),
                ExpectedTotal = new Models.Money(10.500m, "LYD"),
            },
            TestContext.Current.CancellationToken);

        var refusal = outcome switch
        {
            Models.OrderNotPlaced notPlaced => notPlaced.Refusal,
            Models.OrderOutcomeUnknown { Cause: AnisApiException cause } => cause,
            _ => throw new InvalidOperationException($"Expected a refusal, got {outcome.GetType().Name}."),
        };

        // The case the partner switches on must agree with what the refusal says about the order.
        Assert.IsType(refusal.OrderOutcome is OrderRefusalOutcome.NotPlaced ? typeof(Models.OrderNotPlaced) : typeof(Models.OrderOutcomeUnknown), outcome);

        return refusal;
    }
}
