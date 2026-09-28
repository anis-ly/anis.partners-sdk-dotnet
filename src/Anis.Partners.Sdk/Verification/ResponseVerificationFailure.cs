namespace Anis.Partners.Sdk.Verification;

/// <summary>Why a response was refused. Closed: every value maps to a conformance-corpus rejection reason.</summary>
public enum ResponseVerificationFailure
{
    /// <summary>The response carried no <c>Signature</c> or no <c>Signature-Input</c>.</summary>
    SignatureMissing = 1,

    /// <summary>The headers could not be parsed, or the signature was not 64 bytes of P1363.</summary>
    SignatureMalformed = 2,

    /// <summary>The signature did not verify over the rebuilt base.</summary>
    SignatureInvalid = 3,

    /// <summary>The <c>Content-Digest</c> header does not describe the body that arrived.</summary>
    ContentDigestMismatch = 4,

    /// <summary>The advertised component list is not the one the frozen profile produces for this response.</summary>
    CoveredComponentsMismatch = 5,

    /// <summary>The <c>keyid</c> names a version absent from the published document.</summary>
    UnknownKey = 6,

    /// <summary>The published document carries a private member and is refused as a whole.</summary>
    KeyRejected = 7,

    /// <summary>The <c>alg</c> parameter is not the one supported algorithm.</summary>
    AlgorithmNotSupported = 8,

    /// <summary>The signature label is not <c>sig1</c>, or the two headers disagree on it.</summary>
    LabelUnexpected = 9,

    /// <summary>The <c>created</c> parameter is outside the freshness window.</summary>
    CreatedOutOfWindow = 10,
}
