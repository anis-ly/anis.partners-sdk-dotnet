using System.Net;
using System.Security.Cryptography;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

public sealed class ProductSecurityTests
{
    [Theory]
    [InlineData("meter-create")]
    [InlineData("meter-resume")]
    [InlineData("recharge-create")]
    [InlineData("recharge-resume")]
    [InlineData("recipient")]
    [InlineData("endpoint")]
    [InlineData("test")]
    [InlineData("redeliver")]
    public async Task Every_product_mutation_attempt_has_a_fresh_bound_nonce_and_the_same_intent(string resource)
    {
        var nonces = new ChangingNonces();
        using var fixture = new PipelineFixture(nonces: nonces);
        var ct = TestContext.Current.CancellationToken;
        var meter = new CreateMeterChargeRequest { PackageId = ProductBodies.Package, MeterNumber = "0268000710262", ExpectedTotal = new Money(97.500m, "LYD") };
        var recharge = new CreateDirectRechargeRequest
        {
            ProductId = ProductBodies.Product, PayoutAmount = "1500.000", ExpectedTotal = new Money(237.500m, "LYD"),
            Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "01012345678" }],
        };
        fixture.Stub.Body = resource switch
        {
            "meter-create" or "meter-resume" => ProductBodies.MeterSucceeded(),
            "recharge-create" or "recharge-resume" => ProductBodies.DirectRecharge("inProgress"),
            "recipient" => ProductBodies.DirectRechargeAccepted(),
            "endpoint" => """{"url":"https://pos.example.ly/hooks/anis","status":"verified","generation":1}""",
            _ => $$"""{"id":"{{ProductBodies.DirectRechargeId:D}}","status":"pending","queuedAt":"2026-10-20T08:30:00Z"}""",
        };
        if (resource is "test" or "redeliver" or "recipient") fixture.Stub.Status = HttpStatusCode.Accepted;
        if (resource == "recipient") fixture.Stub.Location = $"/v1/direct-recharges/{ProductBodies.DirectRechargeId:D}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            switch (resource)
            {
                case "meter-create": await fixture.Client.MeterCharges.CreateAsync(ProductBodies.Wallet, ProductBodies.MeterCharge, meter, ct); break;
                case "meter-resume": await fixture.Client.MeterCharges.ResumeAsync(ProductBodies.Wallet, ProductBodies.MeterCharge, meter, ct); break;
                case "recharge-create": await fixture.Client.DirectRecharges.CreateAsync(ProductBodies.Wallet, ProductBodies.DirectRechargeId, recharge, ct); break;
                case "recharge-resume": await fixture.Client.DirectRecharges.ResumeAsync(ProductBodies.Wallet, ProductBodies.DirectRechargeId, recharge, ct); break;
                case "recipient": await fixture.Client.DirectRecharges.UpdateRecipientAsync(ProductBodies.DirectRechargeId, new UpdateDirectRechargeRecipientRequest { Recipient = recharge.Recipient }, ct); break;
                case "endpoint": await fixture.Client.WebhookEndpoint.SetAsync(new SetWebhookEndpointRequest { Url = "https://pos.example.ly/hooks/anis" }, ct); break;
                case "test": await fixture.Client.WebhookEndpoint.TestAsync(ct); break;
                case "redeliver": await fixture.Client.WebhookEvents.RedeliverAsync(ProductBodies.DirectRechargeId, ct); break;
            }
        }
        Assert.Equal(2, nonces.Calls);
        Assert.Equal(2, fixture.Stub.Requests.Count);
        Assert.NotEqual(fixture.Stub.Requests[0].Headers.GetValues("Nonce").Single(), fixture.Stub.Requests[1].Headers.GetValues("Nonce").Single());
        Assert.Equal(fixture.Stub.RequestBodies[0], fixture.Stub.RequestBodies[1]);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = fixture.Stub.Requests[attempt];
            Assert.EndsWith($";nonce=\"{request.Headers.GetValues("Nonce").Single()}\"", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
            Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(fixture.Stub.RequestBodies[attempt]!))}:", request.Content!.Headers.GetValues("Content-Digest").Single());
            Assert.Equal(resource.EndsWith("create", StringComparison.Ordinal) || resource.EndsWith("resume", StringComparison.Ordinal), request.Headers.Contains("Idempotency-Key"));
        }
    }

    [Theory]
    [InlineData("packages")]
    [InlineData("meter-charges")]
    [InlineData("products")]
    [InlineData("product")]
    [InlineData("locations")]
    [InlineData("quote")]
    [InlineData("recharges")]
    [InlineData("endpoint")]
    [InlineData("events")]
    public async Task Unsigned_product_reads_and_refusals_never_fetch_verification_keys(string resource)
    {
        var keys = new CountingKeys();
        using var fixture = new PipelineFixture(keys: keys);
        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = resource switch
        {
            "product" => ProductBodies.Fill("""{"id":"@product@","title":"Product","type":"external","payoutCurrency":{"name":"Pound"},"available":true,"recipientFields":[]}"""),
            "quote" => ProductBodies.Fill("""{"productId":"@product@","payout":{"amount":"1500.000","currency":{"name":"Pound"}},"exchangeRate":"0.155000","price":{"amount":"232.500","currency":"LYD"},"commission":{"amount":"5.000","currency":"LYD"},"total":{"amount":"237.500","currency":"LYD"}}"""),
            "endpoint" => """{"url":null,"status":"notConfigured","generation":null}""",
            _ => """{"items":[],"nextCursor":null}""",
        };
        await ReadAsync(fixture, resource);
        fixture.Stub.Status = HttpStatusCode.UnprocessableEntity;
        fixture.Stub.Body = """{"type":"about:blank","status":422,"code":"validation_failed"}""";
        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() => ReadAsync(fixture, resource));
        Assert.Equal(PartnerErrorCode.ValidationFailed, failure.Code);
        Assert.Equal(2, fixture.Stub.Requests.Count);
        Assert.All(fixture.Stub.Requests, request => Assert.True(request.Headers.Contains("Signature")));
        Assert.Equal(0, keys.Gets);
        Assert.Equal(0, keys.Refreshes);
    }

    private static async Task ReadAsync(PipelineFixture fixture, string resource)
    {
        var ct = TestContext.Current.CancellationToken;
        switch (resource)
        {
            case "packages": await fixture.Client.MeterCharges.GetMeterPackagesPageAsync(ProductBodies.Wallet, cancellationToken: ct); break;
            case "meter-charges": await fixture.Client.MeterCharges.GetMeterChargesPageAsync(ProductBodies.Wallet, cancellationToken: ct); break;
            case "products": await fixture.Client.DirectRechargeProducts.GetDirectRechargeProductsPageAsync(ProductBodies.Wallet, cancellationToken: ct); break;
            case "product": await fixture.Client.DirectRechargeProducts.GetAsync(ProductBodies.Wallet, ProductBodies.Product, ct); break;
            case "locations": await fixture.Client.DirectRechargeProducts.GetDirectRechargeProductLocationsPageAsync(ProductBodies.Wallet, ProductBodies.Product, cancellationToken: ct); break;
            case "quote": await fixture.Client.DirectRechargeProducts.QuoteAsync(ProductBodies.Wallet, ProductBodies.Product, "1500.000", DirectRechargeAmountIn.Fx, ct); break;
            case "recharges": await fixture.Client.DirectRecharges.GetDirectRechargesPageAsync(ProductBodies.Wallet, cancellationToken: ct); break;
            case "endpoint": await fixture.Client.WebhookEndpoint.GetAsync(ct); break;
            case "events": await fixture.Client.WebhookEvents.GetWebhookEventsPageAsync(cancellationToken: ct); break;
        }
    }

    private sealed class ChangingNonces : INonceFactory
    {
        public int Calls { get; private set; }
        public string Create() => (++Calls).ToString("D32", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class CountingKeys : ISigningKeySource
    {
        public int Gets { get; private set; }
        public int Refreshes { get; private set; }
        public ValueTask<PartnerSigningKeySet> GetAsync(CancellationToken cancellationToken)
        {
            Gets++;
            throw new InvalidOperationException("Unsigned reads must not fetch keys.");
        }
        public ValueTask<PartnerSigningKeySet> RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            throw new InvalidOperationException("Unsigned reads must not refresh keys.");
        }
    }
}
