namespace Anis.Partners.Sdk.Signing;

/// <summary>Signs one request under one profile. Pure: every input is supplied, nothing is generated here.</summary>
/// <remarks>
/// Purity is the point. The conformance corpus pins the nonce, the date and the created/expires window, so
/// the part of signing that must be byte-exact is the part with no clock and no randomness in it. The
/// <see cref="PartnerSigningHandler"/> is what turns a live request into these inputs.
/// </remarks>
internal sealed class PartnerRequestSigner(IRequestSigner signer)
{
    private readonly IRequestSigner _signer = signer ?? throw new ArgumentNullException(nameof(signer));

    /// <summary>The credential this signer signs with. Public metadata, safe to log and to tag a span with.</summary>
    public Guid KeyId => _signer.KeyId;

    /// <summary>Builds the base, signs it, and returns the headers the request must carry.</summary>
    public async ValueTask<SignedRequestHeaders> SignAsync(
        SignatureProfile profile,
        SignatureInputs inputs,
        long created,
        long expires,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var components = SignatureProfiles.For(profile);
        // The nonce travels twice — as the Nonce header and as the ;nonce parameter — and Anis requires the
        // two to be equal on a nonce-bearing route and both absent on a read.
        if ((profile is SignatureProfile.SafeRead) != (inputs.Nonce is null))
        {
            throw new ArgumentException(
                $"The {profile} profile {(profile is SignatureProfile.SafeRead ? "carries no nonce" : "requires a nonce")}.",
                nameof(inputs));
        }

        var parameters = PartnerRequestSignatureBase.Parameters(components, created, expires, _signer.KeyId, inputs.Nonce);
        var signatureBase = PartnerRequestSignatureBase.Build(components, inputs, parameters);

        var signature = await _signer.SignAsync(signatureBase, cancellationToken).ConfigureAwait(false);

        if (signature.Length != 64)
        {
            throw new InvalidOperationException(
                $"The signer returned {signature.Length} bytes. A P-256 IEEE P1363 signature is exactly 64 "
                + "(r and s, 32 each). A 70-to-72 byte value is almost certainly DER, which the gateway "
                + "refuses — pass DSASignatureFormat.IeeeP1363FixedFieldConcatenation, or convert.");
        }

        return new SignedRequestHeaders
        {
            SignatureInput = PartnerRequestSignatureBase.SignatureInputHeader(parameters),
            Signature = PartnerRequestSignatureBase.SignatureHeader(signature),
            AnisDate = inputs.AnisDate,
            ContentDigest = inputs.ContentDigest,
            Nonce = inputs.Nonce,
            IdempotencyKey = inputs.IdempotencyKey,
            SignatureBase = signatureBase,
        };
    }
}
