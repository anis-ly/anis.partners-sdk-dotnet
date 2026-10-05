using System.Text.Json.Serialization;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Models;

/// <summary>The invitation as the enrollment surface reports it.</summary>
public sealed record EnrollmentState
{
    /// <summary>The invitation.</summary>
    [JsonPropertyName("invitationId")] public Guid? InvitationId { get; init; }

    /// <summary>The application being enrolled.</summary>
    [JsonPropertyName("applicationId")] public Guid? ApplicationId { get; init; }

    /// <summary>
    /// <c>pendingInvitation</c>, <c>pendingPublicKey</c>, <c>pendingProof</c>, <c>pendingApproval</c>,
    /// <c>active</c> or <c>unavailable</c>.
    /// </summary>
    [JsonPropertyName("state")] public string? State { get; init; }

    /// <summary>When the invitation lapses.</summary>
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>The public key being enrolled, and the window it should be valid for.</summary>
public sealed record EnrollmentKeyRequest
{
    /// <summary>The PUBLIC half only. A private member is refused by Anis.</summary>
    [JsonPropertyName("publicJwk")] public required PartnerJwk PublicJwk { get; init; }

    /// <summary>The start of the validity window you ask for.</summary>
    /// <remarks>
    /// Anis uses only the LENGTH of the window (<see cref="ExpiresAt"/> minus this), keeps the shorter of it and
    /// the validity Anis staff set, and starts it when the key is committed. A future start date is not honoured;
    /// send the current time.
    /// </remarks>
    [JsonPropertyName("notBefore")] public required DateTimeOffset NotBefore { get; init; }

    /// <summary>The end of the validity window you ask for. See <see cref="NotBefore"/>.</summary>
    [JsonPropertyName("expiresAt")] public required DateTimeOffset ExpiresAt { get; init; }

}

/// <summary>What the gateway returns when a key is submitted: the challenge to sign.</summary>
public sealed record EnrollmentKeyResult
{
    /// <summary>The credential identifier. This becomes the request <c>keyid</c> once the key is active.</summary>
    [JsonPropertyName("keyId")] public Guid KeyId { get; init; }

    /// <summary>
    /// RFC 7638 thumbprint of the submitted key, as Anis computed it. <c>AnisEnrollmentClient.SubmitKeyAsync</c>
    /// has already checked it against the thumbprint of the key you sent, so it is the same key. You do not
    /// send it to anyone: you read the <see cref="SafetyCode"/> derived from it.
    /// </summary>
    [JsonPropertyName("thumbprint")] public string? Thumbprint { get; init; }

    /// <summary>
    /// The 16-character safety code (<c>XXXX-XXXX-XXXX-XXXX</c>) derived from your key's fingerprint. Anis staff
    /// will phone your technical contact and ask you to read it before your key goes live.
    /// </summary>
    /// <remarks>
    /// <c>SubmitKeyAsync</c> derives it from the thumbprint it has just verified against your own key
    /// (<c>SafetyCode.FromThumbprint</c>), so it never repeats a value on trust. Keep it where the person who takes
    /// that call can read it.
    /// </remarks>
    [JsonPropertyName("safetyCode")] public string? SafetyCode { get; init; }

    /// <summary>The challenge. The proof signs a message built from it — see <c>AnisEnrollmentClient.ProofMessage</c>.</summary>
    [JsonPropertyName("challenge")] public string? Challenge { get; init; }

    /// <summary>Which generation of the challenge this is; a re-issue increments it.</summary>
    [JsonPropertyName("challengeGeneration")] public int? ChallengeGeneration { get; init; }
}

/// <summary>Proof that the enrolling party holds the private half.</summary>
public sealed record EnrollmentProofRequest
{
    /// <summary>The credential the challenge was issued for.</summary>
    [JsonPropertyName("keyId")] public required Guid KeyId { get; init; }

    /// <summary>The generation being answered. An older one is refused.</summary>
    [JsonPropertyName("challengeGeneration")] public required int ChallengeGeneration { get; init; }

