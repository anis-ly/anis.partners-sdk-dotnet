using System.Globalization;
using System.Runtime.CompilerServices;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Operations;

internal static class ProductPaging
{
    public static async IAsyncEnumerable<T> GetAsync<T>(
        Func<string?, CancellationToken, Task<Page<T>>> fetch,
        string? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(cursor))
            seen.Add(cursor);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetch(cursor, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            cursor = page.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                yield break;
            if (!seen.Add(cursor))
                throw new InvalidOperationException("The collection cursor did not advance.");
        }
    }

    public static string Query(string path, params (string Name, string? Value)[] values)
    {
        var query = string.Join("&", values.Where(pair => pair.Value is not null)
            .Select(pair => pair.Name + "=" + Uri.EscapeDataString(pair.Value!)));
        return query.Length == 0 ? path : path + "?" + query;
    }

    public static string? Date(DateTimeOffset? value) => value?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    public static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
    public static string? EnumValue<T>(T? value) where T : struct, Enum
    {
        if (value is null)
            return null;
        var name = value.Value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
