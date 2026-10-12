using System.Text.Json;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Runs every vector, enforcing the manifest's count and listed ids when present.</summary>
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

        // Request manifests declare the count and signing profiles without listing individual vector ids.
        if (!manifest.RootElement.TryGetProperty("vectors", out var vectors))
        {
            Assert.Equal("request", group);
            Assert.Equal(files.Length, manifest.RootElement.GetProperty("total").GetInt32());
            return files;
        }

        var listed = vectors.EnumerateArray()
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
