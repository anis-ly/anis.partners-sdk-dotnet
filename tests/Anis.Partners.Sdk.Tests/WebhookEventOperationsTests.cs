using System.Net;
using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Delivery summaries and redelivery requests.</summary>
public sealed class WebhookEventOperationsTests
{
    private const string EmptyObjectDigest = "sha-256=:RBNvo1WzZ4oRRq0W9+hknpT7T8If536DEMBg9hyq/4o=:";
    private static readonly Guid Event = Guid.Parse("4bceba8e-55d1-59ec-b560-6c1e08f2ae3a");

    private const string Delivered = """{"id":"4bceba8e-55d1-59ec-b560-6c1e08f2ae3a","type":"meterCharge.succeeded","status":"delivered","attempts":1,"lastAttemptAt":"2026-10-20T08:13:03Z","nextAttemptAt":null,"lastResponseCode":200,"lastFailureKind":null}""";

    [Fact]
    public async Task A_page_keeps_the_four_nullable_summary_members_explicit()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = $$"""{"items":[{{Delivered}}],"nextCursor":null}""";

        var page = await fixture.Client.WebhookEvents.GetWebhookEventsPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(page.Items);

        Assert.Equal(WebhookDeliveryStatus.Delivered, item.Status);
        Assert.Equal(WebhookEventType.MeterChargeSucceeded, item.Type);
        Assert.Equal("meterCharge.succeeded", item.RawType);
        Assert.Equal(1, item.Attempts);
        Assert.Null(item.NextAttemptAt);
        Assert.Null(item.LastFailureKind);
        Assert.Equal(200, item.LastResponseCode);
        Assert.Null(page.NextCursor);

        // Every member the schema requires is written, null included.
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(item, AnisJson.Options));

        foreach (var name in new[] { "id", "type", "status", "attempts", "lastAttemptAt", "nextAttemptAt", "lastResponseCode", "lastFailureKind" })
            Assert.True(written.RootElement.TryGetProperty(name, out _), name);

        Assert.Equal(JsonValueKind.Null, written.RootElement.GetProperty("nextAttemptAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, written.RootElement.GetProperty("lastFailureKind").ValueKind);
        Assert.Equal(string.Empty, fixture.Stub.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task A_failed_delivery_names_why_and_a_future_kind_or_type_does_not_fail_the_read()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Body = """
            {"items":[{"id":"4bceba8e-55d1-59ec-b560-6c1e08f2ae3a","type":"a.future.type","status":"retrying","attempts":3,"lastAttemptAt":"2026-10-20T08:13:03Z","nextAttemptAt":"2026-10-20T08:23:03Z","lastResponseCode":503,"lastFailureKind":"a_kind_from_the_future"},
            {"id":"5bceba8e-55d1-59ec-b560-6c1e08f2ae3a","type":"directRecharge.paid","status":"waitingForEndpoint","attempts":0,"lastAttemptAt":null,"nextAttemptAt":null,"lastResponseCode":null,"lastFailureKind":"tls"}],"nextCursor":null}
            """;

        var page = await fixture.Client.WebhookEvents.GetWebhookEventsPageAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WebhookEventType.Unknown, page.Items[0].Type);
        Assert.Equal("a.future.type", page.Items[0].RawType);
        Assert.Equal(WebhookFailureKind.Unknown, page.Items[0].LastFailureKind);
        Assert.Equal(WebhookDeliveryStatus.WaitingForEndpoint, page.Items[1].Status);
        Assert.Equal(WebhookFailureKind.Tls, page.Items[1].LastFailureKind);
        Assert.Null(page.Items[1].LastAttemptAt);
    }

    [Fact]
    public async Task Status_since_and_cursor_are_encoded_once_and_kept_on_every_page()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Unsigned = true;
        fixture.Stub.Bodies.Enqueue($$"""{"items":[{{Delivered}}],"nextCursor":"a+b/c=&d"}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[],"nextCursor":null}""");

        var query = new WebhookEventsQuery { Status = WebhookDeliveryStatus.Failed, Since = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) };
        var count = 0;

        await foreach (var _ in fixture.Client.WebhookEvents.GetWebhookEventsAsync(query, TestContext.Current.CancellationToken))
            count++;

        Assert.Equal(1, count);
        Assert.Equal("?status=failed&since=2026-10-01T00%3A00%3A00.0000000Z", fixture.Stub.Requests[0].RequestUri!.Query);
        Assert.Equal("?status=failed&since=2026-10-01T00%3A00%3A00.0000000Z&cursor=a%2Bb%2Fc%3D%26d", fixture.Stub.Requests[1].RequestUri!.Query);
        Assert.DoesNotContain("pageSize", fixture.Stub.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redelivery_signs_an_empty_object_for_the_same_event_id()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = $$"""{"id":"{{Event:D}}","status":"pending","queuedAt":"2026-10-20T08:30:00Z"}""";

        var accepted = await fixture.Client.WebhookEvents.RedeliverAsync(Event, TestContext.Current.CancellationToken);

        var request = fixture.Stub.LastRequest!;

        Assert.Equal(Event, accepted.Id);
        Assert.Null(accepted.RawType);
        Assert.Equal($"/v1/webhook-events/{Event:D}/redeliver", request.RequestUri!.AbsolutePath);
        Assert.Equal("{}", Encoding.UTF8.GetString(fixture.Stub.LastRequestBody!));
        Assert.Equal(EmptyObjectDigest, request.Content!.Headers.GetValues("Content-Digest").Single());
        Assert.True(request.Headers.Contains("Nonce"));
        Assert.False(request.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task A_redelivery_acknowledgment_for_another_event_is_not_trusted()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Status = HttpStatusCode.Accepted;
        fixture.Stub.Body = """{"id":"11111111-1111-4111-8111-111111111111","status":"pending","queuedAt":"2026-10-20T08:30:00Z"}""";

        await Assert.ThrowsAsync<JsonException>(() => fixture.Client.WebhookEvents.RedeliverAsync(Event, TestContext.Current.CancellationToken));
    }
}
