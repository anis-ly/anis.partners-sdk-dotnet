using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Operations;
using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Which answers are verified: the route decides, never the answer.</summary>
/// <remarks>
/// Anis signs the answers that move money, deliver card codes or establish a key, plus the signature self-check —
/// each one, success and refusal. The information reads answer unsigned. Every request is still signed.
/// </remarks>
public sealed class ResponseSigningTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid Category = Guid.Parse("6e3b9d27-1f84-4a5c-b0d6-8c2e7f4a9b13");
    private static readonly Guid Subcategory = Guid.Parse("8f2a6c41-3d97-4e1b-a5c8-0b7d2e9f6a34");
    private static readonly Guid SoldCard = Guid.Parse("4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046");
    private static readonly Guid Invoice = Guid.Parse("c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51");
    private static readonly Guid Operation = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");

    private const string EmptyPage = """{"items":[],"nextCursor":null}""";

    private const string WalletBody = """{"id":"2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26","name":"Main","currency":"LYD","balance":{"amount":"1.000","currency":"LYD"}}""";

    // Every SDK call on an information route, with an answer its model reads.
    private static readonly Dictionary<string, (string Body, Func<IAnisPartnersClient, CancellationToken, Task> Call)> InformationCalls = new(StringComparer.Ordinal)
    {
        ["GET /v1/profile"] = (
            """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":["profile:read"]}}""",
            (anis, token) => anis.Profile.GetAsync(token)),
        ["GET /v1/wallets"] = (EmptyPage, (anis, token) => anis.Wallets.ListPageAsync(null, token)),
        ["GET /v1/wallets/{walletId}"] = (
            WalletBody,
            (anis, token) => anis.Wallets.GetAsync(Wallet, token)),
        ["GET /v1/wallets/{walletId}/catalog/categories"] = (EmptyPage, (anis, token) => anis.Catalogue.ListCategoriesPageAsync(Wallet, null, token)),
        ["GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories"] = (
            EmptyPage,
            (anis, token) => anis.Catalogue.ListSubcategoriesPageAsync(Wallet, Category, null, token)),
        ["GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}"] = (
            $$"""{"id":"{{Subcategory:D}}"}""",
            (anis, token) => anis.Catalogue.GetSubcategoryAsync(Wallet, Subcategory, token)),
        ["GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards"] = (
            EmptyPage,
            (anis, token) => anis.Catalogue.ListCardsPageAsync(Wallet, Subcategory, null, token)),
        ["GET /v1/wallets/{walletId}/cards"] = (EmptyPage, (anis, token) => anis.OwnedCards.ListPageAsync(Wallet, null, token)),
        ["GET /v1/wallets/{walletId}/cards/{soldCardId}"] = (
            $$"""{"id":"{{SoldCard:D}}","credentialAvailable":true}""",
            (anis, token) => anis.OwnedCards.GetAsync(Wallet, SoldCard, token)),
    };

    // Every signed-route call that throws on an answer it cannot verify. The order create is below: it returns an outcome instead.
    private static readonly Dictionary<string, Func<IAnisPartnersClient, CancellationToken, Task>> SignedCalls = new(StringComparer.Ordinal)
    {
        ["GET /v1/orders/{operationId}"] = (anis, token) => anis.Orders.GetAsync(Operation, token),
        ["POST /v1/wallets/{walletId}/cards/{soldCardId}/reveal"] = (anis, token) => anis.OwnedCards.RevealAsync(Wallet, SoldCard, token),
        ["POST /v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal"] = (anis, token) => anis.OwnedCards.RevealInvoiceAsync(Wallet, Invoice, token),
        ["POST /v1/diagnostics/signature"] = (anis, token) => anis.Diagnostics.CheckSignatureAsync(token),
    };

    public static TheoryData<string> InformationRoutes => new(InformationCalls.Keys.Order(StringComparer.Ordinal));

    public static TheoryData<string> SignedRoutes => new(SignedCalls.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(InformationRoutes))]
    public async Task An_information_routes_unsigned_answer_is_returned(string route)
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        var (body, call) = InformationCalls[route];

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = body;

        await call(fixture.Client, TestContext.Current.CancellationToken);

        AssertSent(fixture, route, signsResponse: false);

        // The request is signed exactly as before: only the answer changed.
        Assert.True(fixture.Stub.LastRequest!.Headers.Contains("Signature"));
        Assert.True(fixture.Stub.LastRequest.Headers.Contains("Signature-Input"));
        Assert.DoesNotContain(capture.Measurements, m => m.Instrument == "anis.partners.response.verification.failures");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "insufficient_scope", typeof(AuthorizationException))]
    [InlineData(HttpStatusCode.NotFound, "wallet_not_granted", typeof(ResourceNotFoundException))]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", typeof(RateLimitedException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", typeof(DependencyUnavailableException))]
    public async Task An_information_routes_unsigned_refusal_is_its_decided_exception(HttpStatusCode status, string code, Type expected)
    {
        using var capture = new TelemetryCapture();
        using var fixture = new PipelineFixture(capture.Loggers);

        fixture.Stub.Unsigned = true;
        fixture.Stub.Status = status;
        fixture.Stub.RetryAfterSeconds = 7;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{(int)status}},"code":"{{code}}","requestId":"01J9"}""";

        var failure = await Assert.ThrowsAnyAsync<AnisApiException>(
            () => fixture.Client.Wallets.GetAsync(Wallet, TestContext.Current.CancellationToken));

        Assert.IsType(expected, failure);
        Assert.Equal(code, failure.RawCode);
        Assert.Equal(status, failure.Status);
        Assert.Equal("01J9", failure.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(7), failure.RetryAfter);

        // Reported like any refusal: one warning carrying the code, never as a discarded answer.
        Assert.Contains(capture.LogLines, line => line.StartsWith("Warning 1002", StringComparison.Ordinal) && line.Contains(code, StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Measurements, m => m.Instrument == "anis.partners.response.verification.failures");
    }

    [Fact]
    public async Task A_signature_on_an_information_route_is_not_checked()
    {
        using var fixture = new PipelineFixture();

        // Far outside the freshness window: verified, this would be discarded as CreatedOutOfWindow.
        fixture.Stub.SignedAt = PipelineFixture.Now.AddHours(-2);
        fixture.Stub.Body = WalletBody;

        var wallet = await fixture.Client.Wallets.GetAsync(Wallet, TestContext.Current.CancellationToken);

        Assert.Equal(Wallet, wallet.Id);
    }

    [Theory]
    [MemberData(nameof(SignedRoutes))]
    public async Task A_signed_routes_answer_without_a_signature_is_discarded(string route)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.NoStore = true;
        fixture.Stub.Body = "{}";

        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            () => SignedCalls[route](fixture.Client, TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.SignatureMissing, failure.Failure);
        AssertSent(fixture, route, signsResponse: true);
    }

    [Fact]
    public async Task A_signed_routes_refusal_without_a_signature_is_discarded_not_mapped()
    {
        using var fixture = new PipelineFixture();

        // Every refusal on a signed route is signed too, so an unsigned one is not Anis's refusal to act on.
        fixture.Stub.Unsigned = true;
        fixture.Stub.NoStore = true;
        fixture.Stub.Status = HttpStatusCode.Forbidden;
        fixture.Stub.Body = """{"type":"about:blank","title":"t","status":403,"code":"reveal_not_allowed","requestId":"01J9"}""";

        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            () => fixture.Client.OwnedCards.RevealAsync(Wallet, SoldCard, TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.SignatureMissing, failure.Failure);
    }

    [Fact]
    public async Task An_order_answer_without_a_signature_is_an_unknown_outcome()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.NoStore = true;
        fixture.Stub.Status = HttpStatusCode.Created;
        fixture.Stub.Body = $$"""{"operationId":"{{Operation:D}}","status":"completed"}""";

        var outcome = await fixture.Client.Orders.CreateAsync(
            Wallet,
            Operation,
            new CreateOrderRequest
            {
                CardId = Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"),
                Quantity = 1,
                ExpectedUnitPrice = new Money(10.500m, "LYD"),
                ExpectedTotal = new Money(10.500m, "LYD"),
            },
            TestContext.Current.CancellationToken);

        var unknown = Assert.IsType<OrderOutcomeUnknown>(outcome);
        Assert.Equal(ResponseVerificationFailure.SignatureMissing, Assert.IsType<UnverifiableResponseException>(unknown.Cause).Failure);
    }

    [Fact]
    public async Task A_client_that_only_reads_information_never_fetches_the_signing_keys()
    {
        using var responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var requestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = EcdsaP256Signer.FromEcdsa(requestKey, Guid.NewGuid());
        var stub = new SignedResponseStub(responseKey, "partner-response-signing/v1-active")
        {
            Unsigned = true,
            Body = InformationCalls["GET /v1/profile"].Body,
        };
        var wire = new RecordingWire(stub, responseKey, "partner-response-signing/v1-active");

        var anis = AnisPartnersClient.Create(
            new AnisPartnersClientOptions { Authority = new Uri("https://partners.example") },
            signer,
            wire);

        await anis.Profile.GetAsync(TestContext.Current.CancellationToken);

        // Nothing to verify, so nothing to verify with: a read keeps working when the key document cannot be reached.
        Assert.Single(wire.Requests);
        Assert.Equal(0, wire.KeyDocumentFetches);
    }

    [Fact]
    public void A_route_missing_from_the_table_is_refused_rather_than_guessed()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => PartnerRoutes.Find(HttpMethod.Get, "/v1/statements"));

        Assert.Contains("/v1/statements", failure.Message, StringComparison.Ordinal);
    }

    // The calls above are keyed by route. This holds each key to the request that actually left and to the route
    // table, so a theory cannot pass by exercising a different route under the name it claims.
    private static void AssertSent(PipelineFixture fixture, string route, bool signsResponse)
    {
        var space = route.IndexOf(' ', StringComparison.Ordinal);
        var method = route[..space];
        var template = route[(space + 1)..];
        var request = fixture.Stub.LastRequest!;

        // Each {parameter} of the template is one GUID in the path.
        var path = "^" + Regex.Replace(template, "\\{[^}]+\\}", "[0-9a-f-]{36}") + "$";

        Assert.Equal(method, request.Method.Method);
        Assert.Matches(path, request.RequestUri!.AbsolutePath);
        Assert.Equal(signsResponse, PartnerRoutes.Find(request.Method, template).SignsResponse);
    }
}
