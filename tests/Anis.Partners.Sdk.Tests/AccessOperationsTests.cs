using System.Net;
using System.Text;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Access stops carry a trimmed reason, a nonce and the digest of their JSON body.</summary>
public sealed class AccessOperationsTests
{
    private static readonly Guid Application = Guid.Parse("16fd2706-8baf-433b-82eb-8c7fada847da");
    private static readonly Guid Key = Guid.Parse("4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stop_uses_the_nonce_profile_with_a_reason_body(bool revoke)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Body = revoke
            ? $$"""{"keyId":"{{Key:D}}","status":"revoked","revokedAt":"2026-09-19T08:00:00Z"}"""
            : $$"""{"applicationId":"{{Application:D}}","status":"suspended","suspendedAt":"2026-09-19T08:00:00Z"}""";

        if (revoke)
        {
            var answer = await fixture.Client.Access.RevokeKeyAsync(Key, "  Key exposed  ", TestContext.Current.CancellationToken);
            Assert.Equal(Key, answer.KeyId);
            Assert.Equal("revoked", answer.Status);
            Assert.Equal(PipelineFixture.Now, answer.RevokedAt);
        }
        else
        {
            var answer = await fixture.Client.Access.SuspendApplicationAsync("  Key exposed  ", TestContext.Current.CancellationToken);
            Assert.Equal(Application, answer.ApplicationId);
            Assert.Equal("suspended", answer.Status);
            Assert.Equal(PipelineFixture.Now, answer.SuspendedAt);
        }

        var request = fixture.Stub.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Anis.Partners/2.2.0", request.Headers.UserAgent.ToString());
        Assert.Equal(revoke ? $"/v1/keys/{Key:D}/revoke" : "/v1/application/suspend", request.RequestUri!.AbsolutePath);
        Assert.Equal("{\"reason\":\"Key exposed\"}", Encoding.UTF8.GetString(fixture.Stub.LastRequestBody!));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(ContentDigest.Of(fixture.Stub.LastRequestBody!), request.Content.Headers.GetValues("Content-Digest").Single());
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
        Assert.StartsWith("sig1=(\"@method\" \"@authority\" \"@path\" \"@query\" \"content-digest\" \"nonce\" \"x-anis-date\")",
            request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
        Assert.EndsWith($";nonce=\"{request.Headers.GetValues("Nonce").Single()}\"",
            request.Headers.GetValues("Signature-Input").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_reason_is_refused_before_sending(string? reason)
    {
        using var fixture = new PipelineFixture();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Client.Access.SuspendApplicationAsync(reason!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Client.Access.RevokeKeyAsync(Key, reason!, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_reason_longer_than_500_characters_is_refused_before_sending()
    {
        using var fixture = new PipelineFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.Access.RevokeKeyAsync(Key, new string('x', 501), TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Stub.Requests);
    }

    [Fact]
    public async Task A_500_character_reason_is_accepted()
    {
        using var fixture = new PipelineFixture();
        await fixture.Client.Access.SuspendApplicationAsync(new string('x', 500), TestContext.Current.CancellationToken);
        Assert.Single(fixture.Stub.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "resource_not_found", typeof(ResourceNotFoundException))]
    [InlineData(HttpStatusCode.Conflict, "key_not_revocable", typeof(AnisApiException))]
    [InlineData(HttpStatusCode.BadRequest, "validation_failed", typeof(ValidationFailedException))]
    public async Task A_signed_stop_refusal_is_mapped(HttpStatusCode status, string code, Type exceptionType)
    {
        using var fixture = new PipelineFixture();
        fixture.Stub.Status = status;
        fixture.Stub.Body = $$"""{"status":{{(int)status}},"code":"{{code}}","requestId":"stop-request"}""";
        var failure = await Assert.ThrowsAnyAsync<AnisApiException>(() => fixture.Client.Access.RevokeKeyAsync(Key, "Retire access", TestContext.Current.CancellationToken));
        Assert.IsType(exceptionType, failure);
        Assert.Equal(code, failure.RawCode);
    }
}
