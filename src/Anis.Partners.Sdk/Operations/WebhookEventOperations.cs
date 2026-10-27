using System.Text.Json;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Reads notification delivery summaries and requests a failed notification again.</summary>
public interface IWebhookEventOperations
{
    /// <summary>Gets one page of delivery summaries.</summary>
    Task<Page<WebhookEvent>> GetWebhookEventsPageAsync(WebhookEventsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets delivery summaries across pages, preserving the status and time filters.</summary>
    IAsyncEnumerable<WebhookEvent> GetWebhookEventsAsync(WebhookEventsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Requests a failed notification again under its original event id. No automatic retry is performed.</summary>
    Task<WebhookEventAccepted> RedeliverAsync(Guid eventId, CancellationToken cancellationToken = default);
}

internal sealed class WebhookEventOperations(PartnerTransport transport) : IWebhookEventOperations
{
    public Task<Page<WebhookEvent>> GetWebhookEventsPageAsync(WebhookEventsQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        return transport.GetAsync<Page<WebhookEvent>>("/v1/webhook-events",
            ProductPaging.Query("v1/webhook-events", ("status", ProductPaging.EnumValue(query.Status)),
                ("since", ProductPaging.Date(query.Since)), ("cursor", query.Cursor)), SignatureProfile.SafeRead, cancellationToken);
    }

    public IAsyncEnumerable<WebhookEvent> GetWebhookEventsAsync(WebhookEventsQuery? query = null, CancellationToken cancellationToken = default)
    {
        var filters = query ?? new();
        return ProductPaging.GetAsync((cursor, token) => GetWebhookEventsPageAsync(filters with { Cursor = cursor }, token), filters.Cursor, cancellationToken);
    }

    public async Task<WebhookEventAccepted> RedeliverAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var accepted = await transport.PostEmptyObjectAsync<WebhookEventAccepted>("/v1/webhook-events/{eventId}/redeliver",
            $"v1/webhook-events/{eventId:D}/redeliver", cancellationToken).ConfigureAwait(false);
        if (accepted.Id != eventId)
            throw new JsonException("The redelivery acknowledgment does not match the requested event.");
        return accepted;
    }
}
