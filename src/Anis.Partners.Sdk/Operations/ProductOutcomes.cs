using System.Net;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;

namespace Anis.Partners.Sdk.Operations;

internal static class ProductOutcomes
{
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(5);

    public static T Read<T>(byte[] body) => JsonSerializer.Deserialize<T>(body, AnisJson.Options)
        ?? throw new JsonException("The operation body is empty.");

    public static T Accepted<T>(byte[] body, Guid operationId, string operationType)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
            || !root.TryGetProperty("operationId", out var id) || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var actualId) || actualId == Guid.Empty || actualId != operationId
            || !root.TryGetProperty("operationType", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != operationType
            || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "processing")
            throw new JsonException("The processing acknowledgment does not match the operation contract.");
        return Read<T>(body);
    }

    public static void RequireIdentity(Guid actual, Guid expected)
    {
        if (actual == Guid.Empty || actual != expected)
            throw new JsonException("The owner read does not match the requested operation.");
    }

    public static void RequireReadStatus(HttpStatusCode status)
    {
        if (status != HttpStatusCode.OK)
            throw new JsonException("A full product owner read requires HTTP 200.");
    }

    public static TimeSpan RetryAfter(HttpResponseMessage response) => AnisApiExceptionFactory.RetryAfterOf(response) ?? DefaultDelay;
    public static TimeSpan Delay(AnisApiException refusal) => refusal.RetryAfter
        ?? (OrderRefusals.RefusedAtTheDoor(refusal.Code) ? TimeSpan.FromSeconds(60) : DefaultDelay);

    public static bool IsDefinitive(AnisApiException refusal, bool resuming)
    {
        // idempotency_conflict is never a definitive "not placed": the original request may already have been accepted
        // and charged, so it stays unknown and the caller reads the operation by its id (GetAsync) before any new attempt.
        var businessRefusal = refusal.Code is PartnerErrorCode.MeterRejected or PartnerErrorCode.PackageUnavailable
            or PartnerErrorCode.DirectRechargeProductUnavailable or PartnerErrorCode.AmountOutOfRange
            or PartnerErrorCode.PriceChanged or PartnerErrorCode.InsufficientBalance or PartnerErrorCode.SpendingLimitExceeded
            or PartnerErrorCode.PurchaseNotAllowed or PartnerErrorCode.ValidationFailed or PartnerErrorCode.CurrencyNotSupported
            or PartnerErrorCode.OwnerLimitExceeded or PartnerErrorCode.DailyLimitExceeded;
        return businessRefusal && (!resuming || refusal.IsReplayed);
    }
}
