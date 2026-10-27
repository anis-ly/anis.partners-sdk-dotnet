using System.Text;

namespace Anis.Partners.Sample;

/// <summary>
/// The innermost handler: prints request metadata after signing and can stop before sending.
/// </summary>
/// <remarks>
/// Installed as the PRIMARY handler, beneath the SDK's signing and verifying handlers, so what it prints is
/// what Anis receives, with product bodies and signatures redacted. In dry-run mode it stops before sending,
/// so nothing is spent — no nonce,
/// no limit, no money — and the caller sees <see cref="DryRunException"/>. In preview mode the reads go out
/// and only the first request that changes something is held back.
///
/// Product bodies stay private even when a caller requests secrets in the command output.
/// </remarks>
internal sealed class WireLog(bool dryRun, string? forwardedFor, bool previewMutations = false) : DelegatingHandler(new SocketsHttpHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Preview: reads go out (they change nothing and supply what the mutation needs — a price, an id),
        // and the first request that CHANGES something is printed and held back.
        var hold = dryRun || (previewMutations && request.Method != HttpMethod.Get);

        if (!string.IsNullOrWhiteSpace(forwardedFor))
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var text = new StringBuilder()
            .Append(hold ? "DRY RUN — not sent\n" : string.Empty)
            .Append("→ ").Append(request.Method).Append(' ').Append(request.RequestUri).Append('\n');

        foreach (var header in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
        {
            var value = string.Join(", ", header.Value);

            if (header.Key.Equals("Signature", StringComparison.OrdinalIgnoreCase))
                value = "<redacted>";

            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                value = value.Split(' ')[0] + " <token>";

            text.Append("  ").Append(header.Key).Append(": ").Append(value).Append('\n');
        }

        text.Append(body switch
        {
            null => "  (no body)",
            { Length: 0 } => "  (empty body: 0 bytes)",
            _ when IsProductChange(request) => "  <redacted product body>",
            _ => "  " + Encoding.UTF8.GetString(body),
        });

        Console.WriteLine(text);

        if (hold)
            throw new DryRunException();

        var response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine($"← {(int)response.StatusCode} {response.StatusCode}"
            + (response.Headers.TryGetValues("X-Request-Id", out var ids) ? $"  request {ids.First()}" : string.Empty));

        return response;
    }

    private static bool IsProductChange(HttpRequestMessage request)
        => request.Method != HttpMethod.Get && request.RequestUri!.AbsolutePath.Split('/')
            .Any(segment => segment is "direct-recharges" or "meter-charges");
}

/// <summary>Thrown by <see cref="WireLog"/> in dry-run mode instead of sending.</summary>
internal sealed class DryRunException() : Exception("Dry run: the request above was built and signed, and not sent.");
