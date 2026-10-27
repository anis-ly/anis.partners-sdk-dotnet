using System.Text.Json;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sample;

/// <summary>What a receiving host does with a notification, independent of any web framework.</summary>
/// <remarks>
/// Call <see cref="HandleAsync"/> from the endpoint at the registered URL with the request exactly as received.
/// Read the body as raw bytes BEFORE JSON middleware runs: re-serializing changes the bytes the signature covers.
/// Expected URL and application id belong to verifier configuration. The actual external target is
/// reconstructed independently by the host after validating its proxy and host context.
/// </remarks>
internal sealed class WebhookReceiverExample(PartnerWebhookVerifier verifier, IWebhookInbox inbox)
{
    /// <summary>Returns the HTTP status and the body to answer with.</summary>
    public async Task<(int Status, string? Body)> HandleAsync(string method, string actualExternalTargetUri,
        IReadOnlyDictionary<string, string> headers, byte[] rawBody, CancellationToken ct)
    {
        WebhookEnvelope envelope;

        try
        {
            envelope = await verifier.VerifyAndParseAsync(new VerifiableWebhook(method, actualExternalTargetUri, headers, rawBody), ct);
        }
        catch (UnverifiableWebhookException failure)
        {
            // The reason only: nothing from the refused request is used, echoed or stored.
            Console.WriteLine($"webhook refused: {failure.Failure}");
            return (400, null);
        }

        // Every verified challenge retry needs the echo, including a previously seen id.
        if (envelope.Data is WebhookVerificationData verification)
            return (200, JsonSerializer.Serialize(new { challenge = verification.Challenge }));

        // Completion and business changes belong to one transaction. A recorded pending id is not completion.
        return await inbox.ProcessOnceAsync(envelope, ct) ? (200, null) : (503, null);
    }
}

/// <summary>The partner's own durable store.</summary>
internal interface IWebhookInbox
{
    /// <summary>
    /// Durably inserts a pending inbox entry keyed by envelope id, then locks that entry in a database
    /// transaction. Applies the current data status and marks completed in that SAME business transaction.
    /// Returns true only after commit or for an already completed id. Pending work returns false or throws;
    /// failure rolls back business changes and completion, leaving the entry recoverable on the next delivery.
    /// Concurrent deliveries must serialize on the id. The host implements this against its business database.
    /// </summary>
    Task<bool> ProcessOnceAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}
