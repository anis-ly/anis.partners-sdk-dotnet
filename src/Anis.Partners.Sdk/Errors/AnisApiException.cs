using System.Globalization;
using System.Net;
using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Errors;

/// <summary>An error the Anis Partner API returned, as an RFC 9457 problem.</summary>
/// <remarks>
/// One base type carrying the machine contract, plus a small set of subclasses for the refusals a caller
/// actually branches on. Not thirty-six exception types: the .NET convention for client libraries is a
/// single exception with a code, with types reserved for the cases that get their own handling — and a
/// type per code would mean a `catch` list nobody maintains.
///
/// Branch on <see cref="Code"/>. Never on <see cref="Exception.Message"/>: the title and detail behind it
/// are localized presentation that change with Accept-Language and with copy edits.
/// </remarks>
public class AnisApiException : Exception
{
    /// <summary>Builds the exception from a parsed problem and the covered response headers.</summary>
    /// <param name="problem">The signed problem body.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="retryAfter">The signed <c>Retry-After</c>, when the refusal carried one.</param>
    /// <param name="isReplayed">Whether the refusal carried the signed <c>Idempotency-Replayed: true</c>.</param>
    public AnisApiException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
        : base(BuildMessage(problem, status, isReplayed))
    {
        ArgumentNullException.ThrowIfNull(problem);

        Problem = problem;
        Status = status;
        RawCode = problem.Code;
        Code = PartnerErrorCodes.Parse(problem.Code);
        RetryAfter = retryAfter;
        IsReplayed = isReplayed;
    }

    /// <summary>The whole problem as returned.</summary>
    public Problem Problem { get; }

    /// <summary>The resolved code. This is the branching surface.</summary>
    public PartnerErrorCode Code { get; }

    /// <summary>The code exactly as sent, including one this SDK version does not know.</summary>
    public string? RawCode { get; }

    /// <summary>The HTTP status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>The correlation id to quote when asking Anis about this call.</summary>
    public string? RequestId => Problem.RequestId;

    /// <summary>The permanent documentation page for this code.</summary>
    public string? TypeUri => Problem.Type;

    /// <summary>The signed <c>Retry-After</c>, when the refusal carried one.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// True when this is the RECORDED answer of an order placed earlier under the same operation id, not a
    /// new decision.
    /// </summary>
    /// <remarks>
    /// Anis marks it with the signed <c>Idempotency-Replayed: true</c> header. It is what a resume after a
    /// timeout returns when the order had in fact already been refused: the order is closed, nothing was
    /// charged, and sending the same operation id again will return this same answer forever. Close the
    /// order on your side; a new attempt needs a new operation id.
    /// </remarks>
    public bool IsReplayed { get; }

    /// <summary>
    /// Whether the catalogue marks this code retryable. An unknown code is never assumed to be.
    /// </summary>
    /// <remarks>
    /// Retryable describes the CODE, not the call. For orders, follow the returned <see cref="OrderResult"/>:
    /// <see cref="OrderOutcomeUnknown"/> and <see cref="OrderProcessing"/> require resuming the same operation id,
    /// regardless of this flag.
    /// </remarks>
    public bool IsRetryable => PartnerErrorCodes.IsRetryable(Code);

    /// <summary>
    /// The refusal classification from its code and replay marker, before the create/resume rule is applied. For
    /// order recovery, branch on the returned <see cref="OrderResult"/>.
    /// </summary>
    /// <remarks>
    /// A fresh refusal on a resume returns <see cref="OrderOutcomeUnknown"/> even when this property is
    /// <see cref="OrderRefusalOutcome.NotPlaced"/>, because the earlier attempt may have completed.
    ///
    /// A refusal marked <see cref="IsReplayed"/> is always <see cref="OrderRefusalOutcome.NotPlaced"/>, whatever
    /// its code: Anis marks a refusal replayed only when the order is already closed on it. That holds even
    /// for a code this SDK version does not know, or for the <c>internal_error</c> Anis falls back to when a
    /// recorded code has left its catalogue — resuming such an order would return the same answer forever.
    /// </remarks>
    public OrderRefusalOutcome OrderOutcome => IsReplayed ? OrderRefusalOutcome.NotPlaced : OrderRefusals.OutcomeOf(Code);

