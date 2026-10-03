using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Operations;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Enrollment;

/// <summary>Bootstrap: turns an invitation into a working signing credential.</summary>
/// <remarks>
/// A separate client with separate authentication, because enrollment happens BEFORE a credential exists.
/// Its routes carry <c>Authorization: Enrollment {token}</c> and no message signature, which is also why
/// their responses omit the <c>;req</c> binding — there is no request signature to bind to.
///
/// The flow: read the invitation, submit the PUBLIC half of a new P-256 key, prove possession of the
/// private half with <see cref="ProveAsync(EnrollmentKeyResult, ECDsa, CancellationToken)"/>, then wait for
/// Anis staff to record and confirm the key's fingerprint — at which point <see cref="GetStatusAsync"/>
/// reports <c>active</c> and the key signs requests. The private half never leaves the caller.
///
/// Run once, by a human or a deployment step, and dispose the client afterwards.
/// </remarks>
public sealed class AnisEnrollmentClient : IDisposable
{
    // Changing this string invalidates every proof: it is the domain separator Anis verifies under.
    private const string ProofDomainSeparator = "anis.partners.v2.credential-proof";

    private const int P1363SignatureBytes = 64;

    private readonly HttpClient _http;
    private readonly PartnerTransport _transport;
    private readonly IDisposable? _ownedKeyClient;
    private readonly Guid _invitationId;

    private AnisEnrollmentClient(HttpClient http, PartnerTransport transport, Guid invitationId, IDisposable? ownedKeyClient)
    {
        _http = http;
        _transport = transport;
        _invitationId = invitationId;
        _ownedKeyClient = ownedKeyClient;
    }

    /// <summary>Creates a client for one invitation.</summary>
    /// <param name="authority">The authority Anis issued.</param>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="enrollmentToken">
    /// The single-use token. At least 256 bits of entropy, valid until the invitation expires (a day by
    /// default — the invitation's <c>expiresAt</c> says exactly), stored by Anis only as a SHA-256 and never
    /// logged — treat it the same way. Once the public key is submitted, the proof must follow before the
    /// challenge expires (30 minutes by default).
    /// </param>
    /// <param name="timeProvider">Clock used for response freshness. Defaults to the system clock.</param>
    /// <remarks>
    /// Enrollment responses ARE signed, even though enrollment REQUESTS are not — they simply carry no
    /// <c>;req</c> binding. So this client verifies them like any other: the key submission response carries
    /// the challenge and the <c>keyId</c> a partner will then sign with for a year, which makes it the last
    /// response anyone should accept unverified.
    /// </remarks>
    public static AnisEnrollmentClient Create(
        Uri authority,
        Guid invitationId,
        string enrollmentToken,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentToken);

        var clock = timeProvider ?? TimeProvider.System;

        // The published key document is the one genuinely unsigned route, so it is fetched by a client with
        // no verifying handler — verifying it would need the keys it is being fetched to supply.
        var keyClient = new HttpClient { BaseAddress = authority };
        var keys = new HttpSigningKeySource(keyClient, TimeSpan.FromMinutes(10), clock);

