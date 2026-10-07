using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Anis.Partners.Sdk.Observability;

/// <summary>The SDK's traces and metrics. Names are part of the public contract.</summary>
/// <remarks>
/// One <see cref="ActivitySource"/> and one <see cref="Meter"/>, both named <c>Anis.Partners.Sdk</c>, so a
/// host subscribes with one line each and gets everything. Nothing is emitted unless something is
/// listening — an unlistened ActivitySource and an unsubscribed Meter cost effectively nothing, which is
/// why this is always on rather than behind a flag somebody forgets to set when they need it most.
///
/// <para><b>What is never emitted, anywhere, on any signal:</b> the private key, the signature, the
/// signature base, the <c>Signature-Input</c> value, the nonce, the enrollment token, a voucher or a
/// serial number. The signature base in particular is the exact input needed to test candidate signatures
/// offline, and a telemetry pipeline is a place logs are shipped, indexed and kept.</para>
///
/// <para>The operation id IS emitted. It is the caller's own identifier, it is not a secret, and being able
/// to trace one purchase across a partner's system and Anis's is the single most useful thing this
/// telemetry does.</para>
/// </remarks>
public static class AnisPartnersTelemetry
{
    /// <summary>The name to subscribe to, for both traces and metrics.</summary>
    public const string Name = "Anis.Partners.Sdk";

    internal static readonly ActivitySource ActivitySource = new(Name, Version);

    private static readonly Meter Meter = new(Name, Version);

    /// <summary>End-to-end duration of one API call, in milliseconds.</summary>
    /// <remarks>
    /// Tagged with the client, route, method, status and — on a refusal — the public error code. A call that
    /// got no usable answer is recorded too, with <c>error.type</c> in place of a status.
    /// </remarks>
    internal static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "anis.partners.request.duration",
        unit: "ms",
        description: "Duration of an Anis Partner API call, from signing to a usable response (verified, on a route Anis signs).");

    /// <summary>Time spent building the base and signing, in milliseconds.</summary>
    /// <remarks>
    /// Separate from the request duration because it is the one part a partner controls: a vault-backed
    /// signer adds a network round trip here, and without its own measurement that cost is invisible
    /// inside the total.
    /// </remarks>
    internal static readonly Histogram<double> SignatureDuration = Meter.CreateHistogram<double>(
        "anis.partners.signature.duration",
        unit: "ms",
        description: "Time spent producing one request signature, including the signer's own work.");

    /// <summary>Responses refused by verification, by the rule that refused them.</summary>
    /// <remarks>
    /// This counter moving at all is an incident. It means something between the partner and Anis altered a
    /// response, or a clock is badly wrong, or a key rotation went unnoticed — never ordinary traffic.
    /// </remarks>
    internal static readonly Counter<long> VerificationFailures = Meter.CreateCounter<long>(
        "anis.partners.response.verification.failures",
        description: "Responses discarded because they could not be verified.");

    /// <summary>Reported order outcomes: completed, processing, replayed or unknown; final refusals are not counted.</summary>
    /// <remarks>
    /// <c>unknown</c> is an order call that ended without an outcome the partner can act on as final: no
    /// answer (a timeout, a lost connection, a cancelled call), an answer that could not be verified, a
    /// refusal that reached no decision, or a refusal of a resume that was decided before the order was looked at.
    /// Each one must be resumed with the same operation id, so it is the
    /// outcome an on-call engineer most needs to see; it carries <c>error.type</c> saying why.
    /// </remarks>
    internal static readonly Counter<long> OrderOutcomes = Meter.CreateCounter<long>(
        "anis.partners.order.outcomes",
        description: "Outcomes of order creation and resume calls.");

    /// <summary>Fetches of the published signing-key document, by why they happened.</summary>
    /// <remarks>
    /// Reasons: <c>first-use</c>, <c>expired</c> (the cache duration passed) and <c>refresh</c> (a response named a key
    /// version this client had not seen). A rising <c>refresh</c> is a rotation in progress, or a client pointed at
    /// the wrong authority.
    /// </remarks>
    internal static readonly Counter<long> SigningKeyFetches = Meter.CreateCounter<long>(
        "anis.partners.signing_keys.fetches",
        description: "Fetches of the published response-signing key document.");

    /// <summary>
    /// Why a call ended without a usable answer, as a small closed set that is safe as a metric dimension:
    /// <c>signing</c> (the partner's own signer failed; nothing was sent), <c>timeout</c>, <c>canceled</c>
    /// (the caller's own token), <c>connection</c>, <c>unverifiable</c> or <c>other</c>.
    /// </summary>
    internal static string NoAnswerReason(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        Signing.RequestSigningException => "signing",
        Verification.UnverifiableResponseException => "unverifiable",
        OperationCanceledException when cancellationToken.IsCancellationRequested => "canceled",
        OperationCanceledException => "timeout",
        HttpRequestException => "connection",
        _ => "other",
    };

    private static string Version =>
        typeof(AnisPartnersTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Tag keys, so a dashboard and the SDK cannot disagree about spelling.</summary>
    public static class Tags
    {
        /// <summary>The route template, e.g. <c>/v1/wallets/{walletId}/orders</c>.</summary>
        public const string Route = "anis.route";

        /// <summary>The HTTP method.</summary>
        public const string Method = "http.request.method";

        /// <summary>The HTTP status code.</summary>
        public const string StatusCode = "http.response.status_code";

        /// <summary>The public error code of a refusal.</summary>
        public const string ErrorCode = "anis.error.code";

        /// <summary>The <c>X-Request-Id</c> to quote when asking Anis about a call.</summary>
        public const string RequestId = "anis.request_id";

        /// <summary>The caller-owned operation id of an order. Not a secret, and the key to tracing a purchase.</summary>
        public const string OperationId = "anis.operation_id";

        /// <summary>Which covered-component profile signed the request.</summary>
        public const string SignatureProfile = "anis.signature.profile";

        /// <summary>Which rule refused a response.</summary>
        public const string VerificationFailure = "anis.verification.failure";

        /// <summary>Which order outcome occurred.</summary>
        public const string OrderOutcome = "anis.order.outcome";

        /// <summary>Why the signing-key document was fetched.</summary>
        public const string FetchReason = "anis.fetch.reason";

        /// <summary>
        /// The name the application was registered under — <c>default</c> unless the host acts for several
        /// Anis applications and named them.
        /// </summary>
        public const string Client = "anis.client";

        /// <summary>
        /// Why a call ended without a usable answer (<c>signing</c>, <c>timeout</c>, <c>canceled</c>,
        /// <c>connection</c>, <c>unverifiable</c>, <c>other</c>) — or, on an unknown order outcome, the refusal code that left it open.
        /// </summary>
        public const string ErrorType = "error.type";
    }
}
