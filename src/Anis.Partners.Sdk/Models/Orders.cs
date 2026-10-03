using System.Text.Json.Serialization;
using Anis.Partners.Sdk.Errors;

namespace Anis.Partners.Sdk.Models;

/// <summary>Where an order stands.</summary>
/// <remarks>
/// The converter is explicit rather than left to the serializer's web defaults, which differ between the
/// two target frameworks. This enum decides whether credentials exist, so which converter reads it is not
/// something to inherit from a default that can change under the library.
/// </remarks>
[JsonConverter(typeof(LenientEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    /// <summary>Absent, or a value this SDK version does not know.</summary>
    Unknown = 0,

    /// <summary>Admitted and still running. Not an outcome.</summary>
    Processing = 1,

    /// <summary>
    /// Anis tried to learn the outcome from the owner to its limit and could not. The order may or may not
    /// have completed; it is NOT failed and nothing was refunded or charged twice. Do not place it again
    /// under a new id — contact Anis with the operation id.
    /// </summary>
    RecoveryExhausted = 2,

    /// <summary>Terminal success.</summary>
    Completed = 3,

    /// <summary>A definitive owner business refusal — and ONLY that. A timeout is never failed.</summary>
    Failed = 4,
}

/// <summary>What the caller wants to buy.</summary>
/// <remarks>
/// A closed DTO: the gateway rejects unknown members. The total must equal unit times quantity computed
/// WITHOUT floating point, which is why <see cref="Money.Multiply"/> exists and why the wire form is a
/// decimal string.
/// </remarks>
public sealed record CreateOrderRequest
{
    /// <summary>The catalogue card to buy.</summary>
    [JsonPropertyName("cardId")] public required Guid CardId { get; init; }

    /// <summary>How many, at least one and at most the live cap.</summary>
    [JsonPropertyName("quantity")] public required int Quantity { get; init; }

    /// <summary>The unit price READ FROM THE CATALOGUE. Do not compute one.</summary>
    [JsonPropertyName("expectedUnitPrice")] public required Money ExpectedUnitPrice { get; init; }

    /// <summary>Unit times quantity, exactly.</summary>
    [JsonPropertyName("expectedTotal")] public required Money ExpectedTotal { get; init; }

    /// <summary>Your own reference, 1 to 100 safe characters.</summary>
    [JsonPropertyName("externalReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalReference { get; init; }

    /// <summary>Defaults to false and is never silently enabled.</summary>
    [JsonPropertyName("useAllowedDebt")] public bool UseAllowedDebt { get; init; }
}

/// <summary>An order as the surface reports it.</summary>
public sealed record Order
{
    /// <summary>The operation identity — the same UUID the caller sent as <c>Idempotency-Key</c>.</summary>
    [JsonPropertyName("operationId")] public Guid OperationId { get; init; }

    /// <summary>Where it stands.</summary>
    [JsonPropertyName("status")] public OrderStatus Status { get; init; }

    /// <summary>The owner invoice, once one exists.</summary>
    [JsonPropertyName("invoiceId")] public Guid? InvoiceId { get; init; }

    /// <summary>The wallet it was bought from.</summary>
    [JsonPropertyName("walletId")] public Guid? WalletId { get; init; }

    /// <summary>The catalogue card.</summary>
    [JsonPropertyName("cardId")] public Guid? CardId { get; init; }

    /// <summary>How many.</summary>
    [JsonPropertyName("quantity")] public int? Quantity { get; init; }

    /// <summary>What it cost.</summary>
    [JsonPropertyName("total")] public Money? Total { get; init; }

    /// <summary>
    /// The credentials the sale released. Present ONLY on the response that first reports the order
    /// completed — the fresh 201, or the 200 a resume receives when the first response was lost.
    /// </summary>
    /// <remarks>
    /// A replay of a terminal key returns identifiers only, and <c>GET /v1/orders/{operationId}</c> is state
    /// only. Reading them again is a reveal and needs <c>cards:reveal</c>. Prefer the typed
    /// <c>OrderResult</c> the SDK returns, which makes this a compile-time distinction instead of a null
    /// check nobody writes.
    /// </remarks>
    [JsonPropertyName("soldCards")] public IReadOnlyList<RevealedCredential>? SoldCards { get; init; }

    /// <summary>When it completed.</summary>
    [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>The outcome of creating or resuming an order: one of five cases, and nothing else.</summary>
/// <remarks>
/// Every outcome of an order call is a value, so one <c>switch</c> handles all of them:
/// <see cref="OrderCompleted"/>, <see cref="OrderReplayed"/>, <see cref="OrderProcessing"/>,
/// <see cref="OrderOutcomeUnknown"/> and <see cref="OrderNotPlaced"/>. The call throws only for a mistake in the
/// call itself — an invalid request, a signer that failed before anything was sent — or when the caller's own
/// token cancels it.
///
/// Not one object with a nullable credential list. The credential-once rule — the first completion carries
/// them and nothing else does — becomes something the compiler makes you handle, rather than a null check
/// that is easy to skip and expensive to skip.
/// </remarks>
public abstract record OrderResult
{
    private protected OrderResult(Guid operationId) => OperationId = operationId;

    /// <summary>The operation identity the caller owns.</summary>
    public Guid OperationId { get; }
}

/// <summary>The order completed, and this response carries the credentials.</summary>
/// <remarks>
/// Returned for the fresh 201 and for the 200 a resume receives when the first answer was lost. Any order
/// response that carries credentials is this type, whatever else it says — the credentials are never
/// hidden behind <see cref="OrderReplayed"/>. This is the only chance to read them under
/// <c>orders:create</c> alone: persist <see cref="Credentials"/> before doing anything else; reading them
/// again later is a reveal and needs <c>cards:reveal</c>.
/// <para>
/// <see cref="Credentials"/> can be EMPTY, and then <see cref="CodesWithheld"/> is true: Anis withholds the codes of an
/// order that was placed and paid when they cannot be released (a card invalidated or refunded, a code not
/// releasable). The order is still completed — do not buy it again. The withheld cards cannot be revealed either;
/// write to support@anis.ly with the operation id.
/// </para>
/// </remarks>
public sealed record OrderCompleted(Order Order) : OrderResult(Order.OperationId)
{
    /// <summary>The credentials the sale released.</summary>
    public IReadOnlyList<RevealedCredential> Credentials => Order.SoldCards ?? [];

    /// <summary>True when the order completed but Anis released no codes. Do not buy it again; see the remarks.</summary>
    public bool CodesWithheld => Credentials.Count == 0;
}

/// <summary>The order was admitted and has no outcome yet.</summary>
/// <remarks>
/// Recovery is a REPEATED SIGNED POST with the same operation id — <c>ResumeAsync</c> — never a new id and
/// never a GET, which reports state and dispatches nothing.
/// </remarks>
public sealed record OrderProcessing(Order Order, TimeSpan RetryAfter, Uri? Location) : OrderResult(Order.OperationId);

/// <summary>An order whose outcome was already delivered to you earlier.</summary>
/// <remarks>
/// The answer to sending the same operation id again after it completed: the original status (usually
/// 201) with <c>Idempotency-Replayed: true</c>, the operation id, the state and the invoice id — nothing
/// else. The credentials went out on the first completion; if you did not store them, read them again with
/// a reveal (<c>cards:reveal</c>), using this invoice id.
/// </remarks>
public sealed record OrderReplayed(Order Order) : OrderResult(Order.OperationId);

/// <summary>Nobody can say yet whether the order was bought. Resume it with the SAME operation id.</summary>
/// <remarks>
/// The call got no answer (a timeout, a lost connection), an answer that could not be verified, a refusal that
/// reached no decision, a rate limit (the call may be a resend of an attempt that is still selling), a refusal of
/// the caller's access (<c>invalid_credentials</c>, <c>insufficient_scope</c>, <c>wallet_not_granted</c>: restore
/// access before resuming), or — on a resume — a refusal decided before the order was looked at, which says nothing
/// about the earlier attempt. The purchase may have happened. Wait <see cref="SuggestedDelay"/>, then call
/// <c>ResumeAsync</c> with the same operation id and the same request. Never place it again under a new id: that
/// can buy the cards a second time. <see cref="Cause"/> is what ended the call, for your logs.
/// </remarks>
public sealed record OrderOutcomeUnknown(Guid OperationId, TimeSpan SuggestedDelay, Exception Cause) : OrderResult(OperationId);

/// <summary>Nothing was bought and nothing was charged. The order is closed.</summary>
/// <remarks>
/// <see cref="Refusal"/> says why, as the typed refusal (<c>PriceChangedException</c>, <c>OutOfStockException</c>,
/// …) with its <c>Code</c>, <c>RequestId</c> and <c>RetryAfter</c>. Fix the cause, then place a NEW order under a
/// NEW operation id. A recorded refusal marked replayed returns the same answer under the old id, so sending it
/// again is pointless.
/// </remarks>
public sealed record OrderNotPlaced(Guid OperationId, AnisApiException Refusal) : OrderResult(OperationId);
