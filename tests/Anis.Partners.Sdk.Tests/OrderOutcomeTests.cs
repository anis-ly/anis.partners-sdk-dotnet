using System.Net;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The five order outcomes, including the refusals a caller branches on, as the live gateway sends them.</summary>
/// <remarks>
/// Each case runs through the real pipeline — signed on the way out, verified on the way back — so these
/// also prove the handlers are ordered correctly and that the request binding survives.
/// </remarks>
public sealed class OrderOutcomeTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid Operation = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");
    private static readonly Guid Invoice = Guid.Parse("c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51");

    private static CreateOrderRequest Order => new()
    {
        CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
        Quantity = 2,
        ExpectedUnitPrice = new Money(10.500m, "LYD"),
        ExpectedTotal = new Money(21.000m, "LYD"),
    };

    [Fact]
    public async Task A_201_is_completed_and_carries_the_credentials()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Location = $"/v1/orders/{Operation:D}";
        fixture.Stub.Body =
            $$"""{"operationId":"{{Operation:D}}","status":"completed","soldCards":[{"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","voucher":"1234"}]}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<OrderCompleted>(result);

        Assert.Single(completed.Credentials);
        Assert.False(completed.CodesWithheld);
        Assert.Equal("1234", completed.Credentials[0].Voucher);
    }

    // Anis withheld the codes of an order that was placed and paid (a card invalidated, a code not releasable): the
    // first report is completed with no credentials and no replay marker. It is still completed: never buy it again.
    [Fact]
    public async Task A_completion_whose_codes_are_withheld_is_completed_with_no_credentials()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Location = $"/v1/orders/{Operation:D}";
        fixture.Stub.Body =
            $$"""{"operationId":"{{Operation:D}}","status":"completed"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<OrderCompleted>(result);

        Assert.Empty(completed.Credentials);
        Assert.True(completed.CodesWithheld);
    }

    [Fact]
    public async Task A_repeat_after_completion_is_replayed_and_carries_no_credentials()
    {
        using var fixture = new PipelineFixture();

        // What the gateway sends today for the same id after a completion: the ORIGINAL status, its
        // Location, the covered marker, and the order handle only (QA 2026-09-23).
        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.Location = $"/v1/orders/{Operation:D}";
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"completed","invoiceId":"{{Invoice:D}}"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var replayed = Assert.IsType<OrderReplayed>(result);

        Assert.Null(replayed.Order.SoldCards);
        Assert.Equal(Invoice, replayed.Order.InvoiceId);
        Assert.Equal(OrderStatus.Completed, replayed.Order.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_resume_that_recovers_the_completion_returns_the_credentials(bool markedReplayed)
    {
        using var fixture = new PipelineFixture();

        // The first answer was lost; the resume learns from the owner that the sale went through. This 200
        // is the first report of the completion and carries the codes. Gateways before the 2026-09-23 fix
        // also marked it replayed — the credentials must win either way, never be hidden behind the marker.
        fixture.Stub.Status = HttpStatusCode.OK;
        fixture.Stub.NoStore = true;
        fixture.Stub.IdempotencyReplayed = markedReplayed;
        fixture.Stub.Location = $"/v1/orders/{Operation:D}";
        fixture.Stub.Body =
            $$"""{"operationId":"{{Operation:D}}","status":"completed","invoiceId":"{{Invoice:D}}","soldCards":[{"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","voucher":"1234"},{"soldCardId":"6e9a3f52-1c48-4b27-9d60-5f2a8c7e3b19","voucher":"5678"}]}""";

        var result = await fixture.Client.Orders.ResumeAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var completed = Assert.IsType<OrderCompleted>(result);

        Assert.Equal(["1234", "5678"], completed.Credentials.Select(credential => credential.Voucher));
    }

    [Fact]
    public async Task A_recorded_refusal_says_it_was_replayed_and_that_nothing_was_placed()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.NoStore = true;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/insufficient-balance","title":"Insufficient balance","status":409,"code":"insufficient_balance","requestId":"01J9"}""";

        var result = await fixture.Client.Orders.ResumeAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        var notPlaced = Assert.IsType<OrderNotPlaced>(result);
        var refusal = Assert.IsType<InsufficientBalanceException>(notPlaced.Refusal);
        Assert.True(refusal.IsReplayed);
        Assert.Equal(OrderRefusalOutcome.NotPlaced, refusal.OrderOutcome);
        Assert.Equal(Operation, notPlaced.OperationId);
    }

    [Fact]
    public async Task A_fresh_refusal_is_not_marked_replayed()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/quantity-unavailable","title":"t","status":409,"code":"quantity_unavailable"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        var notPlaced = Assert.IsType<OrderNotPlaced>(result);
        var refusal = Assert.IsType<OutOfStockException>(notPlaced.Refusal);
        Assert.False(refusal.IsReplayed);
        Assert.Equal(OrderRefusalOutcome.NotPlaced, refusal.OrderOutcome);
        Assert.Equal(Operation, notPlaced.OperationId);
    }

    [Fact]
    public async Task An_unavailable_dependency_leaves_the_order_open_for_a_resume()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.ServiceUnavailable;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/dependency-unavailable","title":"t","status":503,"code":"dependency_unavailable"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        var cause = Assert.IsType<DependencyUnavailableException>(unknown.Cause);
        Assert.Equal(OrderRefusalOutcome.Unknown, cause.OrderOutcome);
        Assert.True(cause.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(5), unknown.SuggestedDelay);
        Assert.Equal(Operation, unknown.OperationId);
    }

    [Fact]
    public async Task A_202_is_processing_and_reports_when_to_resume()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.NoStore = true;
        fixture.Stub.Location = $"/v1/orders/{Operation:D}";
        fixture.Stub.RetryAfterSeconds = 5;
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"processing"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var processing = Assert.IsType<OrderProcessing>(result);

        Assert.Equal(TimeSpan.FromSeconds(5), processing.RetryAfter);
        Assert.Equal(Operation, processing.OperationId);
    }

    [Fact]
    public async Task A_price_change_is_not_placed_and_carries_its_typed_refusal()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/price-changed","title":"Price changed","status":409,"code":"price_changed","requestId":"01J9"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        var notPlaced = Assert.IsType<OrderNotPlaced>(result);
        var refusal = Assert.IsType<PriceChangedException>(notPlaced.Refusal);
        Assert.Equal(PartnerErrorCode.PriceChanged, refusal.Code);
        Assert.Equal("01J9", refusal.RequestId);
        Assert.False(refusal.IsRetryable);
    }

    [Fact]
    public async Task A_rate_limited_create_leaves_the_order_open_and_carries_the_signed_retry_after()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.TooManyRequests;
        fixture.Stub.RetryAfterSeconds = 30;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/rate-limited","title":"Too many","status":429,"code":"rate_limited"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.Equal(Operation, unknown.OperationId);
        Assert.Equal(TimeSpan.FromSeconds(30), unknown.SuggestedDelay);
        var refusal = Assert.IsType<RateLimitedException>(unknown.Cause);
        Assert.Equal(TimeSpan.FromSeconds(30), refusal.RetryAfter);
        Assert.True(refusal.IsRetryable);
        Assert.Equal(OrderRefusalOutcome.Unknown, refusal.OrderOutcome);
    }

    [Fact]
    public async Task A_rate_limited_create_without_a_retry_after_suggests_the_default_delay()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.TooManyRequests;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/rate-limited","title":"Too many","status":429,"code":"rate_limited"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.Equal(TimeSpan.FromSeconds(5), unknown.SuggestedDelay);
        var refusal = Assert.IsType<RateLimitedException>(unknown.Cause);
        Assert.Null(refusal.RetryAfter);
    }

    [Fact]
    public async Task A_fresh_refusal_of_a_resume_leaves_the_order_open()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.TooManyRequests;
        fixture.Stub.RetryAfterSeconds = 30;
        fixture.Stub.Body =
            """{"type":"https://developers.anis.ly/errors/rate-limited","title":"Too many","status":429,"code":"rate_limited"}""";

        var result = await fixture.Client.Orders.ResumeAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.Equal(TimeSpan.FromSeconds(30), unknown.SuggestedDelay);
        Assert.IsType<RateLimitedException>(unknown.Cause);
    }

    [Theory]
    [InlineData("invalid_credentials", 401, typeof(InvalidCredentialsException))]
    [InlineData("signature_expired", 401, typeof(InvalidCredentialsException))]
    [InlineData("insufficient_scope", 403, typeof(AuthorizationException))]
    [InlineData("wallet_not_granted", 404, typeof(ResourceNotFoundException))]
    [InlineData("malformed_signed_request", 400, typeof(AnisApiException))]
    public async Task A_refusal_at_the_door_on_create_leaves_the_order_open_and_suggests_a_minute(string code, int status, Type refusalType)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.Equal(Operation, unknown.OperationId);
        Assert.Equal(TimeSpan.FromSeconds(60), unknown.SuggestedDelay);
        Assert.IsType(refusalType, unknown.Cause);
        Assert.Equal(OrderRefusalOutcome.Unknown, ((AnisApiException)unknown.Cause).OrderOutcome);
    }

    [Theory]
    [InlineData("invalid_credentials", 401)]
    [InlineData("signature_expired", 401)]
    [InlineData("insufficient_scope", 403)]
    [InlineData("wallet_not_granted", 404)]
    [InlineData("malformed_signed_request", 400)]
    public async Task A_refusal_at_the_door_on_resume_suggests_a_minute(string code, int status)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var result = await fixture.Client.Orders.ResumeAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.Equal(TimeSpan.FromSeconds(60), unknown.SuggestedDelay);
    }

    [Fact]
    public async Task A_refusal_at_the_door_with_a_retry_after_suggests_that_wait()
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = HttpStatusCode.Forbidden;
        fixture.Stub.RetryAfterSeconds = 30;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":403,"code":"insufficient_scope"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<OrderOutcomeUnknown>(result).SuggestedDelay);
    }

    [Theory]
    [InlineData("invalid_credentials", 401)]
    [InlineData("signature_expired", 401)]
    [InlineData("insufficient_scope", 403)]
    [InlineData("wallet_not_granted", 404)]
    [InlineData("malformed_signed_request", 400)]
    public async Task A_replayed_refusal_at_the_door_is_not_placed(string code, int status)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.NoStore = true;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var notPlaced = Assert.IsType<OrderNotPlaced>(result);
        Assert.True(notPlaced.Refusal.IsReplayed);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5.000")]
    [InlineData("0.0004")]
    public async Task A_unit_price_that_is_not_positive_never_leaves_the_process(string amount)
    {
        using var fixture = new PipelineFixture();
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var bad = Order with
        {
            ExpectedUnitPrice = new Money(value, "LYD"),
            ExpectedTotal = new Money(value * 2, "LYD"),
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Client.Orders.CreateAsync(Wallet, Operation, bad, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Client.Orders.ResumeAsync(Wallet, Operation, bad, TestContext.Current.CancellationToken));
        Assert.Null(fixture.Stub.LastRequest);
    }

    [Fact]
    public async Task A_timeout_leaves_the_order_open_on_create_and_on_resume()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Failure = () => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

        var created = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);
        var resumed = await fixture.Client.Orders.ResumeAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        Assert.IsType<TaskCanceledException>(Assert.IsType<OrderOutcomeUnknown>(created).Cause);
        Assert.IsType<TaskCanceledException>(Assert.IsType<OrderOutcomeUnknown>(resumed).Cause);
    }

    [Fact]
    public async Task An_unexpected_failure_while_sending_leaves_the_order_open()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Failure = () => new InvalidOperationException("a host handler failed");

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        Assert.IsType<InvalidOperationException>(Assert.IsType<OrderOutcomeUnknown>(result).Cause);
    }

    [Fact]
    public async Task A_call_the_caller_cancelled_is_rethrown()
    {
        using var fixture = new PipelineFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, cancelled.Token));
    }

    [Fact]
    public async Task A_verified_success_with_invalid_order_json_leaves_the_order_open()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = "{";

        var result = await fixture.Client.Orders.CreateAsync(Wallet, Operation, Order, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(result);
        Assert.IsType<System.Text.Json.JsonException>(unknown.Cause);
        Assert.Equal(TimeSpan.FromSeconds(5), unknown.SuggestedDelay);
        Assert.Equal(Operation, unknown.OperationId);
    }

    [Fact]
    public async Task A_total_that_is_not_unit_times_quantity_never_leaves_the_process()
    {
        using var fixture = new PipelineFixture();

        var wrong = Order with { ExpectedTotal = new Money(20.000m, "LYD") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Client.Orders.CreateAsync(Wallet, Operation, wrong, TestContext.Current.CancellationToken));

        Assert.Null(fixture.Stub.LastRequest);
    }
}
