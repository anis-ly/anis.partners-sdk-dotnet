using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Errors;

/// <summary>A verified processing acknowledgment from a read whose full owner model is not available yet.</summary>
/// <param name="accepted">The exact processing acknowledgment.</param>
/// <param name="retryAfter">The verified retry delay, or five seconds when omitted.</param>
/// <param name="location">The verified read location, when supplied.</param>
public sealed class DirectRechargeProcessingException(DirectRechargeAccepted accepted, TimeSpan retryAfter, Uri? location)
    : Exception("The operation is processing; its full owner read is not available yet.")
{
    /// <summary>The separate three-member acknowledgment.</summary>
    public DirectRechargeAccepted Accepted { get; } = accepted;

    /// <summary>How long to wait before another read or same-id resume.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;

    /// <summary>The verified read location, when supplied; it is never followed automatically.</summary>
    public Uri? Location { get; } = location;
}
