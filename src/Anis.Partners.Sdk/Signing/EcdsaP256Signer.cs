using System.Security.Cryptography;

namespace Anis.Partners.Sdk.Signing;

/// <summary>An <see cref="IRequestSigner"/> over a P-256 key held in this process.</summary>
/// <remarks>
/// Suitable for development and for deployments whose key custody is a file the host protects. For custody
/// that keeps the key out of the process entirely — a vault, an HSM, a signing service — implement
/// <see cref="IRequestSigner"/> against whatever you already use. The SDK deliberately ships no
/// vendor-specific signer: your key custody is your choice, and a package that named one would prescribe a
/// tool and drag its dependency chain through every partner who uses a different one.
///
/// .NET's <see cref="ECDsa.SignData(byte[], HashAlgorithmName, DSASignatureFormat)"/> is given the P1363
/// format EXPLICITLY rather than relying on a default. The DER overload exists on the same type and
/// produces a signature that looks correct, is mathematically valid, and is refused by the gateway.
/// </remarks>
public sealed class EcdsaP256Signer : IRequestSigner, IDisposable
{
    private readonly ECDsa _ecdsa;

    private EcdsaP256Signer(ECDsa ecdsa, Guid keyId)
    {
        _ecdsa = ecdsa;
        KeyId = keyId;

        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value
            && !string.Equals(parameters.Curve.Oid.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The Anis Partner API accepts NIST P-256 keys only. There is no algorithm negotiation, so a "
                + "key on another curve cannot be used and this is reported here rather than as a 401 later.",
                nameof(ecdsa));
        }
    }

    /// <inheritdoc/>
    public Guid KeyId { get; }

    /// <summary>Loads a PKCS#8 private key from PEM text.</summary>
    public static EcdsaP256Signer FromPem(string pem, Guid keyId)
    {

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);

        return new EcdsaP256Signer(ecdsa, keyId);
    }

    /// <summary>Loads a PKCS#8 private key from a PEM file on disk.</summary>
    public static EcdsaP256Signer FromPemFile(string path, Guid keyId)
        => FromPem(File.ReadAllText(path), keyId);

    /// <summary>Adopts an already-configured <see cref="ECDsa"/>. The signer takes ownership and disposes it.</summary>
    public static EcdsaP256Signer FromEcdsa(ECDsa ecdsa, Guid keyId)
    {
        ArgumentNullException.ThrowIfNull(ecdsa);

        return new EcdsaP256Signer(ecdsa, keyId);
    }

    /// <inheritdoc/>
    public ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signatureBase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_ecdsa.SignData(
            signatureBase.Span,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <inheritdoc/>
    public void Dispose() => _ecdsa.Dispose();
}
