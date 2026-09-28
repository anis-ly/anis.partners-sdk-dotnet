using System.Net;
using System.Text.Json;
using Anis.Partners.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Buying, and recovering a purchase whose outcome was never delivered.</summary>
internal sealed class OrderOperations(
    PartnerTransport transport,
    ILogger<OrderOperations>? logger = null) : IOrderOperations
{
    private const string IdempotencyReplayedHeader = "Idempotency-Replayed";
    // How long to wait before a resume when Anis named no Retry-After.
    private static readonly TimeSpan DefaultResumeDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger = logger ?? NullLogger<OrderOperations>.Instance;

    public Task<OrderResult> CreateAsync(Guid walletId, Guid operationId, CreateOrderRequest order, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, order, resuming: false, cancellationToken);

    /// <remarks>
    /// The same call on the wire as <see cref="CreateAsync"/>: a signed POST under that idempotency key.
    /// Two names because the intent differs and the wrong one is expensive — CreateAsync with a NEW
    /// operation id after a 202 buys the cards a second time — and because ResumeAsync has no overload
    /// that mints an id.
    /// </remarks>
    public Task<OrderResult> ResumeAsync(Guid walletId, Guid operationId, CreateOrderRequest order, CancellationToken cancellationToken = default)
        => SendAsync(walletId, operationId, order, resuming: true, cancellationToken);

    public Task<Order> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
        => transport.GetAsync<Order>("/v1/orders/{operationId}", $"v1/orders/{operationId:D}", SignatureProfile.SafeRead, cancellationToken);

    private async Task<OrderResult> SendAsync(Guid walletId, Guid operationId, CreateOrderRequest order, bool resuming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        Guard(order);

        try
        {
            var (response, body) = await transport
                .PostOrderAsync("/v1/wallets/{walletId}/orders", $"v1/wallets/{walletId:D}/orders", order, operationId, cancellationToken)
                .ConfigureAwait(false);

            using (response)
                return Classify(operationId, response, body);
        }
        catch (AnisApiException refusal)
        {
            return Refused(operationId, refusal, resuming);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopped the call. The purchase may still have happened, so it is counted as unknown,
            // but the cancellation is the caller's own and goes back to them as .NET's usual exception.
            ReportUnknown(operationId, AnisPartnersTelemetry.NoAnswerReason(exception, cancellationToken));

            throw;
        }
        catch (Exception exception) when (exception is not RequestSigningException)
        {
            // No answer, or none that can be trusted or read: the order may have been placed. A signer failure is
            // excluded because nothing was sent.
            return Unknown(operationId, AnisPartnersTelemetry.NoAnswerReason(exception, cancellationToken), exception, suggestedDelay: null);
        }
    }

    private OrderResult Classify(Guid operationId, HttpResponseMessage response, byte[] body)
    {
        var value = JsonSerializer.Deserialize<Order>(body, AnisJson.Options);

        if (value is null)
        {
            // A verified success that says nothing: the order may well have completed.
            return Unknown(
                operationId,
                "empty_body",
                AnisApiExceptionFactory.EmptyBody(response.StatusCode),
                suggestedDelay: null);
        }

        // 202: admitted, no outcome. Recovery is a repeated signed POST, never a GET loop.
        if (response.StatusCode is HttpStatusCode.Accepted)
        {
            return Report(new OrderProcessing(
                value,
                AnisApiExceptionFactory.RetryAfterOf(response) ?? DefaultResumeDelay,
                response.Headers.Location), "processing");
        }

        // Credentials in the body decide it, and nothing else does. The response that FIRST reports a
        // completion carries them — the fresh 201, or the 200 a resume receives when the first answer was
        // lost — and it is the only chance to read them under orders:create. Letting a header route such a
        // response into the "replayed, nothing here" branch would drop cards the partner paid for, so the
        // replay marker is consulted only when there are no credentials to lose.
        if (value.SoldCards is { Count: > 0 })
            return Report(new OrderCompleted(value), "completed");

        // The covered replay marker with no credentials: an order whose outcome was already delivered.
        // Identifiers and state only; reading the codes again is a reveal.
        var replayed = response.Headers.TryGetValues(IdempotencyReplayedHeader, out var values)
            && values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        if (replayed)
            return Report(new OrderReplayed(value), "replayed");

        return Report(new OrderCompleted(value), "completed");
    }

    // A refusal marked replayed is the order's recorded final answer. Any other refusal of a RESUME was decided before
    // the order was looked at, so it says nothing about the earlier attempt, whose outcome stays unknown.
    private OrderResult Refused(Guid operationId, AnisApiException refusal, bool resuming)
    {
        if (refusal.OrderOutcome is OrderRefusalOutcome.NotPlaced && (refusal.IsReplayed || !resuming))
            return new OrderNotPlaced(operationId, refusal);

        return Unknown(operationId, refusal.RawCode ?? "refused", refusal, refusal.RetryAfter);
    }

    private OrderOutcomeUnknown Unknown(Guid operationId, string reason, Exception cause, TimeSpan? suggestedDelay)
    {
        ReportUnknown(operationId, reason);

        return new OrderOutcomeUnknown(operationId, suggestedDelay ?? DefaultResumeDelay, cause);
    }

    private OrderResult Report(OrderResult result, string outcome)
    {
        AnisPartnersTelemetry.OrderOutcomes.Add(
            1,
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.Client, transport.ClientName),
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.OrderOutcome, outcome));

        // The operation id, never the credentials the completed case carries.
        Log.OrderOutcome(_logger, result.OperationId, outcome);

        return result;
    }

    private void ReportUnknown(Guid operationId, string reason)
    {
        AnisPartnersTelemetry.OrderOutcomes.Add(
            1,
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.Client, transport.ClientName),
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.OrderOutcome, "unknown"),
            new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.ErrorType, reason));

        Log.OrderOutcomeUnknown(_logger, operationId, reason);
    }

    // The gateway refuses a total that is not unit times quantity, computed without floating point. Doing
    // the check here turns a 422 round trip into an exception at the call site, where the arithmetic that
    // produced it is still visible.
    private static void Guard(CreateOrderRequest order)
    {
        if (order.Quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(order), order.Quantity, "An order must be for at least one card.");

        var expected = order.ExpectedUnitPrice.Multiply(order.Quantity);

        if (expected.Amount != order.ExpectedTotal.Amount)
        {
            throw new ArgumentException(
                $"ExpectedTotal is {order.ExpectedTotal.ToWireAmount()} but unit times quantity is "
                + $"{expected.ToWireAmount()}. The gateway computes this without floating point and refuses a "
                + "mismatch; use Money.Multiply rather than a double.",
                nameof(order));
        }

        if (!string.Equals(order.ExpectedUnitPrice.Currency, order.ExpectedTotal.Currency, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ExpectedUnitPrice and ExpectedTotal must carry the same currency.",
                nameof(order));
        }
    }
}
