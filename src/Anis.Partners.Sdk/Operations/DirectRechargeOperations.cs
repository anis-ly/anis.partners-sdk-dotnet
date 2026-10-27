using System.Net;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Purchasing, reading and recovering direct recharges.</summary>
public interface IDirectRechargeOperations
{
    /// <summary>Creates under the caller-persisted id; no automatic retry or replacement id.</summary>
    Task<DirectRechargeResult> CreateAsync(Guid walletId, Guid operationId, CreateDirectRechargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Repeats the identical saved intent and id with a fresh signature.</summary>
    Task<DirectRechargeResult> ResumeAsync(Guid walletId, Guid operationId, CreateDirectRechargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the full owner state. A pre-projection 202 raises DirectRechargeProcessingException. The recorded refusal of a
    /// create (a signed 409 or 422 marked replayed) raises the typed AnisApiException: it is the definitive answer for that id.</summary>
    Task<DirectRecharge> GetAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Gets one page; invalid filters are returned through the API validation refusal.</summary>
    Task<Page<DirectRechargeSummary>> GetDirectRechargesPageAsync(Guid walletId, DirectRechargesQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets summaries across pages, preserving all original filters and page size.</summary>
    IAsyncEnumerable<DirectRechargeSummary> GetDirectRechargesAsync(Guid walletId, DirectRechargesQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Accepts a recipient correction with a signed 202 and read location. Read later or wait for a webhook; never retry automatically.</summary>
    Task<DirectRechargeProcessing> UpdateRecipientAsync(Guid operationId, UpdateDirectRechargeRecipientRequest request, CancellationToken cancellationToken = default);
}

internal sealed class DirectRechargeOperations(PartnerTransport transport) : IDirectRechargeOperations
{
    public Task<DirectRechargeResult> CreateAsync(Guid walletId, Guid operationId, CreateDirectRechargeRequest request, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, request, resuming: false, cancellationToken);

    public Task<DirectRechargeResult> ResumeAsync(Guid walletId, Guid operationId, CreateDirectRechargeRequest request, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, request, resuming: true, cancellationToken);

    public async Task<DirectRecharge> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var (response, body) = await transport.GetProductResponseAsync(
            "/v1/direct-recharges/{operationId}", $"v1/direct-recharges/{operationId:D}", cancellationToken).ConfigureAwait(false);
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var accepted = ProductOutcomes.Accepted<DirectRechargeAccepted>(body, operationId, "directRecharge");
                throw new DirectRechargeProcessingException(accepted, ProductOutcomes.RetryAfter(response), response.Headers.Location);
            }
            ProductOutcomes.RequireReadStatus(response.StatusCode);
            using var document = JsonDocument.Parse(body);
            var value = ProductReadParser.DirectRecharge(document.RootElement);
            ProductOutcomes.RequireIdentity(value.OperationId, operationId);
            return value;
        }
    }

    public Task<Page<DirectRechargeSummary>> GetDirectRechargesPageAsync(Guid walletId, DirectRechargesQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        return transport.GetAsync<Page<DirectRechargeSummary>>("/v1/wallets/{walletId}/direct-recharges",
            ProductPaging.Query($"v1/wallets/{walletId:D}/direct-recharges",
                ("status", ProductPaging.EnumValue(query.Status)), ("productId", query.ProductId?.ToString("D")),
                ("from", ProductPaging.Date(query.From)), ("to", ProductPaging.Date(query.To)),
                ("cursor", query.Cursor), ("pageSize", ProductPaging.Number(query.PageSize))), SignatureProfile.SafeRead, cancellationToken);
    }

    public IAsyncEnumerable<DirectRechargeSummary> GetDirectRechargesAsync(Guid walletId, DirectRechargesQuery? query = null, CancellationToken cancellationToken = default)
    {
        var filters = query ?? new();
        return ProductPaging.GetAsync((cursor, token) => GetDirectRechargesPageAsync(walletId, filters with { Cursor = cursor }, token), filters.Cursor, cancellationToken);
    }

    public async Task<DirectRechargeProcessing> UpdateRecipientAsync(Guid operationId, UpdateDirectRechargeRecipientRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (response, body) = await transport.SendNonceMutationResponseAsync(HttpMethod.Patch,
            "/v1/direct-recharges/{operationId}/recipient", $"v1/direct-recharges/{operationId:D}/recipient", request, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            var accepted = ProductOutcomes.Accepted<DirectRechargeAccepted>(body, operationId, "directRecharge");
            var location = response.Headers.Location;
            if (location is null || string.IsNullOrWhiteSpace(location.OriginalString))
                throw new JsonException("The recipient correction acknowledgement requires a Location header.");
            return new DirectRechargeProcessing(accepted, ProductOutcomes.RetryAfter(response), location);
        }
    }

    private async Task<DirectRechargeResult> SendAsync(Guid walletId, Guid operationId, CreateDirectRechargeRequest request, bool resuming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (operationId == Guid.Empty)
            throw new ArgumentException("Persist a nonempty operation id before sending.", nameof(operationId));
        try
        {
            var (response, body) = await transport.PostOrderAsync("/v1/wallets/{walletId}/direct-recharges",
                $"v1/wallets/{walletId:D}/direct-recharges", request, operationId, cancellationToken).ConfigureAwait(false);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                    return new DirectRechargeProcessing(ProductOutcomes.Accepted<DirectRechargeAccepted>(body, operationId, "directRecharge"),
                        ProductOutcomes.RetryAfter(response), response.Headers.Location);
                ProductOutcomes.RequireReadStatus(response.StatusCode);
                using var document = JsonDocument.Parse(body);
                var value = ProductReadParser.DirectRecharge(document.RootElement);
                ProductOutcomes.RequireIdentity(value.OperationId, operationId);
                if (value.Status is DirectRechargeStatus.PaymentFailed or DirectRechargeStatus.Completed or DirectRechargeStatus.RejectedWithRefund)
                    return new DirectRechargeFinal(value);
                if (value.Status == DirectRechargeStatus.Unknown)
                    throw new JsonException("The owner state is not recognized; the operation remains unresolved.");
                return new DirectRechargeProcessing(value, ProductOutcomes.RetryAfter(response), response.Headers.Location);
            }
        }
        catch (AnisApiException refusal)
        {
            if (ProductOutcomes.IsDefinitive(refusal, resuming))
                return new DirectRechargeNotPlaced(operationId, refusal);
            return new DirectRechargeOutcomeUnknown(operationId, ProductOutcomes.Delay(refusal), refusal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not RequestSigningException)
        {
            return new DirectRechargeOutcomeUnknown(operationId, ProductOutcomes.DefaultDelay, exception);
        }
    }
}
