using System.Text.Json;
using System.Text.Json.Serialization;
using Anis.Partners.Sdk.Errors;

namespace Anis.Partners.Sdk.Models;

/// <summary>Current meter charge state.</summary>
[JsonConverter(typeof(ProductStatusConverter<MeterChargeStatus>))]
public enum MeterChargeStatus
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Processing.</summary>
    Processing = 1,

    /// <summary>Succeeded.</summary>
    Succeeded = 2,

    /// <summary>Failed.</summary>
    Failed = 3,
}

/// <summary>Current meter refund state.</summary>
[JsonConverter(typeof(ProductStatusConverter<MeterRefundStatus>))]
public enum MeterRefundStatus
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Refunded.</summary>
    Refunded = 1,

    /// <summary>NotCharged.</summary>
    NotCharged = 2,

    /// <summary>Pending.</summary>
    Pending = 3,
}

/// <summary>A meter failure carried inside a charge read.</summary>
[JsonConverter(typeof(MeterFailureCodeConverter))]
public enum MeterFailureCode
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>MeterNumberInvalid.</summary>
    MeterNumberInvalid = 1,

    /// <summary>MeterNotWhitelisted.</summary>
    MeterNotWhitelisted = 2,

    /// <summary>MeterNotLinked.</summary>
    MeterNotLinked = 3,

    /// <summary>MeterRemoved.</summary>
    MeterRemoved = 4,

    /// <summary>MeterInactive.</summary>
    MeterInactive = 5,

    /// <summary>MeterAccountBlocked.</summary>
    MeterAccountBlocked = 6,

    /// <summary>MeterDataIncomplete.</summary>
    MeterDataIncomplete = 7,

    /// <summary>MeterDebtExceedsPackage.</summary>
    MeterDebtExceedsPackage = 8,

    /// <summary>PackageUnavailable.</summary>
    PackageUnavailable = 9,

    /// <summary>PackageLimitReached.</summary>
    PackageLimitReached = 10,

    /// <summary>TokenNotWritten.</summary>
    TokenNotWritten = 11,

    /// <summary>InsufficientBalance.</summary>
    InsufficientBalance = 12,

    /// <summary>GecolUnavailable.</summary>
    GecolUnavailable = 13,

    /// <summary>ChargeFailed.</summary>
    ChargeFailed = 14,
}

