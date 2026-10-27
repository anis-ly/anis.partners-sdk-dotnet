using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Request binding, response profile and signing-key lifecycle boundaries.</summary>
public sealed class ResponseVerificationTests
{
    private const string KeyId = "partner-signing/active";
    private const string RequestInput = "sig1=(\"@method\");created=1789804800;keyid=\"request-key\"";
    private const string RequestSignature = "sig1=:raw-request-signature+:";
    private static readonly DateTimeOffset Now = PipelineFixture.Now;

    [Theory]
    [InlineData("active", 1, ResponseVerificationFailure.KeyNotYetValid)]
    [InlineData("next", -1, null)]
    [InlineData("next", 0, null)]
    [InlineData("next", 1, ResponseVerificationFailure.KeyNotYetValid)]
    [InlineData("retired", -1, ResponseVerificationFailure.KeyRetired)]
    [InlineData("retired", 0, null)]
    [InlineData("retired", 1, null)]
    public async Task Lifecycle_is_checked_against_signature_creation(string status, int boundaryOffset, ResponseVerificationFailure? expected)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key, status, Now.AddSeconds(boundaryOffset));
        var verifier = Verifier(document);
        var response = Response(key);

        if (expected is { } failure)
        {
            var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
                async () => await verifier.VerifyAsync(response, TestContext.Current.CancellationToken));
            Assert.Equal(failure, refused.Failure);
        }
        else
        {
            await verifier.VerifyAsync(response, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("active")]
    [InlineData("next")]
    [InlineData("retired")]
    public async Task A_key_of_any_status_is_refused_before_its_not_before(string status)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key, status, Now.AddSeconds(100));
        document = document with { Keys = [document.Keys[0] with { NotBefore = Now.AddSeconds(1) }] };

        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(document).VerifyAsync(Response(key), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.KeyNotYetValid, refused.Failure);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("next")]
    [InlineData("retired")]
    public async Task A_key_of_any_status_is_accepted_at_its_not_before(string status)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key, status, Now.AddSeconds(100));
        document = document with { Keys = [document.Keys[0] with { NotBefore = Now }] };

        await Verifier(document).VerifyAsync(Response(key), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_revoked_kid_is_compromised_even_without_a_public_key(bool includesKey)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key) with
        {
            Keys = includesKey ? Document(key).Keys : [],
            Revoked = [new RevokedSigningKey { Kid = KeyId, CompromisedAt = Now.AddDays(1) }],
        };
        var source = new RefreshingKeys(document, Document(key));
        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await new PartnerResponseVerifier(source, new FixedClock()).VerifyAsync(Response(key), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.KeyCompromised, refused.Failure);
        Assert.Equal(0, source.Refreshes);
    }

    [Fact]
    public async Task A_revocation_found_on_refresh_is_compromised()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var source = new RefreshingKeys(new PartnerSigningKeySet(), Document(key) with
        {
            Keys = [],
            Revoked = [new RevokedSigningKey { Kid = KeyId, CompromisedAt = Now }],
        });
        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await new PartnerResponseVerifier(source, new FixedClock()).VerifyAsync(Response(key), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.KeyCompromised, refused.Failure);
        Assert.Equal(1, source.Refreshes);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("next")]
    [InlineData("retired")]
    public async Task A_not_before_100_microseconds_after_creation_is_refused(string status)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key, status, Now.AddSeconds(100));
        document = document with { Keys = [document.Keys[0] with { NotBefore = Now.AddTicks(1000) }] };

        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(document).VerifyAsync(Response(key), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.KeyNotYetValid, failure.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("anis-response-v1")]
    [InlineData("another-profile")]
    public async Task Only_the_v2_tag_is_accepted(string? tag)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(Document(key)).VerifyAsync(Response(key, tag: tag), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.ProfileUnexpected, refused.Failure);
    }

    [Fact]
    public async Task A_changed_raw_request_signature_cannot_reuse_a_signed_response()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var response = Response(key) with { RequestSignature = "sig1=:different-request-signature=:" };
        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(Document(key)).VerifyAsync(response, TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.SignatureInvalid, refused.Failure);
    }

    [Fact]
    public async Task A_valid_signature_that_omits_the_request_signature_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var refused = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(Document(key)).VerifyAsync(Response(key, omitRequestSignature: true), TestContext.Current.CancellationToken));

        Assert.Equal(ResponseVerificationFailure.CoveredComponentsMismatch, refused.Failure);
    }

    [Fact]
    public async Task Enrollment_omits_both_request_components()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var response = Response(key, enrollment: true);
        await Verifier(Document(key)).VerifyAsync(response, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(";req", response.Headers["Signature-Input"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_rebuilt_profile_has_the_exact_component_order_and_tag()
    {
        var components = PartnerResponseSignatureBase.Components(201, "digest", "request-id", RequestInput,
            RequestSignature, "/order", "7", "true", "no-store");
        var expected = "(\"@status\" \"content-digest\" \"x-request-id\" \"signature-input\";req \"signature\";req"
            + " \"location\" \"retry-after\" \"idempotency-replayed\" \"cache-control\");created=1789804800;keyid=\"partner-signing/active\""
            + ";alg=\"ecdsa-p256-sha256\";tag=\"anis-response-v2\"";

        Assert.Equal(expected, PartnerResponseSignatureBase.Parameters(components, Now.ToUnixTimeSeconds(), KeyId));
        Assert.Contains("\"signature\";req: " + RequestSignature + "\n",
            Encoding.UTF8.GetString(PartnerResponseSignatureBase.Build(components, Now.ToUnixTimeSeconds(), KeyId)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_key_document_reads_lifecycle_and_revocations_without_changing_enrollment_jwks()
    {
        var document = JsonSerializer.Deserialize<PartnerSigningKeySet>(
            """{"keys":[{"kid":"partner-signing/retired","status":"retired","notBefore":"2026-09-18T00:00:00Z","retiredAt":"2026-09-19T08:00:00Z"}],"revoked":[{"kid":"partner-signing/compromised","compromisedAt":"2026-09-19T09:00:00Z"}]}""", AnisJson.Options)!;

        Assert.Equal("retired", Assert.Single(document.Keys).Status);
        Assert.Equal(Now, document.Keys[0].RetiredAt);
        Assert.Equal("partner-signing/compromised", Assert.Single(document.Revoked).Kid);
        Assert.Equal(Now.AddHours(1), document.Revoked[0].CompromisedAt);
        Assert.Null(typeof(PartnerJwk).GetProperty("Status"));
    }


    [Fact]
    public async Task An_unknown_key_refreshes_once_and_accepts_a_published_replacement()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var source = new RefreshingKeys(new PartnerSigningKeySet(), Document(key));
        await new PartnerResponseVerifier(source, new FixedClock()).VerifyAsync(Response(key), TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Refreshes);
    }

    [Fact]
    public async Task An_unknown_key_after_refresh_is_refused_without_another_fetch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var source = new RefreshingKeys(new PartnerSigningKeySet(), new PartnerSigningKeySet());
        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await new PartnerResponseVerifier(source, new FixedClock()).VerifyAsync(Response(key), TestContext.Current.CancellationToken));
        Assert.Equal(ResponseVerificationFailure.UnknownKey, failure.Failure);
        Assert.Equal(1, source.Refreshes);
    }

    [Theory]
    [InlineData("next", 1, ResponseVerificationFailure.KeyNotYetValid)]
    [InlineData("retired", -1, ResponseVerificationFailure.KeyRetired)]
    public async Task Lifecycle_preserves_fractional_boundary_instants(string status, int milliseconds, ResponseVerificationFailure expected)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(Document(key, status, Now.AddMilliseconds(milliseconds)))
                .VerifyAsync(Response(key), TestContext.Current.CancellationToken));
        Assert.Equal(expected, failure.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("retired")]
    public async Task Missing_or_invalid_lifecycle_metadata_is_a_key_rejection(string? status)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Document(key) with { Keys = [Document(key).Keys[0] with { Status = status, RetiredAt = null }] };
        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(document).VerifyAsync(Response(key), TestContext.Current.CancellationToken));
        Assert.Equal(ResponseVerificationFailure.KeyRejected, failure.Failure);
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("")]
    [InlineData("Answer")]
    public async Task A_key_published_for_another_purpose_cannot_verify_an_api_answer(string purpose)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = WithPurpose(Document(key), purpose);
        var failure = await Assert.ThrowsAsync<UnverifiableResponseException>(
            async () => await Verifier(document).VerifyAsync(Response(key), TestContext.Current.CancellationToken));
        Assert.Equal(ResponseVerificationFailure.KeyRejected, failure.Failure);
    }

    [Theory]
    [InlineData("answer")]
    [InlineData(null)]
    public async Task An_answer_purpose_or_absent_purpose_key_verifies_an_api_answer(string? purpose)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Verifier(WithPurpose(Document(key), purpose)).VerifyAsync(Response(key), TestContext.Current.CancellationToken);
    }

    private static PartnerSigningKeySet WithPurpose(PartnerSigningKeySet document, string? purpose)
        => document with { Keys = [document.Keys[0] with { Purpose = purpose }] };

    private static PartnerResponseVerifier Verifier(PartnerSigningKeySet document)
        => new(new StaticSigningKeySource(document), new FixedClock());

    private static PartnerSigningKeySet Document(ECDsa key, string status = "active", DateTimeOffset? boundary = null)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return new PartnerSigningKeySet
        {
            Keys = [new PublishedSigningKey
            {
                Kid = KeyId, Kty = "EC", Crv = "P-256", X = Base64Url.Encode(point.X!), Y = Base64Url.Encode(point.Y!),
                Status = status, NotBefore = status == "retired" ? Now.AddDays(-1) : boundary ?? Now.AddDays(-1),
                RetiredAt = status == "retired" ? boundary : null,
            }],
        };
    }

    // Independently renders the v2 base: these lifecycle tests do not sign with the SDK builder they exercise.
    private static VerifiableResponse Response(ECDsa key, string? tag = "anis-response-v2", bool omitRequestSignature = false, bool enrollment = false)
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var digest = "sha-256=:" + Convert.ToBase64String(SHA256.HashData(body)) + ":";
        var lines = new List<(string Identifier, string Value)>
        {
            ("\"@status\"", "200"), ("\"content-digest\"", digest), ("\"x-request-id\"", "request-id"),
        };
        if (!enrollment)
        {
            lines.Add(("\"signature-input\";req", RequestInput));
            if (!omitRequestSignature)
                lines.Add(("\"signature\";req", RequestSignature));
        }
        var parameters = $"({string.Join(' ', lines.Select(line => line.Identifier))});created={Now.ToUnixTimeSeconds()};keyid=\"{KeyId}\";alg=\"ecdsa-p256-sha256\""
            + (tag is null ? string.Empty : $";tag=\"{tag}\"");
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => line.Identifier + ": " + line.Value))
            + "\n\"@signature-params\": " + parameters);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new VerifiableResponse(200, new Dictionary<string, string>
        {
            ["Content-Digest"] = digest, ["X-Request-Id"] = "request-id", ["Signature-Input"] = "sig1=" + parameters,
            ["Signature"] = "sig1=:" + Convert.ToBase64String(signature) + ":",
        }, body, enrollment ? null : RequestInput, enrollment ? null : RequestSignature);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RefreshingKeys(PartnerSigningKeySet cached, PartnerSigningKeySet refreshed) : ISigningKeySource
    {
        public int Refreshes { get; private set; }
        public ValueTask<PartnerSigningKeySet> GetAsync(CancellationToken cancellationToken) => new(cached);
        public ValueTask<PartnerSigningKeySet> RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            return new(refreshed);
        }
    }
}
