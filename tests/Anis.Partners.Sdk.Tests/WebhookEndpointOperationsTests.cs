using System.Net;
using System.Security.Cryptography;
using System.Text;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Setting and testing the notification endpoint: signed changes with a nonce and no idempotency key.</summary>
public sealed class WebhookEndpointOperationsTests
{
    // The SHA-256 of the two bytes {}.
    private const string EmptyObjectDigest = "sha-256=:RBNvo1WzZ4oRRq0W9+hknpT7T8If536DEMBg9hyq/4o=:";

    [Theory]
    [InlineData("""{"url":null,"status":"notConfigured","generation":null}""", WebhookEndpointStatus.NotConfigured, null, null)]
    [InlineData("""{"url":"https://pos.example.ly/hooks/anis","status":"pendingVerification","generation":3}""", WebhookEndpointStatus.PendingVerification, "https://pos.example.ly/hooks/anis", 3L)]
    [InlineData("""{"url":"https://pos.example.ly/hooks/anis","status":"verified","generation":3}""", WebhookEndpointStatus.Verified, "https://pos.example.ly/hooks/anis", 3L)]
    [InlineData("""{"url":"https://pos.example.ly/hooks/anis","status":"disabled","generation":4}""", WebhookEndpointStatus.Disabled, "https://pos.example.ly/hooks/anis", 4L)]
    [InlineData("""{"url":"https://pos.example.ly/hooks/anis","status":"a_state_from_the_future","generation":4}""", WebhookEndpointStatus.Unknown, "https://pos.example.ly/hooks/anis", 4L)]
    public async Task A_read_returns_each_registration_state_with_its_three_members(string body, WebhookEndpointStatus status, string? url, long? generation)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = body;

        var endpoint = await fixture.Client.WebhookEndpoint.GetAsync(TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;

        Assert.Equal(status, endpoint.Status);
        Assert.Equal(url, endpoint.Url);
        Assert.Equal(generation, endpoint.Generation);
        Assert.Equal("/v1/webhook-endpoint", request.RequestUri!.AbsolutePath);
        Assert.True(request.Headers.Contains("Signature"));
        Assert.False(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task An_absent_url_and_generation_are_written_as_explicit_nulls()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = """{"url":null,"status":"notConfigured","generation":null}""";

        var endpoint = await fixture.Client.WebhookEndpoint.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("""{"url":null,"status":"notConfigured","generation":null}""",
            System.Text.Json.JsonSerializer.Serialize(endpoint, AnisJson.Options));
    }

    [Fact]
    public async Task An_endpoint_answer_missing_a_member_is_rejected()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = """{"status":"verified"}""";

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Client.WebhookEndpoint.GetAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Setting_the_endpoint_sends_exactly_the_url_with_a_nonce_and_no_idempotency_key()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Body = """{"url":"https://pos.example.ly/hooks/anis","status":"pendingVerification","generation":1}""";

        var endpoint = await fixture.Client.WebhookEndpoint.SetAsync(
            new SetWebhookEndpointRequest { Url = "https://pos.example.ly/hooks/anis" }, TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;
        var body = fixture.Stub.LastRequestBody!;

        Assert.Equal(WebhookEndpointStatus.PendingVerification, endpoint.Status);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("""{"url":"https://pos.example.ly/hooks/anis"}""", Encoding.UTF8.GetString(body));
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:", request.Content!.Headers.GetValues("Content-Digest").Single());
        Assert.EndsWith($";nonce=\"{request.Headers.GetValues("Nonce").Single()}\"", request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("webhook_endpoint_invalid", 422)]
    [InlineData("webhook_endpoint_unverified", 409)]
    public async Task A_signed_endpoint_refusal_is_a_typed_error_not_a_purchase_outcome(string code, int status)
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = (HttpStatusCode)status;
        fixture.Stub.Body = $$"""{"type":"about:blank","title":"t","status":{{status}},"code":"{{code}}"}""";

        var failure = await Assert.ThrowsAsync<AnisApiException>(
            () => fixture.Client.WebhookEndpoint.TestAsync(TestContext.Current.CancellationToken));

        Assert.Equal(code, failure.RawCode);
        Assert.NotEqual(PartnerErrorCode.Unknown, failure.Code);
    }

    [Fact]
    public async Task A_test_signs_exactly_an_empty_object_and_reads_the_queued_delivery()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = """{"id":"11111111-1111-4111-8111-111111111111","status":"pending","queuedAt":"2026-10-20T08:00:00Z","type":"webhook_endpoint.test"}""";

        var accepted = await fixture.Client.WebhookEndpoint.TestAsync(TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;

        Assert.Equal("{}", Encoding.UTF8.GetString(fixture.Stub.LastRequestBody!));
        Assert.Equal(EmptyObjectDigest, request.Content!.Headers.GetValues("Content-Digest").Single());
        Assert.Equal("/v1/webhook-endpoint/test", request.RequestUri!.AbsolutePath);
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.Equal(WebhookDeliveryStatus.Pending, accepted.Status);
        Assert.Equal(WebhookEventType.EndpointTest, accepted.Type);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), accepted.QueuedAt);
    }
}
