using System.Text.Json;
using System.Text.Json.Nodes;
using Anis.Partners.Sample;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

public sealed class WebhookReceiverExampleTests
{
    [Fact]
    public async Task A_failed_handler_is_retried_and_only_committed_completion_is_acknowledged()
    {
        using var fixture = new WebhookFixture();
        var inbox = new TransactionalInbox { FailBeforeCommit = true };
        var receiver = new WebhookReceiverExample(fixture.Verifier, inbox);
        var request = fixture.Create("meterCharge.succeeded", JsonNode.Parse(ProductBodies.MeterSucceeded())!);
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<IOException>(() => receiver.HandleAsync(request.Method, request.TargetUri, request.Headers, request.Body, ct));
        Assert.Equal("pending", inbox.State);
        Assert.Equal(0, inbox.BusinessUpdates);
        inbox.FailBeforeCommit = false;
        var retry = await receiver.HandleAsync(request.Method, request.TargetUri, request.Headers, request.Body, ct);
        Assert.Equal(200, retry.Status);
        Assert.Equal("completed", inbox.State);
        Assert.Equal(1, inbox.BusinessUpdates);
        var duplicate = await receiver.HandleAsync(request.Method, request.TargetUri, request.Headers, request.Body, ct);
        Assert.Equal(200, duplicate.Status);
        Assert.Equal(1, inbox.BusinessUpdates);
        Assert.Equal(3, inbox.Calls);
    }

    [Fact]
    public async Task Pending_work_is_not_acknowledged_as_completed()
    {
        using var fixture = new WebhookFixture();
        var inbox = new TransactionalInbox { Busy = true };
        var receiver = new WebhookReceiverExample(fixture.Verifier, inbox);
        var request = fixture.Create("directRecharge.completed", JsonNode.Parse(ProductBodies.DirectRecharge("completed"))!);
        var response = await receiver.HandleAsync(request.Method, request.TargetUri, request.Headers, request.Body, TestContext.Current.CancellationToken);
        Assert.Equal(503, response.Status);
        Assert.Equal("pending", inbox.State);
        Assert.Equal(0, inbox.BusinessUpdates);
    }

    [Fact]
    public async Task Every_verified_challenge_retry_echoes_without_entering_the_operational_inbox()
    {
        using var fixture = new WebhookFixture();
        var inbox = new TransactionalInbox();
        var receiver = new WebhookReceiverExample(fixture.Verifier, inbox);
        var challenge = new string('A', 43);
        var request = fixture.Create("webhook_endpoint.verification", new JsonObject { ["challenge"] = challenge, ["generation"] = 7 });
        for (var delivery = 0; delivery < 2; delivery++)
        {
            var response = await receiver.HandleAsync(request.Method, request.TargetUri, request.Headers, request.Body, TestContext.Current.CancellationToken);
            Assert.Equal(200, response.Status);
            using var body = JsonDocument.Parse(response.Body!);
            Assert.Equal("challenge", Assert.Single(body.RootElement.EnumerateObject()).Name);
            Assert.Equal(challenge, body.RootElement.GetProperty("challenge").GetString());
        }
        Assert.Equal(0, inbox.Calls);
    }

    [Theory]
    [InlineData("https://pos.example.ly/wrong?tenant=one%2Btwo")]
    [InlineData("https://pos.example.ly/hooks/anis?tenant=changed")]
    public async Task An_actual_path_or_query_mismatch_never_reaches_the_inbox(string actualTarget)
    {
        using var fixture = new WebhookFixture();
        var inbox = new TransactionalInbox();
        var receiver = new WebhookReceiverExample(fixture.Verifier, inbox);
        var request = fixture.Create("meterCharge.succeeded", JsonNode.Parse(ProductBodies.MeterSucceeded())!);
        var response = await receiver.HandleAsync(request.Method, actualTarget, request.Headers, request.Body, TestContext.Current.CancellationToken);
        Assert.Equal(400, response.Status);
        Assert.Null(response.Body);
        Assert.Equal(0, inbox.Calls);
    }

    // Models the host's database transaction: staged business changes disappear when commit fails.
    private sealed class TransactionalInbox : IWebhookInbox
    {
        private Guid? _id;
        public bool FailBeforeCommit { get; set; }
        public bool Busy { get; set; }
        public string? State { get; private set; }
        public int BusinessUpdates { get; private set; }
        public int Calls { get; private set; }
        public Task<bool> ProcessOnceAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            Calls++;
            if (_id is { } id) Assert.Equal(id, envelope.Id);
            _id = envelope.Id;
            if (State == "completed") return Task.FromResult(true);
            State = "pending";
            if (Busy) return Task.FromResult(false);
            var stagedUpdates = BusinessUpdates + 1;
            if (FailBeforeCommit) throw new IOException("Business transaction failed.");
            BusinessUpdates = stagedUpdates;
            State = "completed";
            return Task.FromResult(true);
        }
    }
}
