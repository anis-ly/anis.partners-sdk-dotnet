using Microsoft.Extensions.Logging;

namespace Anis.Partners.Sdk.Observability;

/// <summary>Every log the SDK writes, in one place.</summary>
/// <remarks>
/// Source-generated, so the message templates are fixed at compile time and a caller cannot interpolate a
/// secret into one by accident. One place, so "does this SDK ever log X?" is a question you answer by
/// reading a single file.
///
/// Levels are chosen for a partner's on-call, not for our debugging: Debug for the normal path,
/// Warning for a refusal Anis returned, and Error for a response that could not be verified — which is the
/// only one of the three that means something is wrong with the channel rather than with the request.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Anis request signed: {Method} {Path} under {Profile} with key {KeyId}.")]
    public static partial void RequestSigned(ILogger logger, string method, string path, string profile, Guid keyId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Anis {Method} {Route} returned {StatusCode} in {ElapsedMs}ms (request {RequestId}).")]
    public static partial void RequestCompleted(ILogger logger, string method, string route, int statusCode, double elapsedMs, string? requestId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Anis refused {Method} {Route}: {Code} ({StatusCode}), request {RequestId}, retryable {Retryable}, replayed {Replayed}.")]
    public static partial void RequestRefused(ILogger logger, string method, string route, string? code, int statusCode, string? requestId, bool retryable, bool replayed);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Anis response DISCARDED, unverifiable: {Failure}. Its content was not used. "
                + "This is a channel or clock problem, not a request problem.")]
    public static partial void ResponseDiscarded(ILogger logger, string failure);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Anis order {OperationId} outcome: {Outcome}.")]
    public static partial void OrderOutcome(ILogger logger, Guid operationId, string outcome);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Fetched the Anis signing-key document ({Reason}): {KeyCount} published key versions.")]
    public static partial void SigningKeysFetched(ILogger logger, string reason, int keyCount);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Anis signed a response with key {KeyId}, which this client had not seen; refreshing the key document once.")]
    public static partial void UnknownSigningKey(ILogger logger, string keyId);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Anis {Method} {Route} ended without a usable answer after {ElapsedMs}ms: {Reason}.")]
    public static partial void RequestUnanswered(ILogger logger, string method, string route, double elapsedMs, string reason);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "Anis order {OperationId} outcome UNKNOWN ({Reason}): resume it with the same operation id, never a new one.")]
    public static partial void OrderOutcomeUnknown(ILogger logger, Guid operationId, string reason);
}
