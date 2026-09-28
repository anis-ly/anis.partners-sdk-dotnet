using System.Security.Cryptography;
using Anis.Partners.Sdk.DependencyInjection;
using Anis.Partners.Sdk.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace Anis.Partners.Sdk.Tests;

/// <summary>A long-running host must pick up fresh connections, so a change of Anis's address is seen.</summary>
/// <remarks>
/// <see cref="IHttpClientFactory"/> rotates the handler chain — and with it the connection pool — once its
/// lifetime passes, but only for clients created after that. A client created once and kept for the life of
/// the process keeps its first connections, and with them the address Anis had when the host started.
/// </remarks>
public sealed class ConnectionRotationTests
{
    private const string ResponseKeyId = "partner-response-signing/v1-active";

    [Fact]
    public async Task Calls_after_the_handler_lifetime_go_out_on_fresh_connections()
    {
        using var responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var stub = new SignedResponseStub(responseKey, ResponseKeyId)
        {
            SignedAt = DateTimeOffset.UtcNow,
            Body = """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":[]}}""",
        };
        var wire = new RecordingWire(stub, responseKey, ResponseKeyId);
        var pools = new List<CountedPool>();

        var services = new ServiceCollection();

        // Each handler chain the factory builds gets its own "connection pool", so a new one is visible.
        services.ConfigureHttpClientDefaults(http => http
            .SetHandlerLifetime(TimeSpan.FromSeconds(1))
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var pool = new CountedPool(wire);

                lock (pools)
                {
                    pools.Add(pool);
                }

                return pool;
            }));

        services
            .AddAnisPartners(options => options.Authority = new Uri("https://partners.anis.ly"))
            .WithSigner(EcdsaP256Signer.FromEcdsa(ECDsa.Create(ECCurve.NamedCurves.nistP256), Guid.NewGuid()));

        await using var provider = services.BuildServiceProvider();
        var anis = provider.GetRequiredService<IAnisPartnersClient>();

        await anis.Profile.GetAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        await anis.Profile.GetAsync(TestContext.Current.CancellationToken);

        List<CountedPool> used;

        lock (pools)
        {
            used = pools.Where(pool => pool.ProfileCalls > 0).ToList();
        }

        // Two calls either side of the lifetime, two different pools: the second call did not reuse the first.
        Assert.Equal(2, used.Count);
        Assert.All(used, pool => Assert.Equal(1, pool.ProfileCalls));
    }

    private sealed class CountedPool(RecordingWire wire) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _wire = new(wire, disposeHandler: false);

        public int ProfileCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/v1/profile")
                ProfileCalls++;

            return _wire.SendAsync(request, cancellationToken);
        }
    }
}
