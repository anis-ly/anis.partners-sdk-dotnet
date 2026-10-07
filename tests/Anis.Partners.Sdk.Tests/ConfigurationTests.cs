using System.Security.Cryptography;
using Anis.Partners.Sdk.DependencyInjection;
using Anis.Partners.Sdk.Signing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anis.Partners.Sdk.Tests;

/// <summary>
/// Registration from a settings file, the startup checks that make a bad file fail early, and a host acting
/// for several Anis applications.
/// </summary>
public sealed class ConfigurationTests
{
    [Fact]
    public void Options_bind_from_the_AnisPartners_section()
    {
        var configuration = Settings(new()
        {
            ["AnisPartners:Authority"] = "https://partners.example",
            ["AnisPartners:SignatureLifetime"] = "00:00:45",
            ["AnisPartners:AcceptLanguage"] = "English",
            ["AnisPartners:SigningKeyCacheDuration"] = "00:05:00",
            ["AnisPartners:Timeout"] = "00:00:45",
        });

        using var provider = Register(configuration.GetSection(AnisPartnersClientOptions.SectionName));

        var options = provider.GetRequiredService<AnisPartnersClientOptions>();

        Assert.Equal(new Uri("https://partners.example"), options.Authority);
        Assert.Equal(TimeSpan.FromSeconds(45), options.SignatureLifetime);
        Assert.Equal(AnisLanguage.English, options.AcceptLanguage);
        Assert.Equal(TimeSpan.FromMinutes(5), options.SigningKeyCacheDuration);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);

