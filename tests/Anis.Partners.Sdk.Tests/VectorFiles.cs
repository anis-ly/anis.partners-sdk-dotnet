using System.Text.Json;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Runs the vectors listed by each corpus manifest, with an exact disk inventory check.</summary>
internal static class VectorFiles
{
    public static string[] List(string group, string pattern)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "vectors", group);
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory, pattern).Order(StringComparer.Ordinal).ToArray() : [];
        Assert.NotEmpty(files);

        var manifestPath = Path.Combine(directory, "manifest.json");

        // The request, response and enrollment corpora may be distributed without a group manifest.
        if (!File.Exists(manifestPath))
            return files;

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var listed = manifest.RootElement.GetProperty("vectors").EnumerateArray()
            .Select(vector => vector.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();
        var onDisk = files.Select(file =>
        {
            using var vector = JsonDocument.Parse(File.ReadAllText(file));
            return vector.RootElement.GetProperty("id").GetString()!;
        }).Order(StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(listed);
        Assert.Equal(listed, onDisk);
        if (manifest.RootElement.TryGetProperty("total", out var total))
            Assert.Equal(listed.Length, total.GetInt32());

        return files;
    }
}