    private static string BuildMessage(Problem problem, HttpStatusCode status, bool isReplayed)
        => $"Anis returned {(int)status} {problem?.Code}"
         + (isReplayed ? " (the recorded answer of an earlier attempt with this operation id)" : string.Empty)
         + (problem?.RequestId is { } id ? $" (request {id})" : string.Empty)
         + ". Branch on Code, not on this message.";
}

/// <summary>A refusal classification used before the create/resume rule is applied.</summary>
public enum OrderRefusalOutcome
{
    /// <summary>
    /// Nothing was bought and nothing was charged. Fix the cause (see the code), then place the order again
    /// under a NEW operation id. On a resume, a fresh (not replayed) refusal returns
    /// <see cref="OrderOutcomeUnknown"/> instead, because the earlier attempt may have completed.
    /// </summary>
    NotPlaced = 1,

    /// <summary>
    /// The order may still complete. Resume it with the SAME operation id and the same body — never a new
    /// id, which could buy the cards a second time.
    /// </summary>
    Unknown = 2,
}

/// <summary>The wallet cannot cover the purchase. Nothing was charged; the order is closed.</summary>
/// <remarks>Top up, then place a NEW order with a NEW operation id. Resending this id returns this refusal again.</remarks>
public sealed class InsufficientBalanceException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>The price moved between the catalogue read and the order. Nothing was charged; the order is closed.</summary>
/// <remarks>Re-read the catalogue and place a NEW order with a NEW operation id. Never resume this one with a different price.</remarks>
public sealed class PriceChangedException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>
/// The card cannot be sold in this quantity: sold out, not enough stock, outside the card's own minimum or
/// maximum, no longer offered, or an unknown card. Nothing was charged; the order is closed.
/// </summary>
/// <remarks>
/// Covers <c>quantity_unavailable</c> and <c>card_unavailable</c>. Re-read the catalogue (it reports
/// <c>available</c> per card) and place a NEW order with a NEW operation id.
/// </remarks>
public sealed class OutOfStockException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>The same operation id was already used for a DIFFERENT order.</summary>
/// <remarks>
/// A bug on the caller's side, and it creates no owner effect. The earlier order under that id is untouched;
/// this request was not placed. Use a new operation id for a new order.
/// </remarks>
public sealed class IdempotencyConflictException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>A request-rate limit was reached. Wait, then send again.</summary>
/// <remarks>
/// <c>rate_limited</c> only: a limit Anis staff set on this application or wallet (all requests, orders
/// only, or reveals only), or the gateway's own protection. Honour <see cref="AnisApiException.RetryAfter"/>
/// when present; otherwise back off exponentially. On an order nothing was placed, so the same operation
/// id may be sent again once the wait is over.
/// </remarks>
public sealed class RateLimitedException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>An owner spending allowance is used up. Do NOT poll.</summary>
/// <remarks>
/// <c>owner_limit_exceeded</c> and <c>daily_limit_exceeded</c>: the account owner's own allowance, not a
/// request rate. There is no reset time, so waiting and retrying does not help. The order is closed and
/// nothing was charged; place a new order under a new operation id only once you know the allowance changed.
/// </remarks>
public sealed class LimitExceededException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>Authentication failed.</summary>
/// <remarks>
/// <c>invalid_credentials</c>. A revoked key, an unknown key and an invalid signature are deliberately
/// indistinguishable. If a signature will not verify, the signature diagnostic route reports exactly what the
/// gateway saw; a host clock that is badly wrong is a common cause. <c>signature_expired</c> is in the catalogue
/// and maps here too, but Anis does not send it today.
/// </remarks>
public sealed class InvalidCredentialsException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>Anis had already seen this request's nonce.</summary>
/// <remarks>
/// The SDK signs every request with a fresh nonce, so this means the SAME signed bytes arrived twice — a
/// proxy or a transport retry resent them. Nothing was done for THIS copy, but the copy that arrived first
/// was admitted: on an order it may have bought the cards, so <see cref="AnisApiException.OrderOutcome"/> is
/// <see cref="OrderRefusalOutcome.Unknown"/>. Resume with the SAME operation id — the SDK signs again with a
/// new nonce, and Anis answers with the order's real state. Never a new id.
/// </remarks>
public sealed class ReplayDetectedException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>The application's current policy or the owner's state does not allow this call.</summary>
/// <remarks>
/// A missing scope or a source address outside the application's allowed networks — both answered as
/// <c>insufficient_scope</c> on purpose, so the refusal never says which to work around — an address Anis's own
/// edge refuses outright (<c>source_ip_not_allowed</c>), the owner account, wallet or subscription (<c>binding_not_authorized</c>,
/// <c>account_inactive</c>, <c>business_subscription_required</c>, <c>wallet_disabled</c>,
/// <c>wallet_expired</c>), or an owner business rule (<c>purchase_not_allowed</c>, <c>reveal_not_allowed</c>).
/// None of these change by retrying; they need a change on Anis's or the account owner's side. On an order,
/// nothing was placed.
/// </remarks>
public sealed class AuthorizationException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>The resource does not exist, or existence itself is not disclosed.</summary>
/// <remarks>
/// Includes <c>wallet_not_granted</c>: the wallet is not granted to this application, which is reported
/// exactly like a wallet that does not exist.
/// </remarks>
public sealed class ResourceNotFoundException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>The request did not satisfy the contract.</summary>
/// <remarks>
/// <c>validation_failed</c> and <c>currency_not_supported</c>. The refusal never names the failing field —
/// by design, so it cannot be used to probe for valid card ids or prices. See the errors guide for what
/// triggers it.
/// </remarks>
public sealed class ValidationFailedException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>Anis could not reach a decision: a dependency was unavailable, timed out, or failed internally.</summary>
/// <remarks>
/// <c>dependency_unavailable</c>, <c>request_timeout</c> and <c>internal_error</c>. For a read, retry after
/// a short wait. For an order this is NOT a failure: the purchase may still complete, and the only correct
/// move is to resume it with the same operation id.
/// </remarks>
public sealed class DependencyUnavailableException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>An enrollment step was refused.</summary>
/// <remarks>
/// <c>invitation_invalid</c> (unknown, used or expired invitation or token — ask Anis for a new invitation),
/// <c>challenge_expired</c> (the proof names a challenge generation that is no longer current: Anis staff restarted
/// the enrollment — enrol again with the new invitation), <c>key_proof_invalid</c> (the submitted public key is not a
/// usable P-256 public key) and <c>key_duplicate</c> (the key is not waiting for this step — usually the invitation
/// has already taken a key, for instance when a submission is sent again after its answer was lost; ask Anis staff
/// to restart the enrollment). A proof that does not verify is not a refusal: it comes back with
/// <c>ProofState</c> <c>"failed"</c>.
/// </remarks>
public sealed class EnrollmentRefusedException(Problem problem, HttpStatusCode status, TimeSpan? retryAfter = null, bool isReplayed = false)
    : AnisApiException(problem, status, retryAfter, isReplayed);

