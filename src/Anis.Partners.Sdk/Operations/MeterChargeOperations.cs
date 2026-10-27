using System.Net;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Purchasing, reading and recovering meter charges.</summary>
public interface IMeterChargeOperations
{
    /// <summary>Creates under the caller-persisted id; no automatic retry or replacement id.</summary>
    Task<MeterChargeResult> CreateAsync(Guid walletId, Guid operationId, CreateMeterChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Repeats the identical saved intent and id with a fresh signature.</summary>
    Task<MeterChargeResult> ResumeAsync(Guid walletId, Guid operationId, CreateMeterChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads the full owner state. A pre-projection 202 raises MeterChargeProcessingException. The recorded refusal of a
    /// create (a signed 409 or 422 marked replayed) raises the typed AnisApiException: it is the definitive answer for that id.</summary>
    Task<MeterCharge> GetAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Gets one page; invalid filters are returned through the API validation refusal.</summary>
    Task<Page<MeterChargeSummary>> GetMeterChargesPageAsync(Guid walletId, MeterChargesQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets summaries across pages, preserving all original filters and page size.</summary>
    IAsyncEnumerable<MeterChargeSummary> GetMeterChargesAsync(Guid walletId, MeterChargesQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of packages priced for this wallet.</summary>
    Task<Page<MeterPackage>> GetMeterPackagesPageAsync(Guid walletId, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Gets packages across pages.</summary>
    IAsyncEnumerable<MeterPackage> GetMeterPackagesAsync(Guid walletId, CancellationToken cancellationToken = default);
}

internal sealed class MeterChargeOperations(PartnerTransport transport) : IMeterChargeOperations
{
    public Task<MeterChargeResult> CreateAsync(Guid walletId, Guid operationId, CreateMeterChargeRequest request, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, request, resuming: false, cancellationToken);

    public Task<MeterChargeResult> ResumeAsync(Guid walletId, Guid operationId, CreateMeterChargeRequest request, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, request, resuming: true, cancellationToken);

    public async Task<MeterCharge> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var (response, body) = await transport.GetProductResponseAsync(
            "/v1/meter-charges/{operationId}", $"v1/meter-charges/{operationId:D}", cancellationToken).ConfigureAwait(false);
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var accepted = ProductOutcomes.Accepted<MeterChargeAccepted>(body, operationId, "meterCharge");
                throw new MeterChargeProcessingException(accepted, ProductOutcomes.RetryAfter(response), response.Headers.Location);
            }
            ProductOutcomes.RequireReadStatus(response.StatusCode);
            using var document = JsonDocument.Parse(body);
            var value = ProductReadParser.Meter(document.RootElement);
            ProductOutcomes.RequireIdentity(value.OperationId, operationId);
            return value;
        }
    }

    public Task<Page<MeterChargeSummary>> GetMeterChargesPageAsync(Guid walletId, MeterChargesQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        return transport.GetAsync<Page<MeterChargeSummary>>("/v1/wallets/{walletId}/meter-charges",
            ProductPaging.Query($"v1/wallets/{walletId:D}/meter-charges",
                ("status", ProductPaging.EnumValue(query.Status)), ("meterNumber", query.MeterNumber),
                ("from", ProductPaging.Date(query.From)), ("to", ProductPaging.Date(query.To)),
                ("cursor", query.Cursor), ("pageSize", ProductPaging.Number(query.PageSize))), SignatureProfile.SafeRead, cancellationToken);
    }

    public IAsyncEnumerable<MeterChargeSummary> GetMeterChargesAsync(Guid walletId, MeterChargesQuery? query = null, CancellationToken cancellationToken = default)
    {
        var filters = query ?? new();
        return ProductPaging.GetAsync((cursor, token) => GetMeterChargesPageAsync(walletId, filters with { Cursor = cursor }, token), filters.Cursor, cancellationToken);
    }

    public Task<Page<MeterPackage>> GetMeterPackagesPageAsync(Guid walletId, string? cursor = null, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<MeterPackage>>("/v1/wallets/{walletId}/meter-packages",
            ProductPaging.Query($"v1/wallets/{walletId:D}/meter-packages", ("cursor", cursor)), SignatureProfile.SafeRead, cancellationToken);

    public IAsyncEnumerable<MeterPackage> GetMeterPackagesAsync(Guid walletId, CancellationToken cancellationToken = default)
        => ProductPaging.GetAsync((cursor, token) => GetMeterPackagesPageAsync(walletId, cursor, token), null, cancellationToken);

    private async Task<MeterChargeResult> SendAsync(Guid walletId, Guid operationId, CreateMeterChargeRequest request, bool resuming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (operationId == Guid.Empty)
            throw new ArgumentException("Persist a nonempty operation id before sending.", nameof(operationId));
        try
        {
            var (response, body) = await transport.PostOrderAsync("/v1/wallets/{walletId}/meter-charges",
                $"v1/wallets/{walletId:D}/meter-charges", request, operationId, cancellationToken).ConfigureAwait(false);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Accepted)
                    return new MeterChargeProcessing(ProductOutcomes.Accepted<MeterChargeAccepted>(body, operationId, "meterCharge"),
                        ProductOutcomes.RetryAfter(response), response.Headers.Location);
                ProductOutcomes.RequireReadStatus(response.StatusCode);
                using var document = JsonDocument.Parse(body);
                var value = ProductReadParser.Meter(document.RootElement);
                ProductOutcomes.RequireIdentity(value.OperationId, operationId);
                // Final = succeeded, or failed with the refund settled (refunded / not charged). A failed charge whose refund
                // is still pending is not final: money was taken and the refund has not landed, so it stays processing.
                if (value.Status == MeterChargeStatus.Succeeded
                    || (value.Status == MeterChargeStatus.Failed && value.Refund?.Status != MeterRefundStatus.Pending))
                    return new MeterChargeFinal(value);
                if (value.Status == MeterChargeStatus.Unknown)
                    throw new JsonException("The owner state is not recognized; the operation remains unresolved.");
                return new MeterChargeProcessing(value, ProductOutcomes.RetryAfter(response), response.Headers.Location);
            }
        }
        catch (AnisApiException refusal)
        {
            if (ProductOutcomes.IsDefinitive(refusal, resuming))
                return new MeterChargeNotPlaced(operationId, refusal);
            return new MeterChargeOutcomeUnknown(operationId, ProductOutcomes.Delay(refusal), refusal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not RequestSigningException)
        {
            return new MeterChargeOutcomeUnknown(operationId, ProductOutcomes.DefaultDelay, exception);
        }
    }
}
