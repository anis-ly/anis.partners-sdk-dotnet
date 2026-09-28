using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Anis.Partners.Sdk.Verification;

/// <summary>Strictly parses a response's <c>Signature-Input</c> into its label, component list and parameters.</summary>
/// <remarks>
/// Strict on purpose, and deliberately narrow: exactly one dictionary member, quoted identifiers, the
/// <c>;req</c> flag only where RFC 9421 puts it, and parameters in the forms the contract uses. The
/// published surface emits one signature; accepting two would mean choosing which one authenticates a
/// response, and that choice is a rule a caller gets to exploit.
/// </remarks>
internal static class SignatureInputParser
{
    public static bool TryParse(string value, [NotNullWhen(true)] out ParsedSignatureInput? parsed)
    {
        parsed = null;

        if (string.IsNullOrEmpty(value))
            return false;

        var equals = value.IndexOf('=');

        if (equals <= 0 || equals + 1 >= value.Length || value[equals + 1] != '(')
            return false;

        var label = value[..equals];
        var index = equals + 2;
        var identifiers = new List<string>();

        while (index < value.Length && value[index] != ')')
        {
            if (value[index] == ' ')
            {
                index++;
                continue;
            }

            if (value[index] != '"')
                return false;

            var end = value.IndexOf('"', index + 1);

            if (end < 0)
                return false;

            var name = value[(index + 1)..end];
            index = end + 1;

            var requestBound = false;

            if (index < value.Length && value[index] == ';')
            {
                if (!value.AsSpan(index).StartsWith(";req", StringComparison.Ordinal))
                    return false;

                requestBound = true;
                index += 4;
            }

            identifiers.Add(requestBound ? name + ";req" : name);
        }

        if (index >= value.Length || value[index] != ')')
            return false;

        index++;

        long? created = null;
        string? keyId = null;
        string? algorithm = null;

        while (index < value.Length)
        {
            if (value[index] != ';')
                return false;

            index++;

            var nameEnd = value.IndexOf('=', index);

            if (nameEnd < 0)
                return false;

            var name = value[index..nameEnd];
            index = nameEnd + 1;

            if (index < value.Length && value[index] == '"')
            {
                var end = value.IndexOf('"', index + 1);

                if (end < 0)
                    return false;

                var text = value[(index + 1)..end];
                index = end + 1;

                if (name == "keyid")
                    keyId = text;
                else if (name == "alg")
                    algorithm = text;
            }
            else
            {
                var end = index;

                while (end < value.Length && value[end] != ';')
                    end++;

                if (!long.TryParse(value[index..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    return false;

                index = end;

                if (name == "created")
                    created = number;
            }
        }

        if (created is null || keyId is null)
            return false;

        parsed = new ParsedSignatureInput(label, identifiers, created.Value, keyId, algorithm);

        return true;
    }
}

internal sealed record ParsedSignatureInput(
    string Label,
    IReadOnlyList<string> Identifiers,
    long Created,
    string KeyId,
    string? Algorithm);
