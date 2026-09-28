using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Observability;
using Anis.Partners.Sdk.Signing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Sends every Partner request — signed, or an enrollment call — turns refusals into typed exceptions, and reports on both.</summary>
/// <remarks>
/// The one place that knows how a Partner response becomes a value or an exception, and therefore the one
/// place worth instrumenting: every call produces a span, a duration measurement and — on a refusal — one
/// warning carrying the code and the request id, which is what a partner quotes when they ask us about it.
///
/// The route TEMPLATE is what is tagged, never the concrete path. Tagging
/// <c>/v1/wallets/2f1c.../orders</c> would give a metrics backend one time series per wallet.
/// </remarks>
internal sealed class PartnerTransport(
    Func<HttpClient> http,
    AnisPartnersClientOptions options,
    ILogger<PartnerTransport>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<PartnerTransport>.Instance;

    /// <summary>The name the application was registered under.</summary>
    public string ClientName => options.ClientName;

    public Task<T> GetAsync<T>(string route, string path, SignatureProfile profile, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, route, path, profile, content: null, cancellationToken);

    /// <summary>A reveal: a nonce mutation that transmits NO body bytes.</summary>
    /// <remarks>
    /// The gateway refuses any body on a route that does not declare one, and the two reveal routes do not
    /// (<c>PartnerBoundsMiddleware</c>). The digest is then the digest of zero bytes, which the signing
    /// handler computes from the empty content it freezes.
    /// </remarks>
    public Task<T> PostWithoutBodyAsync<T>(string route, string path, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Post, route, path, SignatureProfile.BodylessNonceMutation, content: null, cancellationToken);

    /// <summary>The signature diagnostic: a nonce mutation whose body is exactly the two bytes <c>{}</c>.</summary>
    public Task<T> PostEmptyObjectAsync<T>(string route, string path, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(ContentDigest.EmptyBody.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        return SendAsync<T>(HttpMethod.Post, route, path, SignatureProfile.BodylessNonceMutation, content, cancellationToken);
    }

    /// <summary>An enrollment call: authorised by the enrollment token, not signed; the answer is still verified.</summary>
    public Task<T> SendEnrollmentAsync<T>(HttpMethod method, string route, string path, object? body, CancellationToken cancellationToken)
        => SendAsync<T>(
            method,
            route,
            path,
            profile: null,
            body is null ? null : JsonContent.Create(body, body.GetType(), options: AnisJson.Options),
            cancellationToken);

    /// <summary>An order mutation. Returns the raw response so the caller can read the outcome headers.</summary>
    public async Task<(HttpResponseMessage Response, byte[] Body)> PostOrderAsync<TBody>(
        string route,
        string path,
        TBody body,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity(HttpMethod.Post, route, operationId);
        var started = Stopwatch.GetTimestamp();

        using var request = Build(HttpMethod.Post, path, SignatureProfile.OrderMutation);

        request.Options.Set(PartnerRequestOptions.IdempotencyKey, operationId);
        request.Content = JsonContent.Create(body, options: AnisJson.Options);

        var (response, bytes) = await ExchangeAsync(request, HttpMethod.Post, route, activity, started, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var failure = Refuse(HttpMethod.Post, route, bytes, response, activity, started);

            response.Dispose();

            throw failure;
        }

        Complete(activity, HttpMethod.Post, route, response, started, errorCode: null);

        return (response, bytes);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string route,
        string path,
        SignatureProfile? profile,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity(method, route, operationId: null);
        var started = Stopwatch.GetTimestamp();

        using var request = Build(method, path, profile);
        request.Content = content;

        var (answer, bytes) = await ExchangeAsync(request, method, route, activity, started, cancellationToken).ConfigureAwait(false);

        using var response = answer;

        if (!response.IsSuccessStatusCode)
            throw Refuse(method, route, bytes, response, activity, started);

        Complete(activity, method, route, response, started, errorCode: null);

        return JsonSerializer.Deserialize<T>(bytes, AnisJson.Options)
            ?? throw AnisApiExceptionFactory.EmptyBody(response.StatusCode);
    }

    // The one place a call can end with no answer at all: a timeout, a lost connection, a cancelled call, or
    // an answer the verifying handler discarded. Each is recorded — duration, failed span, a warning — and
    // then rethrown unchanged, so the caller handles exactly the exception it would have seen anyway.
    private async Task<(HttpResponseMessage Response, byte[] Body)> ExchangeAsync(
        HttpRequestMessage request,
        HttpMethod method,
        string route,
        Activity? activity,
        long started,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        try
        {
            // A client per call: the factory hands back pooled handlers, rotated every few minutes.
            response = await http().SendAsync(request, cancellationToken).ConfigureAwait(false);

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            return (response, bytes);
        }
        catch (Exception exception)
        {
            response?.Dispose();

            Unanswered(activity, method, route, started, AnisPartnersTelemetry.NoAnswerReason(exception, cancellationToken));

            throw;
        }
    }

    private void Unanswered(Activity? activity, HttpMethod method, string route, long started, string reason)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        activity?.SetStatus(ActivityStatusCode.Error, reason);
        activity?.SetTag(AnisPartnersTelemetry.Tags.ErrorType, reason);

        AnisPartnersTelemetry.RequestDuration.Record(elapsed, new TagList
        {
            { AnisPartnersTelemetry.Tags.Client, ClientName },
            { AnisPartnersTelemetry.Tags.Route, route },
            { AnisPartnersTelemetry.Tags.Method, method.Method },
            { AnisPartnersTelemetry.Tags.ErrorType, reason },
        });

        Log.RequestUnanswered(_logger, method.Method, route, elapsed, reason);
    }

    private Activity? StartActivity(HttpMethod method, string route, Guid? operationId)
    {
        var activity = AnisPartnersTelemetry.ActivitySource.StartActivity($"anis.partners {route}", ActivityKind.Client);

        activity?.SetTag(AnisPartnersTelemetry.Tags.Client, ClientName);
        activity?.SetTag(AnisPartnersTelemetry.Tags.Route, route);
        activity?.SetTag(AnisPartnersTelemetry.Tags.Method, method.Method);

        // The caller's own identifier, not a secret — and the thing that lets one purchase be traced
        // across a partner's system and ours.
        if (operationId is { } id)
            activity?.SetTag(AnisPartnersTelemetry.Tags.OperationId, id.ToString("D"));

        return activity;
    }

    private void Complete(Activity? activity, HttpMethod method, string route, HttpResponseMessage response, long started, string? errorCode)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var requestId = response.Headers.TryGetValues("X-Request-Id", out var values) ? values.FirstOrDefault() : null;

        activity?.SetTag(AnisPartnersTelemetry.Tags.StatusCode, (int)response.StatusCode);
        activity?.SetTag(AnisPartnersTelemetry.Tags.RequestId, requestId);

        var tags = new TagList
        {
            { AnisPartnersTelemetry.Tags.Client, ClientName },
            { AnisPartnersTelemetry.Tags.Route, route },
            { AnisPartnersTelemetry.Tags.Method, method.Method },
            { AnisPartnersTelemetry.Tags.StatusCode, (int)response.StatusCode },
        };

        // The public code is a closed, low-cardinality set, so it is safe as a metric dimension.
        if (errorCode is not null)
            tags.Add(AnisPartnersTelemetry.Tags.ErrorCode, errorCode);

        AnisPartnersTelemetry.RequestDuration.Record(elapsed, tags);

        Log.RequestCompleted(_logger, method.Method, route, (int)response.StatusCode, elapsed, requestId);
    }

    private AnisApiException Refuse(HttpMethod method, string route, byte[] bytes, HttpResponseMessage response, Activity? activity, long started)
    {
        var failure = AnisApiExceptionFactory.Create(bytes, response, AnisJson.Options);

        Complete(activity, method, route, response, started, failure.RawCode);

        activity?.SetStatus(ActivityStatusCode.Error, failure.RawCode);
        activity?.SetTag(AnisPartnersTelemetry.Tags.ErrorCode, failure.RawCode);

        Log.RequestRefused(_logger, method.Method, route, failure.RawCode, (int)response.StatusCode, failure.RequestId, failure.IsRetryable, failure.IsReplayed);

        return failure;
    }

    private HttpRequestMessage Build(HttpMethod method, string path, SignatureProfile? profile)
    {
        var request = new HttpRequestMessage(method, path);

        // No profile is an enrollment call: authorised by its token, never signed.
        if (profile is { } signed)
            request.Options.Set(PartnerRequestOptions.Profile, signed);

        if (options.AcceptLanguageHeader is { } language)
            request.Headers.TryAddWithoutValidation("Accept-Language", language);

        return request;
    }
}
