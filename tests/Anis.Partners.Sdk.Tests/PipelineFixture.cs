using System.Security.Cryptography;
using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;
using Microsoft.Extensions.Logging;

namespace Anis.Partners.Sdk.Tests;

/// <summary>A whole client over a stub: signing handler, verifying handler, the real operations.</summary>
internal sealed class PipelineFixture : IDisposable
{
    private const string ResponseKeyId = "partner-response-signing/v1-active";

    private readonly ECDsa _requestKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly EcdsaP256Signer _signer;

    public PipelineFixture(ILoggerFactory? loggers = null, IRequestSigner? signer = null)
    {
        _signer = EcdsaP256Signer.FromEcdsa(_requestKey, Guid.NewGuid());

        Stub = new SignedResponseStub(_responseKey, ResponseKeyId) { SignedAt = Now };

        var parameters = _responseKey.ExportParameters(includePrivateParameters: false);

        var verifier = new PartnerResponseVerifier(
            new StaticSigningKeySource(new PartnerSigningKeySet
            {
                Keys =
                [
                    new PartnerJwk
                    {
                        Kty = "EC",
                        Crv = "P-256",
                        Kid = ResponseKeyId,
                        X = Base64Url(parameters.Q.X!),
                        Y = Base64Url(parameters.Q.Y!),
                    },
                ],
            }),
            new FixedTimeProvider(Now),
            loggers?.CreateLogger<PartnerResponseVerifier>());

        var signing = new PartnerSigningHandler(
            new PartnerRequestSigner(signer ?? _signer),
            new FixedTimeProvider(Now),
            new FixedNonceFactory(),
            TimeSpan.FromSeconds(60),
            loggers?.CreateLogger<PartnerSigningHandler>())
        {
            InnerHandler = Stub,
        };

        Http = new HttpClient(new PartnerVerifyingHandler(verifier) { InnerHandler = signing })
        {
            BaseAddress = new Uri("https://partners.anis.ly"),
        };

        Client = new AnisPartnersClient(Http, new AnisPartnersClientOptions { Authority = Http.BaseAddress }, loggers);
    }

    public static DateTimeOffset Now => DateTimeOffset.FromUnixTimeSeconds(1789804800);

    public SignedResponseStub Stub { get; }

    public HttpClient Http { get; }

    public IAnisPartnersClient Client { get; }

    public void Dispose()
    {
        Http.Dispose();
        _signer.Dispose();
        _responseKey.Dispose();
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedNonceFactory : INonceFactory
    {
        public string Create() => "b2F1dGgtbm9uY2UtMDAx";
    }
}
