namespace Anis.Partners.Sdk.Verification;

/// <summary>Supplies the published response-signing keys.</summary>
/// <remarks>
/// Separate from the verifier so rotation is the key source's problem and verification stays pure. The
/// verifier asks once; if the <c>keyid</c> is unknown it asks for a refresh exactly once more, because an
/// unknown key version is the ordinary signal that a rotation has happened — and because an unbounded
/// refresh on every unknown key turns a hostile <c>keyid</c> into a way to make a client hammer the
/// gateway.
/// </remarks>
public interface ISigningKeySource
{
    /// <summary>The cached document, fetching it if this is the first call.</summary>
    ValueTask<PartnerSigningKeySet> GetAsync(CancellationToken cancellationToken);

    /// <summary>Discards the cache and fetches again.</summary>
    ValueTask<PartnerSigningKeySet> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>An in-memory key source for the tests' fixed key documents.</summary>
internal sealed class StaticSigningKeySource(PartnerSigningKeySet keys) : ISigningKeySource
{
    /// <inheritdoc/>
    public ValueTask<PartnerSigningKeySet> GetAsync(CancellationToken cancellationToken) => new(keys);

    /// <inheritdoc/>
    public ValueTask<PartnerSigningKeySet> RefreshAsync(CancellationToken cancellationToken) => new(keys);
}
