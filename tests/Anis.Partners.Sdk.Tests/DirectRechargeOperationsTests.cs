using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Direct recharge products, quotes, purchases, recipient correction and the recovery of an interrupted purchase.</summary>
public sealed class DirectRechargeOperationsTests
{
    private static readonly Guid Wallet = ProductBodies.Wallet;
    private static readonly Guid Product = ProductBodies.Product;
    private static readonly Guid Operation = ProductBodies.DirectRechargeId;

    private static CreateDirectRechargeRequest Recharge => new()
    {
        ProductId = Product,
        PayoutAmount = "1500.000",
        ExpectedTotal = new Money(237.500m, "LYD"),
        Recipient =
        [
            new DirectRechargeRecipientInput { Key = "recipient_number", Value = "01012345678" },
            new DirectRechargeRecipientInput { Key = "recipient_name", Value = "Ahmed Ali" },
        ],
        Note = "Family support",
        ExternalReference = "POS-77-TR-551",
        ContactPhone = "0912345678",
    };

    [Theory]
    [InlineData("6")]
    [InlineData("2")]
    [InlineData("8")]
    [InlineData("paymentFailed, inProgress")]
    [InlineData("completed, paymentFailed")]
    [InlineData("Completed")]
    [InlineData(" completed ")]
    public async Task A_non_wire_status_never_becomes_a_final_direct_recharge_outcome(string status)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Body = ProductBodies.DirectRecharge(status);

