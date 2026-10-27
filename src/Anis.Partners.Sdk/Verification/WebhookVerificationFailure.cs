namespace Anis.Partners.Sdk.Verification;

/// <summary>Webhook verification failures, independently numbered from response failures.</summary>
public enum WebhookVerificationFailure
{
    /// <summary>Signature or Signature-Input is missing.</summary>
    SignatureMissing = 1,

    /// <summary>The signature headers or P1363 bytes are malformed.</summary>
    SignatureMalformed = 2,

    /// <summary>The signature does not verify over the rebuilt base.</summary>
    SignatureInvalid = 3,

    /// <summary>The digest does not describe the raw body bytes.</summary>
    ContentDigestMismatch = 4,

    /// <summary>The covered list or a required binding header is invalid.</summary>
    CoveredComponentsMismatch = 5,

    /// <summary>The exact key version is not published after one refresh.</summary>
    UnknownKey = 6,

    /// <summary>The key or its lifecycle metadata is invalid, or the document contains private material.</summary>
    KeyRejected = 7,

    /// <summary>The mandatory algorithm is not ecdsa-p256-sha256.</summary>
    AlgorithmNotSupported = 8,

    /// <summary>The signature label is not sig1 on both headers.</summary>
    LabelUnexpected = 9,

    /// <summary>The creation time is more than 300 seconds from this clock.</summary>
    CreatedOutOfWindow = 10,

    /// <summary>The mandatory webhook profile tag is absent or incorrect.</summary>
    ProfileUnexpected = 11,

    /// <summary>The key is compromised or revoked regardless of signing date.</summary>
    KeyCompromised = 12,

    /// <summary>The webhook key is retired regardless of signing date.</summary>
    KeyRetired = 13,

    /// <summary>The signature predates the key activation.</summary>
    KeyNotYetValid = 14,

    /// <summary>The signature expiry has passed without any expiry grace.</summary>
    SignatureExpired = 15,

    /// <summary>Expires is missing or the lifetime is not exactly 300 seconds.</summary>
    ValidityWindowInvalid = 16,

    /// <summary>The actual target does not equal the trusted configured URL.</summary>
    TargetUriMismatch = 17,

    /// <summary>The signed header or envelope does not identify the configured application.</summary>
    ApplicationIdMismatch = 18,

    /// <summary>The key does not explicitly declare webhook purpose.</summary>
    KeyPurposeMismatch = 19,

    /// <summary>The actual request method is not POST.</summary>
    MethodUnexpected = 20,

    /// <summary>The envelope id does not equal the signed event id.</summary>
    EventIdMismatch = 21,

    /// <summary>The verified body is not a valid notification envelope or known data model.</summary>
    EnvelopeInvalid = 22,
}
