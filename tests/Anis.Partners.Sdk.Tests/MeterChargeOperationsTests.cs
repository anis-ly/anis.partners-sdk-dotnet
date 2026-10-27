using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Meter packages, charges, receipts, refunds and the recovery of an interrupted purchase.</summary>
public sealed class MeterChargeOperationsTests
{
    private static readonly Guid Wallet = ProductBodies.Wallet;
    private static readonly Guid Operation = ProductBodies.MeterCharge;

    private static CreateMeterChargeRequest Charge => new()
    {
        PackageId = ProductBodies.Package,
        MeterNumber = "0268000710262",
        ExpectedTotal = new Money(97.500m, "LYD"),
        ExternalReference = "POS-77-INV-1093",
    };

    [Fact]
    public async Task A_meter_packages_page_keeps_an_omitted_business_price_absent()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = ProductBodies.Fill("""
            {"items":[{"id":"@package@","name":"باقة 100 دينار","gecolPackageName":"PKG_100","logo":"https://cdn.anis.ly/meter/packages/100.png",
            "unitPrice":{"amount":"97.500","currency":"LYD"},"personalPrice":{"amount":"100.000","currency":"LYD"}}],"nextCursor":null}
            """);

        var page = await fixture.Client.MeterCharges.GetMeterPackagesPageAsync(Wallet, cancellationToken: TestContext.Current.CancellationToken);

        var package = Assert.Single(page.Items);

        Assert.Null(package.BusinessPrice);
        Assert.Equal("باقة 100 دينار", package.Name);
        Assert.Equal(97.500m, package.UnitPrice.Amount);
        Assert.Null(page.NextCursor);
        Assert.Equal($"/v1/wallets/{Wallet:D}/meter-packages", fixture.Stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.False(fixture.Stub.LastRequest.Headers.Contains("Nonce"));
    }

    [Fact]
    public async Task A_meter_create_uses_the_callers_operation_id()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/meter-charges/{Operation:D}";
        fixture.Stub.RetryAfterSeconds = 7;
        fixture.Stub.Body = ProductBodies.MeterAccepted();

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var processing = Assert.IsType<MeterChargeProcessing>(result);
        var request = fixture.Stub.LastRequest!;
        var body = fixture.Stub.LastRequestBody!;
        using var sent = JsonDocument.Parse(body);

        Assert.Equal(Operation, processing.OperationId);
        Assert.Equal(TimeSpan.FromSeconds(7), processing.RetryAfter);
        Assert.Equal($"/v1/meter-charges/{Operation:D}", processing.Location!.OriginalString);
        Assert.Equal("processing", processing.Accepted!.Status);
        Assert.Equal("meterCharge", processing.Accepted.OperationType);

