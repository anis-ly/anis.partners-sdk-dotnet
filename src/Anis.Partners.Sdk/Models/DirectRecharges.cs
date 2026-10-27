using System.Text.Json.Serialization;
using Anis.Partners.Sdk.Errors;

namespace Anis.Partners.Sdk.Models;

/// <summary>The current owner state; an unknown create acknowledgment is separate.</summary>
[JsonConverter(typeof(ProductStatusConverter<DirectRechargeStatus>))]
public enum DirectRechargeStatus
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>AwaitingPayment.</summary>
    AwaitingPayment = 1,

    /// <summary>PaymentFailed.</summary>
    PaymentFailed = 2,

    /// <summary>Pending.</summary>
    Pending = 3,

    /// <summary>InProgress.</summary>
    InProgress = 4,

    /// <summary>SuspendedDataError.</summary>
    SuspendedDataError = 5,

    /// <summary>Completed.</summary>
    Completed = 6,

    /// <summary>RejectedAwaitingRefund.</summary>
    RejectedAwaitingRefund = 7,

    /// <summary>RejectedWithRefund.</summary>
    RejectedWithRefund = 8,
}

/// <summary>A direct recharge intent. Persist it with the caller-owned operation id.</summary>
public sealed record CreateDirectRechargeRequest
{
    /// <summary>ProductId.</summary>
    [JsonPropertyName("productId")] public required Guid ProductId { get; init; }

    /// <summary>PayoutAmount.</summary>
    [JsonPropertyName("payoutAmount")] public required string PayoutAmount { get; init; }

    /// <summary>ExpectedTotal.</summary>
    [JsonPropertyName("expectedTotal")] public required Money ExpectedTotal { get; init; }

    /// <summary>Recipient.</summary>
    [JsonPropertyName("recipient")] public required IReadOnlyList<DirectRechargeRecipientInput> Recipient { get; init; }

    /// <summary>Note.</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }
}

/// <summary>A recipient field supplied by the partner.</summary>
public sealed record DirectRechargeRecipientInput
{
    /// <summary>Key.</summary>
    [JsonPropertyName("key")] public required string Key { get; init; }

    /// <summary>Value.</summary>
    [JsonPropertyName("value")] public required string Value { get; init; }

    /// <summary>Recipient details are personal data and never reach a log through formatting.</summary>
    public override string ToString() => $"DirectRechargeRecipientInput {{ Key = {Key}, Value = <redacted> }}";
}

