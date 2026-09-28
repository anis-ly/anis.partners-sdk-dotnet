using System.Net.Http.Json;
using Anis.Partners.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anis.Partners.Sdk.Verification;

/// <summary>Verifies every signed response, and discards any it cannot verify.</summary>
/// <remarks>
/// Wraps the signing handler, so by the time a response comes back the request object already carries the
/// <c>Signature-Input</c> this client sent — which is exactly what the <c>;req</c> binding needs.
///
/// The body is buffered before verification because the digest is taken over the whole of it. That is not
/// a cost worth avoiding: an unbuffered stream cannot be verified, and an unverified response must not be
/// read.
///
/// The public signing-key route is the one response that carries no signature; it is fetched by a client
/// that does not have this handler.
/// </remarks>
internal sealed class PartnerVerifyingHandler(PartnerResponseVerifier verifier) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // The public key document is the one unsigned branch; it opts out explicitly rather than being detected, so a
        // signed route can never silently take the unsigned path. Enrollment answers are verified like any other.
        if (request.Options.TryGetValue(PartnerResponseOptions.SkipVerification, out var skip) && skip)
            return response;

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        foreach (var header in response.Content.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        await verifier.VerifyAsync(
            new VerifiableResponse(
                (int)response.StatusCode,
                headers,
                body,
                request.Headers.TryGetValues("Signature-Input", out var sent) ? string.Join(", ", sent) : null),
            cancellationToken).ConfigureAwait(false);

        // Re-attach the verified bytes so the caller reads exactly what was verified, not a second read of
        // a stream that has already been consumed.
        var verified = new ByteArrayContent(body);

        foreach (var header in response.Content.Headers)
            verified.Headers.TryAddWithoutValidation(header.Key, header.Value);

        response.Content = verified;

        return response;
    }
}

/// <summary>How a route opts out of response verification.</summary>
internal static class PartnerResponseOptions
{
    /// <summary>
    /// Set on the one genuinely unsigned branch: the public key document. Every other answer — enrollment answers
    /// included — is verified, and there is no configuration switch that turns that off.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> SkipVerification = new("anis.partners.skip-verification");
}

/// <summary>Fetches and caches the published signing-key document.</summary>
/// <remarks>
/// Its <see cref="HttpClient"/> must NOT carry the signing or verifying handlers: the key document is the
/// one genuinely public route, it bypasses admission, and it is unsigned — verifying it would require the
/// keys it is being fetched to provide.
/// </remarks>
public sealed class HttpSigningKeySource(
    Func<HttpClient> http,
    TimeSpan cacheDuration,
    TimeProvider timeProvider,
    ILogger<HttpSigningKeySource>? logger = null) : ISigningKeySource
{
    private const string Path = ".well-known/partner-signing-keys.json";

    /// <summary>Fetches through one <see cref="HttpClient"/> for the life of this source.</summary>
    public HttpSigningKeySource(
        HttpClient http,
        TimeSpan cacheDuration,
        TimeProvider timeProvider,
        ILogger<HttpSigningKeySource>? logger = null)
        : this(Fixed(http), cacheDuration, timeProvider, logger)
    {
    }

    private readonly ILogger _logger = logger ?? NullLogger<HttpSigningKeySource>.Instance;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PartnerSigningKeySet? _cached;
    private DateTimeOffset _fetchedAt;

    /// <inheritdoc/>
    public async ValueTask<PartnerSigningKeySet> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is { } cached && timeProvider.GetUtcNow() - _fetchedAt < cacheDuration)
            return cached;

        return await FetchAsync(_cached is null ? "first-use" : "expired", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<PartnerSigningKeySet> RefreshAsync(CancellationToken cancellationToken)
        => FetchAsync("refresh", cancellationToken);

    private async ValueTask<PartnerSigningKeySet> FetchAsync(string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Path);
            request.Options.Set(PartnerResponseOptions.SkipVerification, true);

            using var response = await http().SendAsync(request, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var document = await response.Content
                .ReadFromJsonAsync<PartnerSigningKeySet>(AnisJson.Options, cancellationToken)
                .ConfigureAwait(false);

            _cached = document ?? new PartnerSigningKeySet();
            _fetchedAt = timeProvider.GetUtcNow();

            AnisPartnersTelemetry.SigningKeyFetches.Add(
                1,
                new KeyValuePair<string, object?>(AnisPartnersTelemetry.Tags.FetchReason, reason));

            Log.SigningKeysFetched(_logger, reason, _cached.Keys.Count);

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Func<HttpClient> Fixed(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return () => http;
    }
}