    /// <summary>
    /// ECDSA P-256/SHA-256 over <c>AnisEnrollmentClient.ProofMessage</c>, IEEE P1363 (64 bytes), base64url
    /// without padding (86 characters). Build it with <c>AnisEnrollmentClient.CreateProof</c>.
    /// </summary>
    [JsonPropertyName("signature")] public required string Signature { get; init; }
}

/// <summary>Where enrollment stands.</summary>
/// <remarks>
/// After a successful proof the key waits in <c>pendingApproval</c> until Anis staff verify its safety code
/// by phone and confirm it; then <see cref="State"/> is <c>active</c> and the key signs requests. That wait is a
/// control, not a queue to work around.
/// </remarks>
public sealed record EnrollmentStatus
{
    /// <summary>The credential.</summary>
    [JsonPropertyName("keyId")] public Guid? KeyId { get; init; }

    /// <summary>Current challenge generation.</summary>
    [JsonPropertyName("challengeGeneration")] public int? ChallengeGeneration { get; init; }

    /// <summary><c>pending</c>, <c>accepted</c> or <c>failed</c>.</summary>
    [JsonPropertyName("proofState")] public string? ProofState { get; init; }

    /// <summary>
    /// <c>pending</c> while staff have not yet confirmed the key, <c>approved</c> once it is active, and
    /// <c>notApplicable</c> in every other state.
    /// </summary>
    [JsonPropertyName("approvalState")] public string? ApprovalState { get; init; }

    /// <summary>
    /// <c>pendingInvitation</c>, <c>pendingProof</c>, <c>pendingApproval</c>, <c>active</c> or <c>unavailable</c> (the
    /// key is being replaced by a newer one). A revoked, retired or expired key is not reported: the read is refused
    /// with <c>resource_not_found</c>.
    /// </summary>
    [JsonPropertyName("state")] public string? State { get; init; }

    /// <summary>
    /// When the current step lapses (the invitation, while the key waits for its public half). Reported by the status
    /// read; absent on the proof answer.
    /// </summary>
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The date the key stops working, once it is active; absent before that and on the proof answer. Ask Anis staff
    /// for a replacement weeks before it: past it, every call is refused with <c>invalid_credentials</c>.
    /// </summary>
    [JsonPropertyName("keyExpiresAt")] public DateTimeOffset? KeyExpiresAt { get; init; }
}

/// <summary>What the signature self-check reports the gateway saw.</summary>
/// <remarks>The right first call when a signature will not verify: it names the exact facts the base was built from.</remarks>
public sealed record SignatureDiagnostic
{
    /// <summary>The route the request resolved to.</summary>
    [JsonPropertyName("routeId")] public string? RouteId { get; init; }

    /// <summary>The method as the gateway saw it.</summary>
    [JsonPropertyName("method")] public string? Method { get; init; }

    /// <summary>The authority as the gateway saw it.</summary>
    [JsonPropertyName("authority")] public string? Authority { get; init; }

    /// <summary>The path as the gateway saw it.</summary>
    [JsonPropertyName("path")] public string? Path { get; init; }

    /// <summary>The query as the gateway saw it, WITHOUT its leading question mark.</summary>
    [JsonPropertyName("canonicalQuery")] public string? CanonicalQuery { get; init; }

    /// <summary>The request kind the route map assigned.</summary>
    [JsonPropertyName("requestKind")] public string? RequestKind { get; init; }

    /// <summary>The scope the route requires.</summary>
    [JsonPropertyName("requiredScope")] public string? RequiredScope { get; init; }

    /// <summary>The covered components the route's profile requires.</summary>
    [JsonPropertyName("coveredComponents")] public IReadOnlyList<string> CoveredComponents { get; init; } = [];

    /// <summary>The credential the signature named.</summary>
    [JsonPropertyName("keyId")] public Guid? KeyId { get; init; }

    /// <summary>The Partner the credential resolved to.</summary>
    [JsonPropertyName("partnerId")] public Guid? PartnerId { get; init; }

    /// <summary>The application the credential resolved to.</summary>
    [JsonPropertyName("applicationId")] public Guid? ApplicationId { get; init; }

    /// <summary>The policy version that authorized it.</summary>
    [JsonPropertyName("policyVersion")] public int? PolicyVersion { get; init; }

    /// <summary>The scopes currently in effect.</summary>
    [JsonPropertyName("effectiveScopes")] public IReadOnlyList<string> EffectiveScopes { get; init; } = [];

    /// <summary>When the gateway received it.</summary>
    [JsonPropertyName("receivedAt")] public DateTimeOffset? ReceivedAt { get; init; }
}
