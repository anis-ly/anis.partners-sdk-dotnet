namespace Anis.Partners.Sdk.Verification;

/// <summary>Thrown when a response cannot be verified. Its content is never returned to the caller.</summary>
/// <remarks>
/// The contract is explicit: clients discard a response they cannot verify. Not log-and-continue —
/// discard. So this is an exception rather than a flag on a result, because a flag is something a caller
/// forgets to read, and the body of an unverifiable response is exactly the thing that must not be acted
/// on.
///
/// The message names the rule that fired and never includes the body, the signature base or any key
/// material.
/// </remarks>
public sealed class UnverifiableResponseException(ResponseVerificationFailure failure, string detail)
    : Exception($"The Anis response could not be verified ({failure}): {detail} "
              + "Its content has been discarded and must not be used.")
{
    /// <summary>Which rule refused the response.</summary>
    public ResponseVerificationFailure Failure { get; } = failure;
}