/// <summary>An electricity package priced for the selected wallet.</summary>
public sealed record MeterPackage
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>The single stored package name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>GecolPackageName.</summary>
    [JsonPropertyName("gecolPackageName")] public required string GecolPackageName { get; init; }

    /// <summary>Logo.</summary>
    [JsonPropertyName("logo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Logo { get; init; }

    /// <summary>UnitPrice.</summary>
    [JsonPropertyName("unitPrice")] public required Money UnitPrice { get; init; }

    /// <summary>PersonalPrice.</summary>
    [JsonPropertyName("personalPrice")] public required Money PersonalPrice { get; init; }

    /// <summary>BusinessPrice.</summary>
    [JsonPropertyName("businessPrice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Money? BusinessPrice { get; init; }
}

/// <summary>A meter charge intent. Persist it with the caller-owned operation id before sending.</summary>
public sealed record CreateMeterChargeRequest
{
    /// <summary>PackageId.</summary>
    [JsonPropertyName("packageId")] public required Guid PackageId { get; init; }

    /// <summary>The meter number, including leading zeros.</summary>
    [JsonPropertyName("meterNumber")] public required string MeterNumber { get; init; }

    /// <summary>ExpectedTotal.</summary>
    [JsonPropertyName("expectedTotal")] public required Money ExpectedTotal { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }
}

/// <summary>The full current owner read, distinct from a processing acknowledgment.</summary>
public sealed record MeterCharge
{
    /// <summary>The caller-owned operation id.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required MeterChargeStatus Status { get; init; }

    /// <summary>WalletId.</summary>
    [JsonPropertyName("walletId")] public required Guid WalletId { get; init; }

    /// <summary>MeterNumber.</summary>
    [JsonPropertyName("meterNumber")] public required string MeterNumber { get; init; }

    /// <summary>Package.</summary>
    [JsonPropertyName("package")] public required MeterChargePackage Package { get; init; }

    /// <summary>Total.</summary>
    [JsonPropertyName("total")] public required Money Total { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }

    /// <summary>CreatedAt.</summary>
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Completion time, present only for a succeeded charge.</summary>
    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>PersonalPrice.</summary>
    [JsonPropertyName("personalPrice")] public required Money PersonalPrice { get; init; }

    /// <summary>Receipt.</summary>
    [JsonPropertyName("receipt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MeterReceipt? Receipt { get; init; }

    /// <summary>Failure.</summary>
    [JsonPropertyName("failure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MeterFailure? Failure { get; init; }

    /// <summary>Refund.</summary>
    [JsonPropertyName("refund")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MeterRefund? Refund { get; init; }
}

/// <summary>A charge summary without receipt or tokens.</summary>
public sealed record MeterChargeSummary
{
    /// <summary>The caller-owned operation id.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required MeterChargeStatus Status { get; init; }

    /// <summary>MeterNumber.</summary>
    [JsonPropertyName("meterNumber")] public required string MeterNumber { get; init; }

    /// <summary>Package.</summary>
    [JsonPropertyName("package")] public required MeterChargePackage Package { get; init; }

    /// <summary>Total.</summary>
    [JsonPropertyName("total")] public required Money Total { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }

    /// <summary>CreatedAt.</summary>
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Completion time, present only for a succeeded charge.</summary>
    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>The package frozen when the charge was created.</summary>
public sealed record MeterChargePackage
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>GecolPackageName.</summary>
    [JsonPropertyName("gecolPackageName")] public required string GecolPackageName { get; init; }
}

/// <summary>The complete receipt from the electricity company with tokens in owner order.</summary>
public sealed record MeterReceipt
{
    /// <summary>ReceiptNumber.</summary>
    [JsonPropertyName("receiptNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReceiptNumber { get; init; }

    /// <summary>The decimal string from the electricity company, preserved without rounding.</summary>
    [JsonPropertyName("creditedAmount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreditedAmount { get; init; }

    /// <summary>Tokens.</summary>
    [JsonPropertyName("tokens")] public required IReadOnlyList<MeterToken> Tokens { get; init; }

    /// <summary>Taxes.</summary>
    [JsonPropertyName("taxes")] public required IReadOnlyList<MeterTax> Taxes { get; init; }
}

/// <summary>An ordered meter token, including key-change tokens.</summary>
public sealed record MeterToken
{
    /// <summary>Sequence.</summary>
    [JsonPropertyName("sequence")] public required int Sequence { get; init; }

    /// <summary>Label.</summary>
    [JsonPropertyName("label")] public required string Label { get; init; }

    /// <summary>The token text with spaces preserved.</summary>
    [JsonPropertyName("code")] public required string Code { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }

    /// <summary>The token text is a credit for the meter and never reaches a log through formatting.</summary>
    public override string ToString() => $"MeterToken {{ Sequence = {Sequence}, Label = {Label}, Code = <redacted> }}";
}

/// <summary>A tax line from the electricity company.</summary>
public sealed record MeterTax
{
    /// <summary>Description.</summary>
    [JsonPropertyName("description")] public required string Description { get; init; }

    /// <summary>The decimal string from the electricity company, preserved without rounding.</summary>
    [JsonPropertyName("amount")] public required string Amount { get; init; }
}

/// <summary>The current refund state, without an invented amount.</summary>
public sealed record MeterRefund
{
    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required MeterRefundStatus Status { get; init; }

    /// <summary>Amount.</summary>
    [JsonPropertyName("amount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Money? Amount { get; init; }
}

/// <summary>A failure returned inside a full meter charge read, independent of HTTP problem codes.</summary>
public sealed record MeterFailure
{
    /// <summary>The exact owner failure code, including future values.</summary>
    [JsonPropertyName("code")] public required string RawCode { get; init; }

    /// <summary>The known failure, or Unknown for a future code.</summary>
    [JsonIgnore] public MeterFailureCode Code => MeterFailureCodeConverter.Parse(RawCode);

    /// <summary>The owner's localized explanation.</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LocalizedText? Message { get; init; }
}

internal sealed class MeterFailureCodeConverter : JsonConverter<MeterFailureCode>
{
    private static readonly Dictionary<string, MeterFailureCode> ByWire = new(StringComparer.Ordinal)
    {
        ["meter_number_invalid"] = MeterFailureCode.MeterNumberInvalid,
        ["meter_not_whitelisted"] = MeterFailureCode.MeterNotWhitelisted,
        ["meter_not_linked"] = MeterFailureCode.MeterNotLinked,
        ["meter_removed"] = MeterFailureCode.MeterRemoved,
        ["meter_inactive"] = MeterFailureCode.MeterInactive,
        ["meter_account_blocked"] = MeterFailureCode.MeterAccountBlocked,
        ["meter_data_incomplete"] = MeterFailureCode.MeterDataIncomplete,
        ["meter_debt_exceeds_package"] = MeterFailureCode.MeterDebtExceedsPackage,
        ["package_unavailable"] = MeterFailureCode.PackageUnavailable,
        ["package_limit_reached"] = MeterFailureCode.PackageLimitReached,
        ["token_not_written"] = MeterFailureCode.TokenNotWritten,
        ["insufficient_balance"] = MeterFailureCode.InsufficientBalance,
        ["gecol_unavailable"] = MeterFailureCode.GecolUnavailable,
        ["charge_failed"] = MeterFailureCode.ChargeFailed,
    };

    internal static MeterFailureCode Parse(string? value)
        => value is not null && ByWire.TryGetValue(value, out var code) ? code : MeterFailureCode.Unknown;

    public override MeterFailureCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? Parse(reader.GetString()) : MeterFailureCode.Unknown;

    public override void Write(Utf8JsonWriter writer, MeterFailureCode value, JsonSerializerOptions options)
        => writer.WriteStringValue(ByWire.FirstOrDefault(pair => pair.Value == value).Key ?? "unknown");
}

/// <summary>Optional filters; cursors must be reused with the same filters.</summary>
public sealed record MeterChargesQuery
{
    /// <summary>Status.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MeterChargeStatus? Status { get; init; }

    /// <summary>MeterNumber.</summary>
    [JsonPropertyName("meterNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MeterNumber { get; init; }

    /// <summary>From.</summary>
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? From { get; init; }

    /// <summary>To.</summary>
    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? To { get; init; }

    /// <summary>Cursor.</summary>
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cursor { get; init; }

    /// <summary>Omit for 50; the server accepts 1 through 100.</summary>
    [JsonPropertyName("pageSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PageSize { get; init; }
}

/// <summary>A processing acknowledgment; full owner reads are separate.</summary>
public sealed record MeterChargeAccepted
{
    /// <summary>The caller-owned operation id.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>The exact operation discriminator.</summary>
    [JsonPropertyName("operationType")] public string OperationType => "meterCharge";

    /// <summary>The operation has no final outcome yet.</summary>
    [JsonPropertyName("status")] public string Status => "processing";
}

/// <summary>The outcome of creating or resuming a meter charge operation.</summary>
public abstract record MeterChargeResult
{
    private protected MeterChargeResult(Guid operationId) => OperationId = operationId;

    /// <summary>The caller-owned operation id.</summary>
    public Guid OperationId { get; }
}

/// <summary>The operation is still processing. Resume using the same id and saved request.</summary>
public sealed record MeterChargeProcessing : MeterChargeResult
{
    /// <summary>A processing acknowledgment with no full owner read yet.</summary>
    /// <param name="accepted">The exact three-member acknowledgment.</param>
    /// <param name="retryAfter">The verified retry delay, or five seconds when omitted.</param>
    /// <param name="location">The verified read location, when supplied.</param>
    public MeterChargeProcessing(MeterChargeAccepted accepted, TimeSpan retryAfter, Uri? location)
        : base(accepted.OperationId)
    {
        Accepted = accepted;
        RetryAfter = retryAfter;
        Location = location;
    }

    /// <summary>A full owner read whose current state is still processing.</summary>
    /// <param name="charge">The current full owner read.</param>
    /// <param name="retryAfter">The verified retry delay, or five seconds when omitted.</param>
    /// <param name="location">The verified read location, when supplied.</param>
    public MeterChargeProcessing(MeterCharge charge, TimeSpan retryAfter, Uri? location)
        : base(charge.OperationId)
    {
        Charge = charge;
        RetryAfter = retryAfter;
        Location = location;
    }

    /// <summary>The separate acknowledgment, present only when HTTP 202 supplied it.</summary>
    public MeterChargeAccepted? Accepted { get; }

    /// <summary>The full owner read, present only when HTTP 200 supplied it.</summary>
    public MeterCharge? Charge { get; }

    /// <summary>The verified retry delay, or five seconds when omitted.</summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>The verified read location, when supplied; it is never followed automatically.</summary>
    public Uri? Location { get; }
}

/// <summary>An authoritative final owner state, including a failed purchase.</summary>
/// <param name="Charge">The full owner read.</param>
public sealed record MeterChargeFinal(MeterCharge Charge) : MeterChargeResult(Charge.OperationId);

/// <summary>A definitive refusal for this application; preserve the typed refusal.</summary>
/// <param name="OperationId">The caller-owned operation id.</param>
/// <param name="Refusal">The verified definitive refusal.</param>
public sealed record MeterChargeNotPlaced(Guid OperationId, AnisApiException Refusal) : MeterChargeResult(OperationId);

/// <summary>The operation may have happened. Resume using the same id and saved request.
/// An <c>idempotency_conflict</c> on a create or resume lands here too: it does not say the original request failed, it may
/// already have been accepted and charged. Read the operation by its id (<c>GetAsync</c>) before any new attempt.</summary>
/// <param name="OperationId">The caller-owned operation id.</param>
/// <param name="SuggestedDelay">The delay before resuming.</param>
/// <param name="Cause">Why no trustworthy outcome was available.</param>
public sealed record MeterChargeOutcomeUnknown(Guid OperationId, TimeSpan SuggestedDelay, Exception Cause) : MeterChargeResult(OperationId);