        var read = await fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken);
        var result = await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        Assert.Equal(DirectRechargeStatus.Unknown, read.Status);
        Assert.IsType<DirectRechargeOutcomeUnknown>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_direct_recharge_read_accepts_optional_recipient_names(bool explicitNull)
    {
        using var fixture = new PipelineFixture();
        var body = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.DirectRecharge("inProgress"))!;
        foreach (var recipient in body["recipient"]!.AsArray())
        {
            if (explicitNull) recipient!["name"] = null;
            else recipient!.AsObject().Remove("name");
        }
        fixture.Stub.Body = body.ToJsonString();

        var read = await fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken);
        Assert.All(read.Recipient, recipient => Assert.Null(recipient.Name));
        Assert.Equal("01012345678", read.Recipient[0].Value);
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();
        var updated = await fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken);
        Assert.Equal(Operation, Assert.IsType<DirectRechargeAccepted>(updated.Accepted).OperationId);
        Assert.Equal($"/v1/direct-recharges/{Operation:D}", Assert.IsType<Uri>(updated.Location).OriginalString);
    }

    [Fact]
    public async Task Reads_reject_invalid_recipient_values_and_patch_rejects_a_direct_recharge_body()
    {
        using var fixture = new PipelineFixture();
        var body = System.Text.Json.Nodes.JsonNode.Parse(ProductBodies.DirectRecharge("inProgress"))!;
        body["recipient"]![0]!["value"] = null;
        fixture.Stub.Body = body.ToJsonString();
        await Assert.ThrowsAsync<JsonException>(() => fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken));
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        await Assert.ThrowsAsync<JsonException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken));
        using var webhook = new WebhookFixture();
        var failure = await Assert.ThrowsAsync<Verification.UnverifiableWebhookException>(async () =>
            await webhook.Verifier.VerifyAndParseAsync(webhook.Create("directRecharge.suspended", body), TestContext.Current.CancellationToken));
        Assert.Equal(Verification.WebhookVerificationFailure.EnvelopeInvalid, failure.Failure);
    }

    [Fact]
    public async Task A_product_page_reads_the_payout_currency_name_and_leaves_absent_limits_absent()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = ProductBodies.Fill("""
            {"items":[
            {"id":"@product@","title":"فودافون كاش مصر","description":"تحويل","type":"external",
            "category":{"id":"4b9e0000-0000-4000-8000-000000000001","name":"مصر","icon":"https://cdn.anis.ly/products/eg.png"},
            "payoutCurrency":{"name":"جنيه مصري"},"available":true,
            "limits":{"minimumTotal":{"amount":"10.000","currency":"LYD"},"maximumTotal":{"amount":"5000.000","currency":"LYD"},"minimumPayout":"50.000","maximumPayout":"30000.000"}},
            {"id":"e7c2a9d4-1b3f-4a85-9c60-8d2e4f7b1a94","title":"خدمة","type":"billing","payoutCurrency":{"name":"دينار"},"available":false}],
            "nextCursor":null}
            """);

        var page = await fixture.Client.DirectRechargeProducts.GetDirectRechargeProductsPageAsync(
            Wallet, new DirectRechargeProductsQuery { Type = DirectRechargeProductType.External }, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal("جنيه مصري", page.Items[0].PayoutCurrency.Name);
        Assert.Equal("50.000", page.Items[0].Limits!.MinimumPayout);
        Assert.False(page.Items[1].Available);
        Assert.Null(page.Items[1].Limits);
        Assert.Equal(DirectRechargeProductType.Billing, page.Items[1].Type);
        Assert.Equal("?type=external", fixture.Stub.LastRequest!.RequestUri!.Query);
        Assert.Equal($"/v1/wallets/{Wallet:D}/direct-recharge-products", fixture.Stub.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task A_product_detail_carries_the_dynamic_recipient_fields_including_the_wire_for()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = ProductBodies.Fill("""
            {"id":"@product@","title":"فودافون كاش مصر","type":"external","payoutCurrency":{"name":"جنيه مصري"},"available":true,
            "recipientFields":[
            {"key":"recipient_number","name":{"ar":"رقم المحفظة","en":"Wallet number"},"kind":"text","for":"recipient","order":1,
            "validation":"^01[0-9]{9}$","validationMessage":{"ar":"رقم غير صحيح","en":"Invalid number"}},
            {"key":"recipient_location","name":{"ar":"المدينة","en":"City"},"kind":"list","for":"recipient","order":3},
            {"key":"Sender","name":{"ar":"المرسل","en":"Sender"},"kind":"note","for":"sender","order":4}]}
            """);

        var product = await fixture.Client.DirectRechargeProducts.GetAsync(Wallet, Product, TestContext.Current.CancellationToken);

        Assert.Equal([DirectRechargeFieldKind.Text, DirectRechargeFieldKind.List, DirectRechargeFieldKind.Note], product.RecipientFields.Select(field => field.Kind));
        Assert.Equal([DirectRechargeFieldFor.Recipient, DirectRechargeFieldFor.Recipient, DirectRechargeFieldFor.Sender], product.RecipientFields.Select(field => field.For));
        Assert.Equal("^01[0-9]{9}$", product.RecipientFields[0].Validation);
        Assert.Equal("Invalid number", product.RecipientFields[0].ValidationMessage!.En);
        Assert.Null(product.RecipientFields[1].Validation);
        Assert.Equal($"/v1/wallets/{Wallet:D}/direct-recharge-products/{Product:D}", fixture.Stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Locations_encode_the_search_once_and_follow_the_cursor()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"1a2b0000-0000-4000-8000-000000000001","name":"القاهرة"}],"nextCursor":"c+1/2="}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"3c4d0000-0000-4000-8000-000000000002","name":"الإسكندرية"}],"nextCursor":null}""");

        var names = new List<string>();

        await foreach (var location in fixture.Client.DirectRechargeProducts.GetDirectRechargeProductLocationsAsync(
            Wallet, Product, new DirectRechargeLocationsQuery { Search = "طرابلس" }, TestContext.Current.CancellationToken))
            names.Add(location.Name);

        Assert.Equal(["القاهرة", "الإسكندرية"], names);
        Assert.Equal($"?search={Uri.EscapeDataString("طرابلس")}", fixture.Stub.Requests[0].RequestUri!.Query);
        Assert.Equal($"?search={Uri.EscapeDataString("طرابلس")}&cursor=c%2B1%2F2%3D", fixture.Stub.Requests[1].RequestUri!.Query);
        Assert.Contains("/locations", fixture.Stub.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DirectRechargeAmountIn.Lyd, "lyd")]
    [InlineData(DirectRechargeAmountIn.Fx, "fx")]
    public async Task A_direct_recharge_quote_keeps_the_payout_currency_name(DirectRechargeAmountIn side, string wire)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = ProductBodies.Fill("""
            {"productId":"@product@","payout":{"amount":"1500.000","currency":{"name":"جنيه مصري"}},"exchangeRate":"0.155000",
            "price":{"amount":"232.500","currency":"LYD"},"commission":{"amount":"5.000","currency":"LYD"},"total":{"amount":"237.500","currency":"LYD"}}
            """);

        var quote = await fixture.Client.DirectRechargeProducts.QuoteAsync(Wallet, Product, "1500.000", side, TestContext.Current.CancellationToken);

        Assert.Equal("جنيه مصري", quote.Payout.Currency.Name);
        Assert.Equal("1500.000", quote.Payout.Amount);
        Assert.Equal("0.155000", quote.ExchangeRate);
        Assert.Equal(237.500m, quote.Total.Amount);
        Assert.Equal($"?amount=1500.000&amountIn={wire}", fixture.Stub.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task A_quote_side_the_wire_does_not_know_never_leaves_the_process()
    {
        using var fixture = new PipelineFixture();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Client.DirectRechargeProducts.QuoteAsync(Wallet, Product, "1.000", (DirectRechargeAmountIn)99, TestContext.Current.CancellationToken));

        Assert.Null(fixture.Stub.LastRequest);
    }

    [Fact]
    public async Task A_direct_recharge_create_sends_the_recipient_list_and_no_owner_pricing()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();

        var result = await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        var processing = Assert.IsType<DirectRechargeProcessing>(result);
        var request = fixture.Stub.LastRequest!;
        var body = fixture.Stub.LastRequestBody!;
        using var sent = JsonDocument.Parse(body);

        Assert.Equal(Operation, processing.OperationId);
        Assert.Equal("directRecharge", processing.Accepted!.OperationType);
        Assert.Equal(Operation.ToString("D"), request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(["contactPhone", "expectedTotal", "externalReference", "note", "payoutAmount", "productId", "recipient"],
            sent.RootElement.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal("0912345678", sent.RootElement.GetProperty("contactPhone").GetString());
        Assert.Equal(2, sent.RootElement.GetProperty("recipient").GetArrayLength());
        Assert.DoesNotContain("price", System.Text.Encoding.UTF8.GetString(body), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("whatsapp", System.Text.Encoding.UTF8.GetString(body), StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:", request.Content!.Headers.GetValues("Content-Digest").Single());
    }

    [Fact]
    public async Task A_recipient_value_never_reaches_a_log_through_formatting()
    {
        var input = new DirectRechargeRecipientInput { Key = "recipient_number", Value = "01012345678" };

        Assert.DoesNotContain("0101", input.ToString(), StringComparison.Ordinal);

        using var fixture = new PipelineFixture();

        fixture.Stub.NoStore = true;
        fixture.Stub.Body = ProductBodies.DirectRecharge("completed");

        var read = await fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("0101", read.Recipient[0].ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("awaitingPayment", DirectRechargeStatus.AwaitingPayment)]
    [InlineData("paymentFailed", DirectRechargeStatus.PaymentFailed)]
    [InlineData("pending", DirectRechargeStatus.Pending)]
    [InlineData("inProgress", DirectRechargeStatus.InProgress)]
    [InlineData("suspendedDataError", DirectRechargeStatus.SuspendedDataError)]
    [InlineData("completed", DirectRechargeStatus.Completed)]
    [InlineData("rejectedAwaitingRefund", DirectRechargeStatus.RejectedAwaitingRefund)]
    [InlineData("rejectedWithRefund", DirectRechargeStatus.RejectedWithRefund)]
    public async Task All_eight_statuses_are_known_and_only_the_closed_ones_are_final(string wire, DirectRechargeStatus expected)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.DirectRecharge(wire);

        var result = await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        var closed = expected is DirectRechargeStatus.PaymentFailed or DirectRechargeStatus.Completed or DirectRechargeStatus.RejectedWithRefund;

        if (closed)
            Assert.Equal(expected, Assert.IsType<DirectRechargeFinal>(result).Recharge.Status);
        else
            Assert.Equal(expected, Assert.IsType<DirectRechargeProcessing>(result).Recharge!.Status);
    }

    [Fact]
    public async Task A_partial_refund_alone_is_not_completed_and_round_trips()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = ProductBodies.DirectRecharge("inProgress", partialRefund: true);

        var result = await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        var processing = Assert.IsType<DirectRechargeProcessing>(result);
        var refund = processing.Recharge!.PartialRefund!;

        Assert.Equal("500.000", refund.RefundedPayout);
        Assert.Equal(77.500m, refund.RefundedTotal.Amount);
        Assert.Equal("1000.000", refund.RemainingPayout);
        Assert.Equal(160.000m, refund.RemainingTotal.Amount);

        var json = JsonSerializer.Serialize(refund, AnisJson.Options);

        Assert.Contains("\"refundedPayout\":\"500.000\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_completed_read_keeps_the_proof_the_exchange_rate_and_the_recipient_names()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.NoStore = true;
        fixture.Stub.Body = ProductBodies.DirectRecharge("completed");

        var read = await fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken);

        Assert.Equal("0.155000", read.ExchangeRate);
        Assert.Equal("جنيه مصري", read.Payout.Currency.Name);
        Assert.Equal("https://example.ly/proof", read.ProofUrl);
        Assert.Equal("القاهرة", read.Recipient[1].ValueName);
        Assert.Equal(30412, read.Number);
        Assert.Null(read.Reason);
        Assert.NotNull(read.CompletedAt);
    }

    [Fact]
    public async Task A_recipient_update_has_a_nonce_but_no_idempotency_key()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();

        var read = await fixture.Client.DirectRecharges.UpdateRecipientAsync(
            Operation,
            new UpdateDirectRechargeRecipientRequest
            {
                Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "01098765432" }],
            },
            TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;
        var body = fixture.Stub.LastRequestBody!;

        Assert.IsType<DirectRechargeProcessing>(read);
        var accepted = Assert.IsType<DirectRechargeAccepted>(read.Accepted);
        Assert.Equal(Operation, read.OperationId);
        Assert.Null(read.Recharge);
        Assert.Equal(TimeSpan.FromSeconds(5), read.RetryAfter);
        Assert.Equal(Operation, accepted.OperationId);
        Assert.Equal("directRecharge", accepted.OperationType);
        Assert.Equal("processing", accepted.Status);
        Assert.Equal($"/v1/direct-recharges/{Operation:D}", Assert.IsType<Uri>(read.Location).OriginalString);
        using var acceptedBody = JsonDocument.Parse(JsonSerializer.Serialize(read.Accepted, AnisJson.Options));
        Assert.Equal(["operationId", "operationType", "status"], acceptedBody.RootElement.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Single(fixture.Stub.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"/v1/direct-recharges/{Operation:D}/recipient", request.RequestUri!.AbsolutePath);
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:", request.Content!.Headers.GetValues("Content-Digest").Single());
        Assert.Contains("\"content-digest\"", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
        Assert.Equal("""{"recipient":[{"key":"recipient_number","value":"01098765432"}]}""", System.Text.Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Recipient_corrections_reuse_the_create_processing_result_and_acknowledgement()
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();
        var created = Assert.IsType<DirectRechargeProcessing>(await fixture.Client.DirectRecharges.CreateAsync(
            Wallet, Operation, Recharge, TestContext.Current.CancellationToken));
        var corrected = await fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken);

        Assert.IsType<DirectRechargeProcessing>(corrected);
        Assert.Equal(Assert.IsType<DirectRechargeAccepted>(created.Accepted), Assert.IsType<DirectRechargeAccepted>(corrected.Accepted));
        Assert.Equal(created.Location, corrected.Location);
        Assert.Null(corrected.Recharge);
        Assert.Equal(2, fixture.Stub.Requests.Count);
    }

    [Fact]
    public async Task A_recipient_acknowledgement_without_a_location_is_rejected()
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();
        var failure = await Assert.ThrowsAsync<JsonException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken));
        Assert.Contains("Location header", failure.Message, StringComparison.Ordinal);
        Assert.Single(fixture.Stub.Requests);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, false)]
    [InlineData(204, false)]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    public async Task A_recipient_body_under_a_wrong_success_status_is_rejected(int status, bool acknowledgement)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = acknowledgement ? ProductBodies.DirectRechargeAccepted() : ProductBodies.DirectRecharge("inProgress");
        var failure = await Assert.ThrowsAsync<JsonException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken));
        Assert.Contains("published success status", failure.Message, StringComparison.Ordinal);
        Assert.Single(fixture.Stub.Requests);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"operationId":null,"operationType":"directRecharge","status":"processing"}""")]
    [InlineData("""{"operationId":"00000000-0000-0000-0000-000000000000","operationType":"directRecharge","status":"processing"}""")]
    [InlineData("""{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","operationType":"directRecharge","status":"processing"}""")]
    [InlineData("""{"operationId":"7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81","operationType":"meterCharge","status":"processing"}""")]
    [InlineData("""{"operationId":"7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81","operationType":"directRecharge","status":"completed"}""")]
    [InlineData("""{"operationId":"7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81","status":"processing"}""")]
    [InlineData("""{"operationId":"7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81","operationType":"directRecharge","status":"processing","extra":1}""")]
    public async Task A_recipient_acknowledgement_must_match_the_exact_published_body(string body)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = body;
        await Assert.ThrowsAsync<JsonException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken));
        Assert.Single(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_recipient_refusal_keeps_the_field_errors_and_is_not_retried()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.UnprocessableEntity;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":422,"code":"validation_failed","extensions":{"fields":{"recipient_number":"رقم غير صحيح"},"future":1}}""";

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(
            Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "x" }] },
            TestContext.Current.CancellationToken));

        Assert.Equal("رقم غير صحيح", failure.Problem.Extensions!.Value.GetProperty("fields").GetProperty("recipient_number").GetString());
        Assert.Equal(1, failure.Problem.Extensions.Value.GetProperty("future").GetInt32());
        Assert.Single(fixture.Stub.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"fields\":null}")]
    [InlineData("{\"fields\":[]}")]
    [InlineData("{\"fields\":\"bad\"}")]
    [InlineData("{\"fields\":{\"recipient_number\":42,\"recipient_name\":[\"bad\"]}}")]
    public async Task Missing_or_malformed_field_error_shapes_preserve_the_typed_refusal(string? extensions)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = HttpStatusCode.UnprocessableEntity;
        fixture.Stub.Body = "{\"type\":\"about:blank\",\"status\":422,\"code\":\"validation_failed\""
            + (extensions is null ? string.Empty : ",\"extensions\":" + extensions) + "}";
        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(
            Operation, new UpdateDirectRechargeRecipientRequest { Recipient = Recharge.Recipient }, TestContext.Current.CancellationToken));
        Assert.Equal(PartnerErrorCode.ValidationFailed, failure.Code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failure.Status);
        Assert.Equal(extensions is null or "null" ? null : extensions, failure.Problem.Extensions?.GetRawText());
        Assert.Single(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_recipient_change_on_a_running_direct_recharge_is_a_typed_conflict()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"direct_recharge_not_suspended"}""";

        var failure = await Assert.ThrowsAnyAsync<AnisApiException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(
            Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "x" }] },
            TestContext.Current.CancellationToken));

        Assert.Equal(PartnerErrorCode.DirectRechargeNotSuspended, failure.Code);
    }

    [Fact]
    public async Task A_202_on_the_read_raises_the_processing_acknowledgment()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.RetryAfterSeconds = 9;
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();

        var processing = await Assert.ThrowsAsync<DirectRechargeProcessingException>(
            () => fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(9), processing.RetryAfter);
        Assert.Equal(Operation, processing.Accepted.OperationId);
    }

    [Theory]
    [InlineData("direct_recharge_product_unavailable", 409)]
    [InlineData("amount_out_of_range", 422)]
    [InlineData("price_changed", 409)]
    [InlineData("purchase_not_allowed", 403)]
    [InlineData("validation_failed", 422)]
    public async Task A_definitive_business_refusal_on_create_is_not_placed(string code, int status)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var result = await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        Assert.Equal(code, Assert.IsType<DirectRechargeNotPlaced>(result).Refusal.RawCode);
    }

    [Fact]
    public async Task AlreadyExistsForeignId_After60Seconds_IsIdempotencyConflict_and_stays_unknown()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();

        Assert.IsType<DirectRechargeProcessing>(
            await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken));

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"idempotency_conflict"}""";

        var late = await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        Assert.Equal(PartnerErrorCode.IdempotencyConflict, Assert.IsAssignableFrom<AnisApiException>(Assert.IsType<DirectRechargeOutcomeUnknown>(late).Cause).Code);
        Assert.Equal(fixture.Stub.RequestBodies[0], fixture.Stub.RequestBodies[1]);
        Assert.All(fixture.Stub.Requests, request => Assert.Equal(Operation.ToString("D"), request.Headers.GetValues("Idempotency-Key").Single()));
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
            ? await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken)
            : await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<DirectRechargeOutcomeUnknown>(result);

        Assert.Equal(Operation, unknown.OperationId);
        Assert.Equal(PartnerErrorCode.IdempotencyConflict, Assert.IsAssignableFrom<AnisApiException>(unknown.Cause).Code);
    }

    [Theory]
    [InlineData(409, "insufficient_balance")]
    [InlineData(409, "price_changed")]
    [InlineData(409, "direct_recharge_product_unavailable")]
    [InlineData(409, "idempotency_conflict")]
    [InlineData(422, "amount_out_of_range")]
    [InlineData(422, "validation_failed")]
    public async Task Reading_an_operation_whose_create_was_refused_raises_the_recorded_refusal(int status, string code)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.IdempotencyReplayed = true;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var refusal = await Assert.ThrowsAnyAsync<AnisApiException>(
            () => fixture.Client.DirectRecharges.GetAsync(Operation, TestContext.Current.CancellationToken));

        Assert.Equal(code, refusal.RawCode);
        Assert.Equal((HttpStatusCode)status, refusal.Status);
        Assert.True(refusal.IsReplayed);
        Assert.Single(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_fresh_refusal_on_resume_and_a_lost_answer_stay_unknown_with_the_original_id()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Conflict;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":409,"code":"direct_recharge_product_unavailable"}""";

        Assert.Equal(Operation, Assert.IsType<DirectRechargeOutcomeUnknown>(
            await fixture.Client.DirectRecharges.ResumeAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken)).OperationId);

        fixture.Stub.Failure = () => new TaskCanceledException("timeout", new TimeoutException());

        Assert.Equal(Operation, Assert.IsType<DirectRechargeOutcomeUnknown>(
            await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken)).OperationId);
    }

    [Fact]
    public async Task An_empty_body_and_an_answer_for_another_id_stay_unknown()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = string.Empty;

        Assert.IsType<DirectRechargeOutcomeUnknown>(
            await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken));

        fixture.Stub.Body = ProductBodies.DirectRecharge("completed", id: Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34"));

        Assert.IsType<DirectRechargeOutcomeUnknown>(
            await fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_list_keeps_every_filter_and_the_boolean_partial_refund_flag()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Bodies.Enqueue(ProductBodies.Fill("""
            {"items":[{"operationId":"@id@","status":"completed","number":30412,"product":{"id":"@product@","title":"فودافون كاش مصر"},
            "payout":{"amount":"1500.000","currency":{"name":"جنيه مصري"}},"total":{"amount":"237.500","currency":"LYD"},
            "externalReference":"POS-77-TR-551","requestedAt":"2026-10-20T09:00:00Z","completedAt":"2026-10-20T09:14:00Z","hasPartialRefund":false}],
            "nextCursor":"next"}
            """, ("id", Operation.ToString("D"))));
        fixture.Stub.Bodies.Enqueue("""{"items":[],"nextCursor":null}""");

        var query = new DirectRechargesQuery { Status = DirectRechargeStatus.Completed, ProductId = Product, PageSize = 100 };
        var summaries = new List<DirectRechargeSummary>();

        await foreach (var summary in fixture.Client.DirectRecharges.GetDirectRechargesAsync(Wallet, query, TestContext.Current.CancellationToken))
            summaries.Add(summary);

        var single = Assert.Single(summaries);

        Assert.False(single.HasPartialRefund);
        Assert.Equal(DirectRechargeStatus.Completed, single.Status);
        Assert.Equal($"?status=completed&productId={Product:D}&pageSize=100", fixture.Stub.Requests[0].RequestUri!.Query);
        Assert.Equal($"?status=completed&productId={Product:D}&cursor=next&pageSize=100", fixture.Stub.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task An_empty_list_page_ends_the_iteration()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = """{"items":[],"nextCursor":null}""";

        var page = await fixture.Client.DirectRecharges.GetDirectRechargesPageAsync(Wallet, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(string.Empty, fixture.Stub.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task A_recipient_answer_without_the_required_signature_is_discarded()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Location = $"/v1/direct-recharges/{Operation:D}";
        fixture.Stub.Body = ProductBodies.DirectRechargeAccepted();

        await Assert.ThrowsAsync<Verification.UnverifiableResponseException>(() => fixture.Client.DirectRecharges.UpdateRecipientAsync(
            Operation,
            new UpdateDirectRechargeRecipientRequest { Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "x" }] },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_call_the_caller_cancelled_is_rethrown_and_an_empty_id_never_leaves_the_process()
    {
        using var fixture = new PipelineFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.DirectRecharges.CreateAsync(Wallet, Operation, Recharge, cancelled.Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Client.DirectRecharges.CreateAsync(Wallet, Guid.Empty, Recharge, TestContext.Current.CancellationToken));
    }
}
