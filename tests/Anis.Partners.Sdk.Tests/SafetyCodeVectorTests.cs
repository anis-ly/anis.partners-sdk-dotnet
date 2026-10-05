using System.Text.Json;
using Anis.Partners.Sdk.Enrollment;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The gate for the safety code: every vector's key, code and entries must come out as declared.</summary>
/// <remarks>
/// The vectors were derived by an independent implementation (plain Python, no SDK code) from real public keys, so a
/// failure here means the SDK would show a code Anis staff cannot match, or would judge an entry differently from them.
/// </remarks>
public sealed class SafetyCodeVectorTests
{
    public static TheoryData<string> Vectors
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "vectors", "safety-code");

            var files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "SC-*.json").Order(StringComparer.Ordinal).ToArray()
                : [];

            Assert.True(files.Length > 0, $"No safety-code vectors were found under {directory}.");

            return new TheoryData<string>(files);
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task The_key_has_the_declared_thumbprint_and_code(string path)
    {
        var vector = await ReadAsync(path);
        var jwk = vector.GetProperty("publicJwk");

        var thumbprint = KeyThumbprint.Compute(new PartnerJwk
        {
            Kty = jwk.GetProperty("kty").GetString(),
            Crv = jwk.GetProperty("crv").GetString(),
            X = jwk.GetProperty("x").GetString(),
            Y = jwk.GetProperty("y").GetString(),
        });

        Assert.Equal(vector.GetProperty("thumbprint").GetString(), thumbprint);
        Assert.Equal(vector.GetProperty("expectedCode").GetString(), SafetyCode.FromThumbprint(thumbprint));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task Every_entry_matches_or_does_not_as_declared(string path)
    {
        var vector = await ReadAsync(path);
        var thumbprint = vector.GetProperty("thumbprint").GetString()!;
        var inputs = vector.GetProperty("inputs").EnumerateArray().ToList();

        Assert.NotEmpty(inputs);

        foreach (var input in inputs)
        {
            var entered = input.GetProperty("entered").GetString()!;

            Assert.True(
                input.GetProperty("matches").GetBoolean() == SafetyCode.Matches(entered, thumbprint),
                $"{vector.GetProperty("id").GetString()}: \"{input.GetProperty("case").GetString()}\" ({entered}) is declared "
                + $"{(input.GetProperty("matches").GetBoolean() ? "a match" : "not a match")}.");
        }
    }

    [Fact]
    public void The_manifest_lists_exactly_the_vectors_on_disk()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "vectors", "safety-code");
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))).RootElement;

        var listed = manifest.GetProperty("vectors").EnumerateArray().Select(v => v.GetProperty("id").GetString()!).Order(StringComparer.Ordinal);
        var onDisk = Directory.GetFiles(directory, "SC-*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal);

        Assert.Equal(onDisk, listed);
        Assert.Equal(onDisk.Count(), manifest.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-thumbprint")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Only_a_thumbprint_has_a_safety_code(string? thumbprint)
        => Assert.Throws<ArgumentException>(() => SafetyCode.FromThumbprint(thumbprint!));

    private static async Task<JsonElement> ReadAsync(string path)
        => JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).RootElement;
}