        // And the client resolves: the whole pipeline is buildable from the file plus a signer.
        Assert.NotNull(provider.GetRequiredService<IAnisPartnersClient>());
    }

    [Fact]
    public void Code_can_adjust_what_the_file_says()
    {
        var configuration = Settings(new() { ["AnisPartners:Authority"] = "https://partners.example" });

        using var provider = Register(
            configuration.GetSection(AnisPartnersClientOptions.SectionName),
            options => options.Timeout = TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), provider.GetRequiredService<AnisPartnersClientOptions>().Timeout);
    }

    [Theory]
    [InlineData(null, "00:01:00")]                                // no authority
    [InlineData("partners.example", "00:01:00")]                  // not absolute
    [InlineData("ftp://partners.example", "00:01:00")]            // not http(s)
    [InlineData("https://partners.example", "00:05:01")]          // beyond the contract's 300 seconds
    [InlineData("https://partners.example", "00:01:01")]          // beyond the SDK's 60-second answer window
    [InlineData("https://partners.example", "00:00:00")]          // no lifetime at all
    public void A_settings_file_that_cannot_work_fails_at_startup(string? authority, string lifetime)
    {
        var values = new Dictionary<string, string?> { ["AnisPartners:SignatureLifetime"] = lifetime };

        if (authority is not null)
            values["AnisPartners:Authority"] = authority;

        var section = Settings(values).GetSection(AnisPartnersClientOptions.SectionName);

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAnisPartners(section));
    }

    [Fact]
    public void A_registration_without_a_signer_says_so_when_the_client_is_resolved()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners(options => options.Authority = new Uri("https://partners.example"));

        using var provider = services.BuildServiceProvider();

        var failure = Assert.ThrowsAny<InvalidOperationException>(() => provider.GetRequiredService<IAnisPartnersClient>());

        Assert.Contains(nameof(IRequestSigner), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosts_own_clock_is_kept()
    {
        var hostClock = new FrozenClock();
        var services = new ServiceCollection();

        // A host that runs its own code on its own clock — every test suite that moves time does this.
        services.AddSingleton<TimeProvider>(hostClock);
        services.AddAnisPartners(options => options.Authority = new Uri("https://partners.example")).WithSigner(NewSigner());

        using var provider = services.BuildServiceProvider();

        Assert.Same(hostClock, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void The_hosts_own_nonce_source_is_kept()
    {
        var hostNonces = new RandomNonceFactory();
        var services = new ServiceCollection();

        services.AddSingleton<INonceFactory>(hostNonces);
        services.AddAnisPartners(options => options.Authority = new Uri("https://partners.example")).WithSigner(NewSigner());

        using var provider = services.BuildServiceProvider();

        Assert.Same(hostNonces, provider.GetRequiredService<INonceFactory>());
    }

    [Fact]
    public void A_named_application_never_borrows_an_unnamed_signer()
    {
        var services = new ServiceCollection();

        // A signer registered directly, as a host might for its one default application...
        services.AddSingleton<IRequestSigner>(NewSigner());

        // ...must not silently sign for a second application that forgot its own.
        services.AddAnisPartners("brand-b", options => options.Authority = new Uri("https://b.partners.example"));

        using var provider = services.BuildServiceProvider();

        var failure = Assert.ThrowsAny<InvalidOperationException>(
            () => provider.GetRequiredService<IAnisPartnersClientFactory>().GetClient("brand-b"));

        Assert.Contains("'brand-b'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_twice_without_a_name_is_refused_at_startup()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners(options => options.Authority = new Uri("https://partners.example")).WithSigner(NewSigner());

        var failure = Assert.Throws<InvalidOperationException>(
            () => services.AddAnisPartners(options => options.Authority = new Uri("https://other.example")));

        Assert.Contains("AddAnisPartners(\"<name>\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_can_be_registered_once()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://partners.example"));

        Assert.Throws<InvalidOperationException>(
            () => services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://partners.example")));
    }

    [Fact]
    public async Task Named_applications_each_sign_with_their_own_key_at_their_own_authority()
    {
        using var responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var stub = new SignedResponseStub(responseKey, ResponseKeyId)
        {
            SignedAt = DateTimeOffset.UtcNow,
            Body = """{"partner":{"id":"7c9e6679-7425-40de-944b-e07fc1f90ae7"},"application":{"id":"16fd2706-8baf-433b-82eb-8c7fada847da","scopes":[]}}""",
        };
        var wire = new RecordingWire(stub, responseKey, ResponseKeyId);

        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        var services = new ServiceCollection();

        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => wire));
        services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://a.partners.example")).WithSigner(NewSigner(brandA));
        services.AddAnisPartners("brand-b", options => options.Authority = new Uri("https://b.partners.example")).WithSigner(NewSigner(brandB));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IAnisPartnersClientFactory>();

        await factory.GetClient("brand-a").Profile.GetAsync(TestContext.Current.CancellationToken);
        await factory.GetClient("brand-b").Profile.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["brand-a", "brand-b"], factory.Names);
        Assert.Collection(
            wire.Requests,
            a => AssertSignedBy(a, "a.partners.example", brandA),
            b => AssertSignedBy(b, "b.partners.example", brandB));

        // The same clients are keyed services, for hosts that inject rather than look up.
        Assert.Same(factory.GetClient("brand-b"), provider.GetRequiredKeyedService<IAnisPartnersClient>("brand-b"));
    }

    [Fact]
    public async Task Each_named_application_verifies_with_the_keys_of_its_own_authority()
    {
        // Two Anis deployments, each signing with its own key under its own key id.
        using var keyA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var keyB = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wireA = new RecordingWire(new SignedResponseStub(keyA, "deployment-a/v1") { SignedAt = DateTimeOffset.UtcNow, Body = Order }, keyA, "deployment-a/v1");
        var wireB = new RecordingWire(new SignedResponseStub(keyB, "deployment-b/v1") { SignedAt = DateTimeOffset.UtcNow, Body = Order }, keyB, "deployment-b/v1");

        var services = new ServiceCollection();

        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new ByHost(new()
        {
            ["a.partners.example"] = wireA,
            ["b.partners.example"] = wireB,
        })));
        services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://a.partners.example")).WithSigner(NewSigner());
        services.AddAnisPartners("brand-b", options => options.Authority = new Uri("https://b.partners.example")).WithSigner(NewSigner());

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IAnisPartnersClientFactory>();

        // Each answer verifies — which it can only do against its OWN deployment's published key: brand B's
        // key id is absent from deployment A's document, so a shared or swapped key source would discard it.
        // An order read, because Anis signs its answers.
        await factory.GetClient("brand-a").Orders.GetAsync(OperationId, TestContext.Current.CancellationToken);
        await factory.GetClient("brand-b").Orders.GetAsync(OperationId, TestContext.Current.CancellationToken);

        Assert.Equal(1, wireA.KeyDocumentFetches);
        Assert.Equal(1, wireB.KeyDocumentFetches);
    }

    [Fact]
    public async Task Create_builds_a_signed_and_verified_client_without_a_container()
    {
        using var responseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var stub = new SignedResponseStub(responseKey, ResponseKeyId)
        {
            SignedAt = DateTimeOffset.UtcNow,
            Body = Order,
        };
        var wire = new RecordingWire(stub, responseKey, ResponseKeyId);
        var keyId = Guid.NewGuid();

        var anis = AnisPartnersClient.Create(
            new AnisPartnersClientOptions { Authority = new Uri("https://partners.example") },
            NewSigner(keyId),
            wire);

        await anis.Orders.GetAsync(OperationId, TestContext.Current.CancellationToken);

        // Signed with the given key at the given authority, and verified: the answer was only accepted after the
        // published key document was fetched once.
        AssertSignedBy(Assert.Single(wire.Requests), "partners.example", keyId);
        Assert.Equal(1, wire.KeyDocumentFetches);
    }

    [Fact]
    public void An_unnamed_and_a_named_application_live_side_by_side()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners(options => options.Authority = new Uri("https://partners.example")).WithSigner(NewSigner());
        services.AddAnisPartners("brand-b", options => options.Authority = new Uri("https://b.partners.example")).WithSigner(NewSigner());

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IAnisPartnersClientFactory>();

        Assert.Same(provider.GetRequiredService<IAnisPartnersClient>(), factory.GetClient(AnisPartnersClientOptions.DefaultClientName));
        Assert.NotSame(provider.GetRequiredService<IAnisPartnersClient>(), factory.GetClient("brand-b"));
    }

    [Fact]
    public void An_unknown_name_says_which_names_exist()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://partners.example")).WithSigner(NewSigner());

        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IAnisPartnersClientFactory>().GetClient("brand-z"));

        Assert.Contains("brand-a", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_application_without_a_signer_names_itself_in_the_failure()
    {
        var services = new ServiceCollection();

        services.AddAnisPartners("brand-a", options => options.Authority = new Uri("https://partners.example"));

        using var provider = services.BuildServiceProvider();

        var failure = Assert.ThrowsAny<InvalidOperationException>(
            () => provider.GetRequiredService<IAnisPartnersClientFactory>().GetClient("brand-a"));

        Assert.Contains("'brand-a'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IRequestSigner), failure.Message, StringComparison.Ordinal);
    }

    private const string ResponseKeyId = "partner-response-signing/v1-active";

    private static readonly Guid OperationId = Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34");

    // An order read's answer: one Anis signs, so the client must verify it.
    private const string Order = """{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","status":"completed"}""";

    private static EcdsaP256Signer NewSigner(Guid? keyId = null)
        => EcdsaP256Signer.FromEcdsa(ECDsa.Create(ECCurve.NamedCurves.nistP256), keyId ?? Guid.NewGuid());

    private static void AssertSignedBy(WireRequest request, string host, Guid keyId)
    {
        Assert.Equal(host, request.Uri.Host);
        Assert.Contains($";keyid=\"{keyId:D}\"", Assert.Single(request.Headers["Signature-Input"]), StringComparison.Ordinal);
    }

    /// <summary>A primary handler that sends each request to the wire of its host.</summary>
    private sealed class ByHost(Dictionary<string, RecordingWire> wires) : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpMessageInvoker> _invokers =
            wires.ToDictionary(pair => pair.Key, pair => new HttpMessageInvoker(pair.Value, disposeHandler: false));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _invokers[request.RequestUri!.Host].SendAsync(request, cancellationToken);
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private static IConfiguration Settings(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider Register(IConfiguration section, Action<AnisPartnersClientOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services
            .AddAnisPartners(section, configure)
            .WithSigner(EcdsaP256Signer.FromEcdsa(
                System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256),
                Guid.NewGuid()));

        return services.BuildServiceProvider();
    }
}