        // The id travels as the header only; the body holds the purchase and nothing else.
        Assert.Equal(Operation.ToString("D"), request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(["expectedTotal", "externalReference", "meterNumber", "packageId"],
            sent.RootElement.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal("0268000710262", sent.RootElement.GetProperty("meterNumber").GetString());
        Assert.Equal("97.500", sent.RootElement.GetProperty("expectedTotal").GetProperty("amount").GetString());
        Assert.Equal($"/v1/wallets/{Wallet:D}/meter-charges", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, request.Method);

        // The digest the signature covers is the digest of the bytes that were transmitted.
        Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:", request.Content!.Headers.GetValues("Content-Digest").Single());
        Assert.Equal(request.Headers.GetValues("Nonce").Single(), NonceOf(request));
    }

    [Fact]
    public async Task A_meter_read_returns_every_token_in_order_with_the_receipt_and_taxes()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.NoStore = true;
        fixture.Stub.Body = ProductBodies.MeterSucceeded();

        var charge = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.Equal(MeterChargeStatus.Succeeded, charge.Status);
        Assert.Equal(["KCT1", "KCT2", "STS"], charge.Receipt!.Tokens.Select(token => token.Label));
        Assert.Equal("1234 5678 9012 3456 7890", charge.Receipt.Tokens[0].Code);
        Assert.Equal("88.640", charge.Receipt.CreditedAmount);
        Assert.Equal(["5.000", "3.860"], charge.Receipt.Taxes.Select(tax => tax.Amount));
        Assert.Equal("ضريبة الاستهلاك", charge.Receipt.Taxes[0].Description);
        Assert.Equal(97.500m, charge.Total.Amount);
        Assert.Equal(100.000m, charge.PersonalPrice.Amount);
        Assert.NotNull(charge.CompletedAt);
        Assert.Equal($"/v1/meter-charges/{Operation:D}", fixture.Stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_token_never_reaches_a_log_through_formatting()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterSucceeded();

        var charge = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("5821", charge.Receipt!.Tokens[2].ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", charge.Receipt.Tokens[2].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_processing_meter_read_has_no_receipt_and_no_completion_time()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterProcessing();

        var charge = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.Equal(MeterChargeStatus.Processing, charge.Status);
        Assert.Null(charge.Receipt);
        Assert.Null(charge.CompletedAt);
        Assert.Equal(97.500m, charge.Total.Amount);
        Assert.Equal(100.000m, charge.PersonalPrice.Amount);
    }

    [Theory]
    [InlineData("refunded", true)]
    [InlineData("notCharged", false)]
    public async Task A_failed_meter_charge_with_a_settled_refund_is_a_final_result_not_a_refusal(string refund, bool withAmount)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterFailed("meter_debt_exceeds_package", refund, withAmount);

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var final = Assert.IsType<MeterChargeFinal>(result);

        Assert.Equal(MeterChargeStatus.Failed, final.Charge.Status);
        Assert.Equal(MeterFailureCode.MeterDebtExceedsPackage, final.Charge.Failure!.Code);
        Assert.Equal(Enum.Parse<MeterRefundStatus>(refund, ignoreCase: true), final.Charge.Refund!.Status);
        Assert.Equal(withAmount, final.Charge.Refund.Amount is not null);
        Assert.Null(final.Charge.CompletedAt);
        Assert.Null(final.Charge.Receipt);
    }

    [Theory]
    [InlineData("meter_number_invalid", MeterFailureCode.MeterNumberInvalid)]
    [InlineData("meter_not_whitelisted", MeterFailureCode.MeterNotWhitelisted)]
    [InlineData("meter_not_linked", MeterFailureCode.MeterNotLinked)]
    [InlineData("meter_removed", MeterFailureCode.MeterRemoved)]
    [InlineData("meter_inactive", MeterFailureCode.MeterInactive)]
    [InlineData("meter_account_blocked", MeterFailureCode.MeterAccountBlocked)]
    [InlineData("meter_data_incomplete", MeterFailureCode.MeterDataIncomplete)]
    [InlineData("meter_debt_exceeds_package", MeterFailureCode.MeterDebtExceedsPackage)]
    [InlineData("package_unavailable", MeterFailureCode.PackageUnavailable)]
    [InlineData("package_limit_reached", MeterFailureCode.PackageLimitReached)]
    [InlineData("token_not_written", MeterFailureCode.TokenNotWritten)]
    [InlineData("insufficient_balance", MeterFailureCode.InsufficientBalance)]
    [InlineData("gecol_unavailable", MeterFailureCode.GecolUnavailable)]
    [InlineData("charge_failed", MeterFailureCode.ChargeFailed)]
    [InlineData("a_failure_from_the_future", MeterFailureCode.Unknown)]
    public async Task Every_meter_failure_code_is_known_and_a_future_one_keeps_its_raw_spelling(string wire, MeterFailureCode expected)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterFailed(wire, "refunded", withAmount: true);

        var charge = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.Equal(expected, charge.Failure!.Code);
        Assert.Equal(wire, charge.Failure.RawCode);
        Assert.Equal("The meter's debt is larger than the package", charge.Failure.Message!.En);
    }