/// <summary>Which order refusals leave the purchase open.</summary>
/// <remarks>
/// Mirrors the gateway's own split (<c>PartnerUpstreamMapper</c>): every owner business refusal is a final
/// verdict that closes the operation, and every refusal at admission happens before an operation exists.
/// Only an answer that never reached an owner decision — an unavailable dependency, a timeout, an internal
/// error — leaves the operation open.
///
/// <c>replay_detected</c> is the one admission refusal that is open too. It refuses THIS copy because an
/// identical signed copy arrived first — and that copy passed admission and may have placed the order.
/// The catalogue says the same: keep the idempotency key for the same intent. A code this SDK version does
/// not know is treated as open, because resuming is always safe and a new id is not.
/// </remarks>
internal static class OrderRefusals
{
    public static OrderRefusalOutcome OutcomeOf(PartnerErrorCode code) => code switch
    {
        PartnerErrorCode.DependencyUnavailable
            or PartnerErrorCode.RequestTimeout
            or PartnerErrorCode.InternalError
            or PartnerErrorCode.OperationProcessing
            or PartnerErrorCode.ReplayDetected
            or PartnerErrorCode.Unknown => OrderRefusalOutcome.Unknown,
        _ => OrderRefusalOutcome.NotPlaced,
    };
}

/// <summary>Builds the right exception for a refusal.</summary>
internal static class AnisApiExceptionFactory
{
    private const string IdempotencyReplayedHeader = "Idempotency-Replayed";

