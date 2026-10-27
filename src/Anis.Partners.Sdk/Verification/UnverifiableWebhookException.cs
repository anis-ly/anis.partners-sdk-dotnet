namespace Anis.Partners.Sdk.Verification;

/// <summary>A notification failed verification. Its contents must not be processed or echoed.</summary>
/// <param name="failure">The first verification rule that failed.</param>
public sealed class UnverifiableWebhookException(WebhookVerificationFailure failure)
    : Exception($"The Anis webhook could not be verified ({failure}). Its contents must not be used.")
{
    /// <summary>The first verification failure, without body or cryptographic material.</summary>
    public WebhookVerificationFailure Failure { get; } = failure;
}