    [Fact]
    public async Task A_200_that_is_still_processing_is_not_final()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterProcessing();

        var result = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.Equal(MeterChargeStatus.Processing, Assert.IsType<MeterChargeProcessing>(result).Charge!.Status);
    }

    [Fact]
    public async Task A_status_this_version_does_not_know_stays_unresolved()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterProcessing("a_state_from_the_future");

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<MeterChargeOutcomeUnknown>(result);
    }

    [Fact]
    public async Task A_202_on_the_read_raises_the_processing_acknowledgment_without_following_the_location()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/meter-charges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.MeterAccepted();

        var processing = await Assert.ThrowsAsync<MeterChargeProcessingException>(
            () => fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken));

        Assert.Equal(Operation, processing.Accepted.OperationId);
        Assert.Equal(TimeSpan.FromSeconds(5), processing.RetryAfter);
        Assert.Single(fixture.Stub.Requests);
    }

    [Theory]
    [InlineData("""{"operationId":"3c1e9b7a-0d42-4f5e-8a61-2b9c7d0e5f13","operationType":"meterCharge","status":"processing","extra":1}""")]
    [InlineData("""{"operationId":"3c1e9b7a-0d42-4f5e-8a61-2b9c7d0e5f13","operationType":"directRecharge","status":"processing"}""")]
    [InlineData("""{"operationId":"3c1e9b7a-0d42-4f5e-8a61-2b9c7d0e5f13","status":"processing"}""")]
    [InlineData("""{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","operationType":"meterCharge","status":"processing"}""")]
    public async Task A_202_that_breaks_the_three_member_contract_is_not_trusted(string body)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = body;

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<MeterChargeOutcomeUnknown>(result);

        Assert.Equal(Operation, unknown.OperationId);
        Assert.IsType<JsonException>(unknown.Cause);
    }

    [Theory]
    [InlineData("meter_rejected", 422)]
    [InlineData("package_unavailable", 409)]
    [InlineData("price_changed", 409)]
    [InlineData("insufficient_balance", 409)]
    [InlineData("validation_failed", 422)]
    public async Task A_definitive_business_refusal_on_create_is_not_placed(string code, int status)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}","requestId":"01J9"}""";

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var notPlaced = Assert.IsType<MeterChargeNotPlaced>(result);

        Assert.Equal(Operation, notPlaced.OperationId);
        Assert.Equal(code, notPlaced.Refusal.RawCode);
    }

    [Fact]
    public async Task A_fresh_refusal_on_resume_stays_unknown_but_a_replayed_one_is_final()
    {
        using var fresh = new PipelineFixture();

        fresh.Stub.Status = HttpStatusCode.UnprocessableEntity;
        fresh.Stub.Body = """{"type":"about:blank","title":"t","status":422,"code":"meter_rejected"}""";

        Assert.IsType<MeterChargeOutcomeUnknown>(
            await fresh.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken));

        using var replayed = new PipelineFixture();

        replayed.Stub.Status = HttpStatusCode.UnprocessableEntity;
        replayed.Stub.IdempotencyReplayed = true;
        replayed.Stub.Body = """{"type":"about:blank","title":"t","status":422,"code":"meter_rejected"}""";

        Assert.IsType<MeterChargeNotPlaced>(
            await replayed.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AlreadyExistsForeignId_After60Seconds_IsIdempotencyConflict_and_stays_unknown()
    {
        using var fixture = new PipelineFixture();

        // Inside the lag window the gateway answers that the id is still being processed.
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.RetryAfterSeconds = 5;
        fixture.Stub.Body = ProductBodies.MeterAccepted();

        var early = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<MeterChargeProcessing>(early);

        // After the window, with the scoped read still missing, the gateway closes the repeat as idempotency_conflict. That
        // does not say the first request failed, so the SDK never calls it "not placed": the partner reads the id first.
        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.RetryAfterSeconds = null;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = """{"type":"https://developers.anis.ly/errors/idempotency-conflict","title":"Conflict","status":409,"code":"idempotency_conflict"}""";

        var late = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<MeterChargeOutcomeUnknown>(late);

        Assert.Equal(PartnerErrorCode.IdempotencyConflict, Assert.IsAssignableFrom<AnisApiException>(unknown.Cause).Code);
        Assert.Equal(Operation, unknown.OperationId);

        // Both attempts carry the original id and the identical saved body: no replacement purchase was made.
        Assert.Equal(2, fixture.Stub.Requests.Count);
        Assert.All(fixture.Stub.Requests, request => Assert.Equal(Operation.ToString("D"), request.Headers.GetValues("Idempotency-Key").Single()));
        Assert.Equal(fixture.Stub.RequestBodies[0], fixture.Stub.RequestBodies[1]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_idempotency_conflict_on_create_or_resume_is_never_not_placed(bool resuming, bool replayed)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.IdempotencyReplayed = replayed;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"idempotency_conflict"}""";

        var result = resuming
            ? await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken)
            : await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<MeterChargeOutcomeUnknown>(result);

        Assert.Equal(Operation, unknown.OperationId);
        Assert.Equal(PartnerErrorCode.IdempotencyConflict, Assert.IsAssignableFrom<AnisApiException>(unknown.Cause).Code);
    }

    [Theory]
    [InlineData(409, "insufficient_balance")]
    [InlineData(409, "price_changed")]
    [InlineData(409, "package_unavailable")]
    [InlineData(409, "idempotency_conflict")]
    [InlineData(422, "meter_rejected")]
    [InlineData(422, "amount_out_of_range")]
    public async Task Reading_an_operation_whose_create_was_refused_raises_the_recorded_refusal(int status, string code)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var refusal = await Assert.ThrowsAnyAsync<AnisApiException>(
            () => fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken));

        Assert.Equal(code, refusal.RawCode);
        Assert.Equal((HttpStatusCode)status, refusal.Status);
        Assert.True(refusal.IsReplayed);
        Assert.Single(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_failed_meter_charge_with_a_pending_refund_is_not_final_it_stays_processing_with_the_read()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.RetryAfterSeconds = 9;
        fixture.Stub.Body = ProductBodies.MeterFailed("meter_debt_exceeds_package", "pending", withAmount: true);

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var processing = Assert.IsType<MeterChargeProcessing>(result);

        Assert.Equal(MeterChargeStatus.Failed, processing.Charge!.Status);
        Assert.Equal(MeterRefundStatus.Pending, processing.Charge.Refund!.Status);
        Assert.Equal(TimeSpan.FromSeconds(9), processing.RetryAfter);

        var resumed = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<MeterChargeProcessing>(resumed);
    }

    [Fact]
    public async Task A_failed_meter_charge_with_a_pending_refund_is_a_200_read_that_carries_the_amount()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterFailed("meter_debt_exceeds_package", "pending", withAmount: true);

        var charge = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.Equal(MeterChargeStatus.Failed, charge.Status);
        Assert.Equal(MeterRefundStatus.Pending, charge.Refund!.Status);
        Assert.Equal(97.500m, charge.Refund.Amount!.Value.Amount);
    }

    [Fact]
    public async Task A_timeout_leaves_the_charge_open_and_the_same_id_resumes()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Failure = () => new TaskCanceledException("timeout", new TimeoutException());

        var created = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.Equal(Operation, Assert.IsType<MeterChargeOutcomeUnknown>(created).OperationId);

        fixture.Stub.Failure = null;
        fixture.Stub.Body = ProductBodies.MeterSucceeded();

        var resumed = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<MeterChargeFinal>(resumed);
        Assert.Equal(Operation.ToString("D"), fixture.Stub.LastRequest!.Headers.GetValues("Idempotency-Key").Single());
    }

    [Theory]
    [InlineData("insufficient_scope", 403)]
    [InlineData("wallet_not_granted", 404)]
    [InlineData("invalid_credentials", 401)]
    public async Task An_access_refusal_on_resume_does_not_prove_the_charge_absent(string code, int status)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var result = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<MeterChargeOutcomeUnknown>(result);

        Assert.Equal(TimeSpan.FromSeconds(60), unknown.SuggestedDelay);
    }

    [Fact]
    public async Task An_answer_for_another_operation_is_not_trusted()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.MeterSucceeded(Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34"));

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<JsonException>(Assert.IsType<MeterChargeOutcomeUnknown>(result).Cause);
    }

    [Fact]
    public async Task A_call_the_caller_cancelled_is_rethrown()
    {
        using var fixture = new PipelineFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, cancelled.Token));
    }

    [Fact]
    public async Task A_signer_failure_before_sending_is_not_an_unknown_charge()
    {
        using var fixture = new PipelineFixture(signer: new UnreachableSigner());

        await Assert.ThrowsAsync<Signing.RequestSigningException>(
            () => fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Stub.Requests);
    }

    [Fact]
    public async Task An_answer_without_the_signature_a_signed_route_requires_is_discarded_as_unknown()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.Body = ProductBodies.MeterSucceeded();

        var result = await fixture.Client.MeterCharges.CreateAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.IsType<UnverifiableResponseException>(Assert.IsType<MeterChargeOutcomeUnknown>(result).Cause);

        await Assert.ThrowsAsync<UnverifiableResponseException>(
            () => fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_empty_operation_id_never_leaves_the_process()
    {
        using var fixture = new PipelineFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Client.MeterCharges.CreateAsync(Wallet, Guid.Empty, Charge, TestContext.Current.CancellationToken));

        Assert.Null(fixture.Stub.LastRequest);
    }

    [Fact]
    public async Task A_list_encodes_every_filter_once_and_keeps_them_on_every_page()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Bodies.Enqueue("{\"items\":[" + Summary("0268000710262") + "],\"nextCursor\":\"a+b/c=&d\"}");
        fixture.Stub.Bodies.Enqueue("{\"items\":[" + Summary("0268000710262") + "],\"nextCursor\":null}");

        var query = new MeterChargesQuery
        {
            Status = MeterChargeStatus.Succeeded,
            MeterNumber = "0268000710262",
            From = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            PageSize = 50,
        };

        var seen = new List<MeterChargeSummary>();

        await foreach (var summary in fixture.Client.MeterCharges.GetMeterChargesAsync(Wallet, query, TestContext.Current.CancellationToken))
            seen.Add(summary);

        Assert.Equal(2, seen.Count);
        Assert.Equal(
            "?status=succeeded&meterNumber=0268000710262&from=2026-10-01T00%3A00%3A00.0000000Z&to=2026-10-20T12%3A00%3A00.0000000Z&pageSize=50",
            fixture.Stub.Requests[0].RequestUri!.Query);
        Assert.Equal(
            "?status=succeeded&meterNumber=0268000710262&from=2026-10-01T00%3A00%3A00.0000000Z&to=2026-10-20T12%3A00%3A00.0000000Z&cursor=a%2Bb%2Fc%3D%26d&pageSize=50",
            fixture.Stub.Requests[1].RequestUri!.Query);
        Assert.Equal(97.500m, seen[0].Total.Amount);
        Assert.Equal("POS-77-INV-1093", seen[0].ExternalReference);
        Assert.Null(fixture.Stub.Requests[0].Content);
    }

    [Fact]
    public async Task A_list_with_a_cursor_that_does_not_advance_stops_with_an_error()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Bodies.Enqueue("""{"items":[],"nextCursor":"same"}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[],"nextCursor":"same"}""");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in fixture.Client.MeterCharges.GetMeterChargesAsync(Wallet, null, TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task A_list_validation_refusal_is_a_typed_error()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Status = HttpStatusCode.UnprocessableEntity;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":422,"code":"validation_failed","extensions":{"fields":{"to":"The window is longer than 31 days"}}}""";

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => fixture.Client.MeterCharges.GetMeterChargesPageAsync(Wallet, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(PartnerErrorCode.ValidationFailed, failure.Code);
    }

    [Fact]
    public async Task A_create_scope_does_not_broaden_a_read_into_a_create()
    {
        // The route table gives a create route its create scope and a read route its read scope.
        var create = Operations.PartnerRoutes.All.Single(route => route.RouteId == "partner.meter-charges.create");
        var read = Operations.PartnerRoutes.All.Single(route => route.RouteId == "partner.meter-charges.read");

        Assert.Equal("meter_charges:create", create.RequiredScope);
        Assert.Equal("meter_charges:read", read.RequiredScope);
        await Task.CompletedTask;
    }

    private static string Summary(string meter) => ProductBodies.Fill("""
        {"operationId":"@id@","status":"succeeded","meterNumber":"@meter@",
        "package":{"id":"@package@","name":"باقة 100 دينار","gecolPackageName":"PKG_100"},
        "total":{"amount":"97.500","currency":"LYD"},"externalReference":"POS-77-INV-1093",
        "createdAt":"2026-10-20T08:12:44Z","completedAt":"2026-10-20T08:13:02Z"}
        """, ("id", Operation.ToString("D")), ("meter", meter));

    private sealed class UnreachableSigner : Signing.IRequestSigner
    {
        public Guid KeyId { get; } = Guid.NewGuid();

        public ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signatureBase, CancellationToken cancellationToken)
            => throw new HttpRequestException("vault.partner.example: connection refused");
    }

    private static string NonceOf(HttpRequestMessage request)
    {
        var input = request.Headers.GetValues("Signature-Input").Single();
        var marker = ";nonce=\"";
        var start = input.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;

        return input[start..input.IndexOf('"', start)];
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("processing, succeeded")]
    [InlineData("succeeded, failed")]
    [InlineData("Succeeded")]
    [InlineData(" succeeded ")]
    public async Task A_non_wire_status_never_becomes_a_final_meter_outcome(string status)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Body = ProductBodies.MeterSucceeded().Replace("\"succeeded\"", JsonSerializer.Serialize(status), StringComparison.Ordinal);

        var read = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);
        var result = await fixture.Client.MeterCharges.ResumeAsync(Wallet, Operation, Charge, TestContext.Current.CancellationToken);

        Assert.Equal(MeterChargeStatus.Unknown, read.Status);
        Assert.IsType<MeterChargeOutcomeUnknown>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_meter_read_accepts_optional_receipt_and_failure_members(bool explicitNull)
    {
        using var fixture = new PipelineFixture();
        var body = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.MeterSucceeded())!;
        foreach (var member in new[] { "receiptNumber", "creditedAmount" })
        {
            if (explicitNull) body["receipt"]![member] = null;
            else body["receipt"]!.AsObject().Remove(member);
        }
        fixture.Stub.Body = body.ToJsonString();
        var read = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);
        Assert.Null(read.Receipt!.ReceiptNumber);
        Assert.Null(read.Receipt.CreditedAmount);
        Assert.Equal(3, read.Receipt.Tokens.Count);
        Assert.Equal(2, read.Receipt.Taxes.Count);

        body = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.MeterFailed("charge_failed", "refunded", true))!;
        if (explicitNull) body["failure"]!["message"] = null;
        else body["failure"]!.AsObject().Remove("message");
        fixture.Stub.Body = body.ToJsonString();
        read = await fixture.Client.MeterCharges.GetAsync(Operation, TestContext.Current.CancellationToken);
        Assert.Null(read.Failure!.Message);
        Assert.Equal(MeterFailureCode.ChargeFailed, read.Failure.Code);
    }
}
