using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Anis.Partners.Sdk.Tests;

/// <summary>
/// The wire under a container-built client: serves the published key document itself, answers every other
/// route through a <see cref="SignedResponseStub"/>, and records each request as it was at the moment it left.
/// </summary>
/// <remarks>
/// The snapshot matters: a retried request is the SAME object sent twice, so reading its headers afterwards
/// would show only the last attempt.
/// </remarks>
internal sealed class RecordingWire(SignedResponseStub stub, ECDsa responseKey, string responseKeyId) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _signed = new(stub, disposeHandler: false);

    public List<WireRequest> Requests { get; } = [];

    /// <summary>How many times the published key document was fetched from this wire.</summary>
    public int KeyDocumentFetches { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("partner-signing-keys.json", StringComparison.Ordinal))
        {
            KeyDocumentFetches++;

            return KeyDocument();
        }

        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        lock (Requests)
        {
            Requests.Add(new WireRequest(request.RequestUri, headers));
        }

        return await _signed.SendAsync(request, cancellationToken);
    }

    private HttpResponseMessage KeyDocument()
    {
        var q = responseKey.ExportParameters(includePrivateParameters: false).Q;
        var json = $$"""{"keys":[{"kty":"EC","crv":"P-256","kid":"{{responseKeyId}}","x":"{{Base64Url(q.X!)}}","y":"{{Base64Url(q.Y!)}}"}]}""";

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>One request as it left: its address and every header, request and content alike.</summary>
internal sealed record WireRequest(Uri Uri, Dictionary<string, string[]> Headers);
