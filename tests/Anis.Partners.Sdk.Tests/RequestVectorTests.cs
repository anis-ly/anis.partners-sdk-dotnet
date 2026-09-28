using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The gate: the SDK must reproduce every request vector's signature base byte for byte.</summary>
/// <remarks>
/// These vectors were cross-validated when they were generated — Anis's own parser accepted them, its
/// own base builder rebuilt identical bytes and its own verifier accepted the signature. So a failure here
/// is not "the vectors and the SDK disagree", it is "the SDK no longer signs what the gateway verifies".
///
/// The signature VALUE is never compared. ECDSA is randomized: the same key over the same base produces
/// different bytes every call. What must be identical is the base; what must be true of the signature is
/// that it verifies.
/// </remarks>
public sealed class RequestVectorTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Vectors
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "vectors", "request");

            var files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "RQ-*.json").Order(StringComparer.Ordinal).ToArray()
                : [];

            // Running zero vectors and reporting green is the one outcome this suite must never produce.
            Assert.True(
                files.Length > 0,
                $"No request vectors were found under {directory}. The conformance corpus is missing: check "
                + "the ConformanceVectors property, or the vectors folder beside the tests.");

            var data = new TheoryData<string>();

            foreach (var file in files)
                data.Add(file);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task The_sdk_reproduces_the_vector_base_exactly(string path)
    {
        var vector = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).RootElement;

        var request = vector.GetProperty("request");
        var key = vector.GetProperty("key");
        var expected = vector.GetProperty("expected");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key.GetProperty("privateKeyPkcs8Base64").GetString()!), out _);

        using var signer = EcdsaP256Signer.FromEcdsa(ecdsa, Guid.Parse(key.GetProperty("keyId").GetString()!));

        var bodyBase64 = request.GetProperty("bodyBase64").GetString();
        var body = bodyBase64 is null ? null : Convert.FromBase64String(bodyBase64);

        var inputs = new SignatureInputs
        {
            // The authority is taken AS THE CALLER GAVE IT, so the SDK's own lower-casing is what is
            // under test rather than a value the fixture pre-normalized.
            Method = request.GetProperty("method").GetString()!,
            Authority = request.GetProperty("authorityAsGiven").GetString()!.ToLowerInvariant(),
            Path = request.GetProperty("path").GetString()!,
            CanonicalQuery = request.GetProperty("canonicalQuery").GetString()!,
            AnisDate = request.GetProperty("anisDate").GetString()!,
            ContentDigest = body is null ? null : ContentDigest.Of(body),
            Nonce = request.GetProperty("nonce").GetString(),
            IdempotencyKey = request.GetProperty("idempotencyKey").GetString() is { } id ? Guid.Parse(id) : null,
        };

        var signed = await new PartnerRequestSigner(signer).SignAsync(
            Enum.Parse<SignatureProfile>(vector.GetProperty("profile").GetString()!),
            inputs,
            vector.GetProperty("signature").GetProperty("created").GetInt64(),
            vector.GetProperty("signature").GetProperty("expires").GetInt64(),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected.GetProperty("signatureBaseUtf8").GetString(), Encoding.UTF8.GetString(signed.SignatureBase));
        Assert.Equal(expected.GetProperty("signatureInput").GetString(), signed.SignatureInput);
        Assert.Equal(expected.GetProperty("contentDigest").GetString(), signed.ContentDigest);

        // The value differs every run; the shape does not, and 70-to-72 bytes here would mean DER.
        var signature = Convert.FromBase64String(signed.Signature[(signed.Signature.IndexOf(':') + 1)..signed.Signature.LastIndexOf(':')]);

        Assert.Equal(64, signature.Length);
        Assert.True(
            ecdsa.VerifyData(signed.SignatureBase, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            "The signature did not verify over the base the SDK itself built.");
    }
}
