using System.Text.Json;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The gate for response verification: every vector must reach its declared outcome.</summary>
/// <remarks>
/// The accepted vectors were signed by the gateway's production <c>PartnerResponseSigner</c>, so a failure
/// on one means the SDK would discard real responses. The rejected vectors are single-change derivations,
/// so a failure on one names the rule that did not fire — and rejecting for the WRONG reason is a failure
/// too, because it usually means a different rule fired by accident.
/// </remarks>
public sealed class ResponseVectorTests
{
    private static readonly Dictionary<string, ResponseVerificationFailure> Reasons = new(StringComparer.Ordinal)
    {
        ["signature_missing"] = ResponseVerificationFailure.SignatureMissing,
        ["signature_malformed"] = ResponseVerificationFailure.SignatureMalformed,
        ["signature_invalid"] = ResponseVerificationFailure.SignatureInvalid,
        ["content_digest_mismatch"] = ResponseVerificationFailure.ContentDigestMismatch,
        ["covered_components_mismatch"] = ResponseVerificationFailure.CoveredComponentsMismatch,
        ["unknown_key"] = ResponseVerificationFailure.UnknownKey,
        ["key_rejected"] = ResponseVerificationFailure.KeyRejected,
        ["algorithm_not_supported"] = ResponseVerificationFailure.AlgorithmNotSupported,
        ["label_unexpected"] = ResponseVerificationFailure.LabelUnexpected,
        ["created_out_of_window"] = ResponseVerificationFailure.CreatedOutOfWindow,
    };

    public static TheoryData<string> Vectors
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "vectors", "response");

            var files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "RS-*.json").Order(StringComparer.Ordinal).ToArray()
                : [];

            Assert.True(files.Length > 0, $"No response vectors were found under {directory}.");

            var data = new TheoryData<string>();

            foreach (var file in files)
                data.Add(file);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public async Task The_verifier_reaches_the_declared_outcome(string path)
    {
        var vector = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).RootElement;
        var expected = vector.GetProperty("expected");
        var response = vector.GetProperty("response");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.GetProperty("headers").EnumerateObject())
            headers[header.Name] = header.Value.GetString()!;

        var subject = new VerifiableResponse(
            response.GetProperty("status").GetInt32(),
            headers,
            Convert.FromBase64String(response.GetProperty("bodyBase64").GetString()!),
            vector.GetProperty("request").GetProperty("signatureInput").GetString());

        var verifier = new PartnerResponseVerifier(
            new StaticSigningKeySource(vector.GetProperty("signingKeys").Deserialize<PartnerSigningKeySet>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!),
            new FixedClock(DateTimeOffset.FromUnixTimeSeconds(vector.GetProperty("verifyAt").GetInt64())));

        var act = async () => await verifier.VerifyAsync(subject, TestContext.Current.CancellationToken);

        if (expected.GetProperty("outcome").GetString() == "accept")
        {
            await act();

            return;
        }

        var reason = expected.GetProperty("reason").GetString()!;
        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(act);

        Assert.Equal(Reasons[reason], failure.Failure);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
