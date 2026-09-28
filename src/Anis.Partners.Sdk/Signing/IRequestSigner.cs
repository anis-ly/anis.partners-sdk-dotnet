namespace Anis.Partners.Sdk.Signing;

/// <summary>Produces a P-256 signature over the exact signature-base bytes.</summary>
/// <remarks>
/// The seam is this narrow deliberately. It takes bytes and returns bytes, and there is NO member that
/// hands over, exports or describes a private key — which is what lets a vault, an HSM or a signing service
/// satisfy it without the key ever entering this process. An interface that accepted a key object would be
/// an interface every partner eventually loads a PEM into.
///
/// This seam is why the SDK ships no vendor-specific signer. Key custody is the partner's decision, and
/// whatever tool you hold your key in, it signs bytes.
///
/// Implementations MUST return IEEE P1363 fixed-field concatenation: r then s, 32 bytes each, 64 total.
/// A DER-encoded value is the same mathematical signature in a different encoding and is refused by
/// Anis's verifier, because accepting both would mean one signature has two valid forms.
/// </remarks>
public interface IRequestSigner
{
    /// <summary>The credential this signer's key belongs to. Issued at enrollment.</summary>
    /// <remarks>
    /// A Guid rather than a string because the REQUEST <c>keyid</c> is a canonical UUID and Anis
    /// validates it as one. (The RESPONSE signature's keyid is a different namespace — keyName/keyVersion —
    /// and never appears here.) Typing it removes a failure that is invisible at the call site and arrives
    /// as an unexplained 401.
    /// </remarks>
    Guid KeyId { get; }

    /// <summary>Signs the base and returns exactly 64 bytes of IEEE P1363 <c>r‖s</c>.</summary>
    ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signatureBase, CancellationToken cancellationToken);
}