        return Build(authority, invitationId, enrollmentToken, keys, clock, keyClient, inner: null);
    }

    /// <summary>Creates a client over a key source the host already has.</summary>
    public static AnisEnrollmentClient Create(
        Uri authority,
        Guid invitationId,
        string enrollmentToken,
        ISigningKeySource keys,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentToken);
        ArgumentNullException.ThrowIfNull(keys);

        return Build(authority, invitationId, enrollmentToken, keys, timeProvider ?? TimeProvider.System, ownedKeyClient: null, inner: null);
    }

    /// <summary>Creates a client whose requests leave through <paramref name="innerHandler"/>.</summary>
    /// <remarks>
    /// For hosts that route outbound traffic through their own handler (a proxy, a test stub). Response
    /// verification still wraps it and cannot be removed.
    /// </remarks>
    public static AnisEnrollmentClient Create(
        Uri authority,
        Guid invitationId,
        string enrollmentToken,
        ISigningKeySource keys,
        HttpMessageHandler innerHandler,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentToken);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(innerHandler);

        return Build(authority, invitationId, enrollmentToken, keys, timeProvider ?? TimeProvider.System, ownedKeyClient: null, innerHandler);
    }

    /// <summary>Reads the invitation.</summary>
    public Task<EnrollmentState> GetAsync(CancellationToken cancellationToken = default)
        => _transport.SendEnrollmentAsync<EnrollmentState>(HttpMethod.Get, "/v1/enrollments/{invitationId}", $"v1/enrollments/{_invitationId:D}", null, cancellationToken);

    /// <summary>Reads where enrollment stands.</summary>
    /// <remarks>
    /// <c>state</c> is <c>pendingProof</c> until possession is proved, then <c>pendingApproval</c> until Anis
    /// staff record the key's fingerprint and confirm it, then <c>active</c>: from that moment the key signs
    /// requests. <c>unavailable</c> means the key is being replaced by a newer one. Once a key has ended
    /// (revoked, retired or past its end date) the read is refused with <c>resource_not_found</c>, like an
    /// unknown invitation.
    /// </remarks>
    public Task<EnrollmentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => _transport.SendEnrollmentAsync<EnrollmentStatus>(HttpMethod.Get, "/v1/enrollments/{invitationId}/status", $"v1/enrollments/{_invitationId:D}/status", null, cancellationToken);

    /// <summary>Submits the PUBLIC half of a freshly generated key and receives the challenge to sign.</summary>
    public Task<EnrollmentKeyResult> SubmitKeyAsync(EnrollmentKeyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _transport.SendEnrollmentAsync<EnrollmentKeyResult>(HttpMethod.Post, "/v1/enrollments/{invitationId}/keys", $"v1/enrollments/{_invitationId:D}/keys", request, cancellationToken);
    }

    /// <summary>Submits a possession proof built by <see cref="CreateProof(EnrollmentKeyResult, ECDsa)"/>.</summary>
    public Task<EnrollmentStatus> SubmitProofAsync(EnrollmentProofRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _transport.SendEnrollmentAsync<EnrollmentStatus>(HttpMethod.Post, "/v1/enrollments/{invitationId}/proof", $"v1/enrollments/{_invitationId:D}/proof", request, cancellationToken);
    }

    /// <summary>Proves possession of the private half of the key just submitted. One call.</summary>
    /// <param name="submitted">The answer of <see cref="SubmitKeyAsync"/>, unchanged.</param>
    /// <param name="key">The P-256 key whose public half was submitted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<EnrollmentStatus> ProveAsync(EnrollmentKeyResult submitted, ECDsa key, CancellationToken cancellationToken = default)
        => SubmitProofAsync(CreateProof(submitted, key), cancellationToken);

    /// <summary>The exact bytes a possession proof signs.</summary>
    /// <remarks>
    /// Not the challenge itself. Anis stores only a hash of the challenge, so the proof is signed over a
    /// domain-separated message built from values both sides hold, one per line with <c>\n</c> between them
    /// and none after the last: the separator <c>anis.partners.v2.credential-proof</c>, the <c>keyId</c>
    /// (lower-case, hyphenated), the challenge generation, the lower-case hex SHA-256 of the challenge's UTF-8
    /// bytes, and the key thumbprint Anis returned. Use this when the private key lives in a vault or HSM:
    /// sign these bytes with ECDSA P-256/SHA-256 in IEEE P1363 form and pass the 64 bytes to
    /// <see cref="CreateProof(EnrollmentKeyResult, ReadOnlySpan{byte})"/>.
    /// </remarks>
    public static byte[] ProofMessage(EnrollmentKeyResult submitted)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        var challenge = submitted.Challenge is { Length: > 0 } value
            ? value
            : throw new ArgumentException("The key submission result carries no challenge.", nameof(submitted));

        var thumbprint = submitted.Thumbprint is { Length: > 0 } print
            ? print
            : throw new ArgumentException("The key submission result carries no thumbprint.", nameof(submitted));

        var generation = submitted.ChallengeGeneration
            ?? throw new ArgumentException("The key submission result carries no challenge generation.", nameof(submitted));

        var challengeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge))).ToLowerInvariant();

        return Encoding.UTF8.GetBytes(string.Join(
            '\n',
            ProofDomainSeparator,
            submitted.KeyId.ToString("D"),
            generation.ToString(CultureInfo.InvariantCulture),
            challengeHash,
            thumbprint));
    }

    /// <summary>Builds the proof request, signing with a key held in this process.</summary>
    public static EnrollmentProofRequest CreateProof(EnrollmentKeyResult submitted, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.KeySize != 256)
            throw new ArgumentException("The enrollment key must be ECDSA P-256.", nameof(key));

        var signature = key.SignData(
            ProofMessage(submitted),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return CreateProof(submitted, signature);
    }

    /// <summary>Builds the proof request from a signature produced elsewhere (a vault, an HSM).</summary>
    /// <param name="submitted">The answer of <see cref="SubmitKeyAsync"/>.</param>
    /// <param name="p1363Signature">
    /// ECDSA P-256/SHA-256 over <see cref="ProofMessage"/>, as IEEE P1363 <c>r‖s</c> — exactly 64 bytes. A DER
    /// signature (70–72 bytes) is refused here rather than by Anis.
    /// </param>
    public static EnrollmentProofRequest CreateProof(EnrollmentKeyResult submitted, ReadOnlySpan<byte> p1363Signature)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        if (p1363Signature.Length != P1363SignatureBytes)
        {
            throw new ArgumentException(
                $"A P-256 proof signature is exactly {P1363SignatureBytes} bytes in IEEE P1363 form; got "
                + $"{p1363Signature.Length}. A DER-encoded signature is refused.",
                nameof(p1363Signature));
        }

        return new EnrollmentProofRequest
        {
            KeyId = submitted.KeyId,
            ChallengeGeneration = submitted.ChallengeGeneration
                ?? throw new ArgumentException("The key submission result carries no challenge generation.", nameof(submitted)),
            Signature = Base64Url.Encode(p1363Signature),
        };
    }

    /// <summary>The strict public JWK of a key, with no private member. What the submit route accepts.</summary>
    public static PartnerJwk PublicJwkOf(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var parameters = key.ExportParameters(includePrivateParameters: false);

        return new PartnerJwk
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64Url.Encode(parameters.Q.X!),
            Y = Base64Url.Encode(parameters.Q.Y!),
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _http.Dispose();
        _ownedKeyClient?.Dispose();
    }

    private static AnisEnrollmentClient Build(
        Uri authority,
        Guid invitationId,
        string enrollmentToken,
        ISigningKeySource keys,
        TimeProvider clock,
        IDisposable? ownedKeyClient,
        HttpMessageHandler? inner)
    {
        var verifying = new PartnerVerifyingHandler(new PartnerResponseVerifier(keys, clock))
        {
            InnerHandler = inner ?? new HttpClientHandler(),
        };

        var http = new HttpClient(verifying) { BaseAddress = authority };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Enrollment {enrollmentToken}");

        var transport = new PartnerTransport(() => http, new AnisPartnersClientOptions { Authority = authority });

        return new AnisEnrollmentClient(http, transport, invitationId, ownedKeyClient);
    }
}