/// <summary>An owner-described recipient value.</summary>
public sealed record DirectRechargeRecipientValue
{
    /// <summary>Key.</summary>
    [JsonPropertyName("key")] public required string Key { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LocalizedText? Name { get; init; }

    /// <summary>Value.</summary>
    [JsonPropertyName("value")] public required string Value { get; init; }

    /// <summary>ValueName.</summary>
    [JsonPropertyName("valueName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ValueName { get; init; }

    /// <summary>Recipient details are personal data and never reach a log through formatting.</summary>
    public override string ToString() => $"DirectRechargeRecipientValue {{ Key = {Key}, Value = <redacted> }}";
}

/// <summary>The product frozen on the purchase.</summary>
public sealed record DirectRechargeProductReference
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Title.</summary>
    [JsonPropertyName("title")] public required string Title { get; init; }
}

/// <summary>The full current owner read.</summary>
public sealed record DirectRecharge
{
    /// <summary>OperationId.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required DirectRechargeStatus Status { get; init; }

    /// <summary>Number.</summary>
    [JsonPropertyName("number")] public required int Number { get; init; }

    /// <summary>WalletId.</summary>
    [JsonPropertyName("walletId")] public required Guid WalletId { get; init; }

    /// <summary>Product.</summary>
    [JsonPropertyName("product")] public required DirectRechargeProductReference Product { get; init; }

    /// <summary>Payout.</summary>
    [JsonPropertyName("payout")] public required DirectRechargePayout Payout { get; init; }

    /// <summary>Total.</summary>
    [JsonPropertyName("total")] public required Money Total { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }

    /// <summary>RequestedAt.</summary>
    [JsonPropertyName("requestedAt")] public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>CompletedAt.</summary>
    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>ExchangeRate.</summary>
    [JsonPropertyName("exchangeRate")] public required string ExchangeRate { get; init; }

    /// <summary>Price.</summary>
    [JsonPropertyName("price")] public required Money Price { get; init; }

    /// <summary>Commission.</summary>
    [JsonPropertyName("commission")] public required Money Commission { get; init; }

    /// <summary>Recipient.</summary>
    [JsonPropertyName("recipient")] public required IReadOnlyList<DirectRechargeRecipientValue> Recipient { get; init; }

    /// <summary>Note.</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }

    /// <summary>ProofUrl.</summary>
    [JsonPropertyName("proofUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProofUrl { get; init; }

    /// <summary>Reason.</summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; init; }

    /// <summary>PartialRefund.</summary>
    [JsonPropertyName("partialRefund")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargePartialRefund? PartialRefund { get; init; }
}

/// <summary>A direct recharge summary without recipient or proof.</summary>
public sealed record DirectRechargeSummary
{
    /// <summary>OperationId.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required DirectRechargeStatus Status { get; init; }

    /// <summary>Number.</summary>
    [JsonPropertyName("number")] public required int Number { get; init; }

    /// <summary>Product.</summary>
    [JsonPropertyName("product")] public required DirectRechargeProductReference Product { get; init; }

    /// <summary>Payout.</summary>
    [JsonPropertyName("payout")] public required DirectRechargePayout Payout { get; init; }

    /// <summary>Total.</summary>
    [JsonPropertyName("total")] public required Money Total { get; init; }

    /// <summary>ExternalReference.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }

    /// <summary>RequestedAt.</summary>
    [JsonPropertyName("requestedAt")] public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>CompletedAt.</summary>
    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Whether a settled partial refund exists, including false.</summary>
    [JsonPropertyName("hasPartialRefund")] public required bool HasPartialRefund { get; init; }
}

/// <summary>A settled partial refund, only when enabled by the owner.</summary>
public sealed record DirectRechargePartialRefund
{
    /// <summary>RefundedPayout.</summary>
    [JsonPropertyName("refundedPayout")] public required string RefundedPayout { get; init; }

    /// <summary>RefundedTotal.</summary>
    [JsonPropertyName("refundedTotal")] public required Money RefundedTotal { get; init; }

    /// <summary>RemainingPayout.</summary>
    [JsonPropertyName("remainingPayout")] public required string RemainingPayout { get; init; }

    /// <summary>RemainingTotal.</summary>
    [JsonPropertyName("remainingTotal")] public required Money RemainingTotal { get; init; }
}

/// <summary>Recipient corrections while the purchase is suspended.</summary>
public sealed record UpdateDirectRechargeRecipientRequest
{
    /// <summary>Recipient.</summary>
    [JsonPropertyName("recipient")] public required IReadOnlyList<DirectRechargeRecipientInput> Recipient { get; init; }
}

/// <summary>Optional filters; cursors must be reused with the same filters.</summary>
public sealed record DirectRechargesQuery
{
    /// <summary>Status.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeStatus? Status { get; init; }

    /// <summary>ProductId.</summary>
    [JsonPropertyName("productId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? ProductId { get; init; }

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
public sealed record DirectRechargeAccepted
{
    /// <summary>The caller-owned operation id.</summary>
    [JsonPropertyName("operationId")] public required Guid OperationId { get; init; }

    /// <summary>The exact operation discriminator.</summary>
    [JsonPropertyName("operationType")] public string OperationType => "directRecharge";

    /// <summary>The operation has no final outcome yet.</summary>
    [JsonPropertyName("status")] public string Status => "processing";
}

/// <summary>The outcome of creating or resuming a direct recharge operation.</summary>
public abstract record DirectRechargeResult
{
    private protected DirectRechargeResult(Guid operationId) => OperationId = operationId;

    /// <summary>The caller-owned operation id.</summary>
    public Guid OperationId { get; }
}

/// <summary>The operation is still processing. After a recipient correction, read later or wait for a webhook.</summary>
public sealed record DirectRechargeProcessing : DirectRechargeResult
{
    /// <summary>A processing acknowledgment with no full owner read yet.</summary>
    /// <param name="accepted">The exact three-member acknowledgment.</param>
    /// <param name="retryAfter">The verified retry delay, or five seconds when omitted.</param>
    /// <param name="location">The verified read location, when supplied.</param>
    public DirectRechargeProcessing(DirectRechargeAccepted accepted, TimeSpan retryAfter, Uri? location)
        : base(accepted.OperationId)
    {
        Accepted = accepted;
        RetryAfter = retryAfter;
        Location = location;
    }

    /// <summary>A full owner read whose current state is still processing.</summary>
    /// <param name="recharge">The current full owner read.</param>
    /// <param name="retryAfter">The verified retry delay, or five seconds when omitted.</param>
    /// <param name="location">The verified read location, when supplied.</param>
    public DirectRechargeProcessing(DirectRecharge recharge, TimeSpan retryAfter, Uri? location)
        : base(recharge.OperationId)
    {
        Recharge = recharge;
        RetryAfter = retryAfter;
        Location = location;
    }

    /// <summary>The separate acknowledgment, present only when HTTP 202 supplied it.</summary>
    public DirectRechargeAccepted? Accepted { get; }

    /// <summary>The full owner read, present only when HTTP 200 supplied it.</summary>
    public DirectRecharge? Recharge { get; }

    /// <summary>The verified retry delay, or five seconds when omitted.</summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>The verified read location, when supplied; it is never followed automatically.</summary>
    public Uri? Location { get; }
}

/// <summary>An authoritative final owner state, including a failed purchase.</summary>
/// <param name="Recharge">The full owner read.</param>
public sealed record DirectRechargeFinal(DirectRecharge Recharge) : DirectRechargeResult(Recharge.OperationId);

/// <summary>A definitive refusal for this application; preserve the typed refusal.</summary>
/// <param name="OperationId">The caller-owned operation id.</param>
/// <param name="Refusal">The verified definitive refusal.</param>
public sealed record DirectRechargeNotPlaced(Guid OperationId, AnisApiException Refusal) : DirectRechargeResult(OperationId);

/// <summary>The operation may have happened. Resume using the same id and saved request.
/// An <c>idempotency_conflict</c> on a create or resume lands here too: it does not say the original request failed, it may
/// already have been accepted and charged. Read the operation by its id (<c>GetAsync</c>) before any new attempt.</summary>
/// <param name="OperationId">The caller-owned operation id.</param>
/// <param name="SuggestedDelay">The delay before resuming.</param>
/// <param name="Cause">Why no trustworthy outcome was available.</param>
public sealed record DirectRechargeOutcomeUnknown(Guid OperationId, TimeSpan SuggestedDelay, Exception Cause) : DirectRechargeResult(OperationId);