    /// <summary>Reads the problem and the covered semantic headers of a verified refusal.</summary>
    public static AnisApiException Create(byte[] body, HttpResponseMessage response, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(response);

        var replayed = response.Headers.TryGetValues(IdempotencyReplayedHeader, out var values)
            && values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        return Create(body, response.StatusCode, RetryAfterOf(response), replayed, options);
    }

    public static AnisApiException Create(byte[] body, HttpStatusCode status, TimeSpan? retryAfter, bool replayed, JsonSerializerOptions options)
    {
        Problem problem;

        try
        {
            problem = JsonSerializer.Deserialize<Problem>(body, options) ?? Fallback(status);
        }
        catch (JsonException)
        {
            // A signed response whose body is not a problem is still a refusal. Inventing a code would be
            // worse than saying so.
            problem = Fallback(status);
        }

        return PartnerErrorCodes.Parse(problem.Code) switch
        {
            PartnerErrorCode.InsufficientBalance => new InsufficientBalanceException(problem, status, retryAfter, replayed),
            PartnerErrorCode.PriceChanged => new PriceChangedException(problem, status, retryAfter, replayed),
            PartnerErrorCode.QuantityUnavailable or PartnerErrorCode.CardUnavailable
                => new OutOfStockException(problem, status, retryAfter, replayed),
            PartnerErrorCode.IdempotencyConflict => new IdempotencyConflictException(problem, status, retryAfter, replayed),
            PartnerErrorCode.RateLimited => new RateLimitedException(problem, status, retryAfter, replayed),
            PartnerErrorCode.OwnerLimitExceeded or PartnerErrorCode.DailyLimitExceeded
                => new LimitExceededException(problem, status, retryAfter, replayed),
            PartnerErrorCode.InvalidCredentials or PartnerErrorCode.SignatureExpired
                => new InvalidCredentialsException(problem, status, retryAfter, replayed),
            PartnerErrorCode.ReplayDetected => new ReplayDetectedException(problem, status, retryAfter, replayed),
            PartnerErrorCode.InsufficientScope or PartnerErrorCode.SourceIpNotAllowed or PartnerErrorCode.BindingNotAuthorized
                or PartnerErrorCode.AccountInactive or PartnerErrorCode.BusinessSubscriptionRequired
                or PartnerErrorCode.WalletDisabled or PartnerErrorCode.WalletExpired
                or PartnerErrorCode.PurchaseNotAllowed or PartnerErrorCode.RevealNotAllowed
                => new AuthorizationException(problem, status, retryAfter, replayed),
            PartnerErrorCode.ResourceNotFound or PartnerErrorCode.CardNotFound or PartnerErrorCode.WalletNotGranted
                => new ResourceNotFoundException(problem, status, retryAfter, replayed),
            PartnerErrorCode.ValidationFailed or PartnerErrorCode.CurrencyNotSupported
                => new ValidationFailedException(problem, status, retryAfter, replayed),
            PartnerErrorCode.DependencyUnavailable or PartnerErrorCode.RequestTimeout or PartnerErrorCode.InternalError
                => new DependencyUnavailableException(problem, status, retryAfter, replayed),
            PartnerErrorCode.InvitationInvalid or PartnerErrorCode.ChallengeExpired or PartnerErrorCode.KeyProofInvalid
                or PartnerErrorCode.KeyDuplicate
                => new EnrollmentRefusedException(problem, status, retryAfter, replayed),
            _ => new AnisApiException(problem, status, retryAfter, replayed),
        };
    }

    /// <summary>The signed <c>Retry-After</c> in seconds, when present.</summary>
    public static TimeSpan? RetryAfterOf(HttpResponseMessage response)
        => response.Headers.RetryAfter?.Delta
           ?? (response.Headers.TryGetValues("Retry-After", out var values)
               && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                   ? TimeSpan.FromSeconds(seconds)
                   : null);

    /// <summary>A verified success whose body is JSON <c>null</c>: there is nothing to read, and that is not a value.</summary>
    public static AnisApiException EmptyBody(HttpStatusCode status) => new(
        new Problem { Type = "about:blank", Title = "Empty body", Status = (int)status, Code = "internal_error" },
        status);

    private static Problem Fallback(HttpStatusCode status) => new()
    {
        Type = "about:blank",
        Title = "Unreadable problem",
        Status = (int)status,
        Code = "internal_error",
    };
}
