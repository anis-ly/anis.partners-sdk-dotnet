namespace Anis.Partners.Sdk.Signing;

/// <summary>Every header a signed Partner request carries, produced together from one component list.</summary>
internal sealed record SignedRequestHeaders
{
    /// <summary><c>Signature-Input</c>.</summary>
    public required string SignatureInput { get; init; }

    /// <summary><c>Signature</c>.</summary>
    public required string Signature { get; init; }

    /// <summary><c>X-Anis-Date</c>.</summary>
    public required string AnisDate { get; init; }

    /// <summary><c>Content-Digest</c>, on the body-bearing profiles.</summary>
    public string? ContentDigest { get; init; }

    /// <summary><c>Nonce</c>, on the nonce profiles.</summary>
    public string? Nonce { get; init; }

    /// <summary><c>Idempotency-Key</c>, on the order profile.</summary>
    public Guid? IdempotencyKey { get; init; }

    /// <summary>
    /// The exact bytes that were signed. Kept for diagnostics in tests and vector generation.
    /// NEVER log this in production: it contains the digest and every covered value, which is precisely
    /// the input needed to test candidate signatures offline.
    /// </summary>
    public required byte[] SignatureBase { get; init; }
}
