using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Anis.Partners.Sdk.Verification;

internal static class WebhookSignatureInputParser
{
    private static readonly Regex Input = new("\\A([a-z][a-z0-9_.*-]*)=\\(([^)]*)\\)((?:;[a-z][a-z0-9_]*=(?:[0-9]+|\"[^\"\\\\\\r\\n]*\"))*)\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Component = new("\\A\"[^\"\\\\\\r\\n]+\"(?: \"[^\"\\\\\\r\\n]+\")*\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Parameter = new(";([a-z][a-z0-9_]*)=([0-9]+|\"[^\"\\\\\\r\\n]*\")", RegexOptions.CultureInvariant);

    public static bool TryParse(string value, [NotNullWhen(true)] out ParsedWebhookSignatureInput? parsed)
    {
        parsed = null;
        var match = Input.Match(value);
        if (!match.Success || !Component.IsMatch(match.Groups[2].Value))
            return false;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match parameter in Parameter.Matches(match.Groups[3].Value))
        {
            if (!parameters.TryAdd(parameter.Groups[1].Value, parameter.Groups[2].Value))
                return false;
        }
        if (parameters.Keys.Any(name => name is not ("created" or "expires" or "keyid" or "alg" or "tag"))
            || !parameters.TryGetValue("created", out var createdText)
            || !long.TryParse(createdText, NumberStyles.None, CultureInfo.InvariantCulture, out var created)
            || !Quoted(parameters, "keyid", out var keyId) || string.IsNullOrEmpty(keyId))
            return false;
        long? expires = null;
        if (parameters.TryGetValue("expires", out var expiresText))
        {
            if (!long.TryParse(expiresText, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return false;
            expires = number;
        }
        // A missing algorithm is malformed; a missing tag is a profile failure and expires has its own lifetime failure.
        if (!Quoted(parameters, "alg", out var algorithm) || algorithm is null
            || !Quoted(parameters, "tag", out var tag))
            return false;
        var identifiers = match.Groups[2].Value.Split(' ').Select(component => component[1..^1]).ToArray();
        parsed = new(match.Groups[1].Value, identifiers, created, expires, keyId, algorithm, tag);
        return true;
    }

    private static bool Quoted(Dictionary<string, string> parameters, string name, out string? value)
    {
        value = null;
        if (!parameters.TryGetValue(name, out var raw))
            return true;
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
            return false;
        value = raw[1..^1];
        return true;
    }
}

internal sealed record ParsedWebhookSignatureInput(
    string Label, IReadOnlyList<string> Identifiers, long Created, long? Expires,
    string KeyId, string? Algorithm, string? Tag);
