using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Manages this application's webhook endpoint without an additional scope.</summary>
public interface IWebhookEndpointOperations
{
    /// <summary>Reads the endpoint registration.</summary>
    Task<WebhookEndpoint> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the endpoint URL. Reread after an interrupted change before choosing to retry.</summary>
    Task<WebhookEndpoint> SetAsync(SetWebhookEndpointRequest request, CancellationToken cancellationToken = default);

    /// <summary>Queues a signed test notification. Reread deliveries after an interrupted request.</summary>
    Task<WebhookEventAccepted> TestAsync(CancellationToken cancellationToken = default);
}

internal sealed class WebhookEndpointOperations(PartnerTransport transport) : IWebhookEndpointOperations
{
    public Task<WebhookEndpoint> GetAsync(CancellationToken cancellationToken = default)
        => transport.GetAsync<WebhookEndpoint>("/v1/webhook-endpoint", "v1/webhook-endpoint", SignatureProfile.SafeRead, cancellationToken);

    public Task<WebhookEndpoint> SetAsync(SetWebhookEndpointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return transport.SendNonceMutationAsync<WebhookEndpoint, SetWebhookEndpointRequest>(HttpMethod.Put,
            "/v1/webhook-endpoint", "v1/webhook-endpoint", request, cancellationToken);
    }

    public Task<WebhookEventAccepted> TestAsync(CancellationToken cancellationToken = default)
        => transport.PostEmptyObjectAsync<WebhookEventAccepted>("/v1/webhook-endpoint/test", "v1/webhook-endpoint/test", cancellationToken);
}
