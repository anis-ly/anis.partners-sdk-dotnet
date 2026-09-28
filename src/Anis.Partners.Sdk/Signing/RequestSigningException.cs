namespace Anis.Partners.Sdk.Signing;

/// <summary>The request could not be signed, so it was never sent.</summary>
/// <remarks>
/// Raised when the <see cref="IRequestSigner"/> fails — a vault or HSM that is unreachable or refuses, or a
/// signer that returns something other than a 64-byte P1363 signature. The inner exception is the signer's
/// own. Nothing reached Anis: an order under this call was NOT placed, and the call can be made again
/// (an order with the same operation id) once the signer works.
/// </remarks>
public sealed class RequestSigningException(Exception inner)
    : Exception($"The request could not be signed and was not sent: {inner?.Message}", inner);
