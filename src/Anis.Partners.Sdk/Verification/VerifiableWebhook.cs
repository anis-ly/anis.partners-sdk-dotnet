namespace Anis.Partners.Sdk.Verification;

/// <summary>A notification as received, before JSON middleware changes its bytes.</summary>
/// <param name="Method">The actual request method.</param>
/// <param name="TargetUri">The actual absolute external request URL, including its query.</param>
/// <param name="Headers">Raw single header values, looked up case-insensitively; duplicate spellings are rejected.</param>
/// <param name="Body">The exact body bytes received.</param>
public sealed record VerifiableWebhook(string Method, string TargetUri, IReadOnlyDictionary<string, string> Headers, byte[] Body);
