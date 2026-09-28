using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Anis.Partners.Sdk.DependencyInjection;

/// <summary>Registers Anis Partner clients.</summary>
/// <remarks>
/// One registration is one Anis application: its authority, its signing key and its own HTTP pipeline. The
/// unnamed overloads register the application most hosts have — resolve it as <see cref="IAnisPartnersClient"/>.
/// A host that acts for several applications registers each under a name and resolves them through
/// <see cref="IAnisPartnersClientFactory"/> or as a keyed service.
///
/// Each application gets two HTTP clients, on purpose. The API client carries the verifying handler wrapped
/// around the signing handler — that order matters, because after the response returns the request object
/// still holds the <c>Signature-Input</c> the signer wrote, which is what the response's <c>;req</c> binding
/// is rebuilt from. The key-document client carries NEITHER handler: the published key set is the one
/// genuinely public route, it is unsigned, and verifying it would need the very keys it is being fetched to
/// provide.
/// </remarks>
public static class ServiceCollectionExtensions
{
    private const string ApiClient = "anis-partners";
    private const string KeyClient = "anis-partners-signing-keys";

    /// <summary>Adds the application's <see cref="IAnisPartnersClient"/> and its HTTP pipeline.</summary>
    public static AnisPartnersBuilder AddAnisPartners(
        this IServiceCollection services,
        Action<AnisPartnersClientOptions> configure)
        => AddAnisPartners(services, AnisPartnersClientOptions.DefaultClientName, configure);

    /// <summary>Adds the client with its options read from configuration, e.g. <c>appsettings.json</c>.</summary>
    /// <param name="services">The container.</param>
    /// <param name="section">
    /// Usually <c>configuration.GetSection(AnisPartnersClientOptions.SectionName)</c>. It carries
    /// <c>Authority</c>, <c>SignatureLifetime</c>, <c>AcceptLanguage</c>, <c>SigningKeyCacheDuration</c> and
    /// <c>Timeout</c> — and never a private key, which reaches the client only through a signer.
    /// </param>
    /// <param name="configure">Optional adjustments applied after binding.</param>
    /// <remarks>
    /// The values are read ONCE, at registration, and validated there: a missing authority or a signature
    /// lifetime above 60 seconds fails the host at startup rather than at the first call.
    /// </remarks>
    public static AnisPartnersBuilder AddAnisPartners(
        this IServiceCollection services,
        IConfiguration section,
        Action<AnisPartnersClientOptions>? configure = null)
        => AddAnisPartners(services, AnisPartnersClientOptions.DefaultClientName, section, configure);

    /// <summary>Adds one of several applications, under a name the host chooses.</summary>
    /// <param name="services">The container.</param>
    /// <param name="name">
    /// The host's own name for this application, e.g. <c>"brand-a"</c>. It selects the client from
    /// <see cref="IAnisPartnersClientFactory"/> and tags this client's traces and metrics.
    /// </param>
    /// <param name="configure">The application's settings.</param>
    public static AnisPartnersBuilder AddAnisPartners(
        this IServiceCollection services,
        string name,
        Action<AnisPartnersClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AnisPartnersClientOptions();
        configure(options);

        return Register(services, name, options);
    }

    /// <summary>Adds one of several applications, with its options read from configuration.</summary>
    /// <param name="services">The container.</param>
    /// <param name="name">The host's own name for this application.</param>
    /// <param name="section">This application's section, e.g. <c>configuration.GetSection("AnisPartners:BrandA")</c>.</param>
    /// <param name="configure">Optional adjustments applied after binding.</param>
    public static AnisPartnersBuilder AddAnisPartners(
        this IServiceCollection services,
        string name,
        IConfiguration section,
        Action<AnisPartnersClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        var options = new AnisPartnersClientOptions();
        section.Bind(options);
        configure?.Invoke(options);

        return Register(services, name, options);
    }

    private static AnisPartnersBuilder Register(IServiceCollection services, string name, AnisPartnersClientOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        options.Validate();
        options.ClientName = name;

        Claim(services, name);

        var isDefault = name == AnisPartnersClientOptions.DefaultClientName;
        var apiClient = isDefault ? ApiClient : $"{ApiClient}:{name}";
        var keyClient = isDefault ? KeyClient : $"{KeyClient}:{name}";

        // Try: the host's own clock and nonce source win. A host that registered a clock for its own code
        // must not find ours in its place.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<INonceFactory, RandomNonceFactory>();
        services.TryAddSingleton<IAnisPartnersClientFactory, AnisPartnersClientFactory>();

        services.AddKeyedSingleton(name, options);

        services.AddHttpClient(keyClient, client =>
        {
            client.BaseAddress = options.Authority;
            client.Timeout = options.Timeout;
        });

        // Both clients are taken from the factory per call, never held: a held HttpClient keeps its
        // connections for the life of the process and never sees Anis move to a new address.
        services.AddKeyedSingleton<ISigningKeySource>(name, (provider, _) => new HttpSigningKeySource(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(keyClient),
            options.SigningKeyCacheDuration,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<HttpSigningKeySource>>()));

        services.AddKeyedSingleton(name, (provider, _) => new PartnerResponseVerifier(
            provider.GetRequiredKeyedService<ISigningKeySource>(name),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<PartnerResponseVerifier>>()));

        services.AddHttpClient(apiClient, client =>
            {
                client.BaseAddress = options.Authority;
                client.Timeout = options.Timeout;
            })
            .AddHttpMessageHandler(provider => new PartnerVerifyingHandler(
                provider.GetRequiredKeyedService<PartnerResponseVerifier>(name)))
            .AddHttpMessageHandler(provider => new PartnerSigningHandler(
                new PartnerRequestSigner(SignerFor(provider, name, isDefault)),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<INonceFactory>(),
                options.SignatureLifetime,
                provider.GetService<ILogger<PartnerSigningHandler>>()));

        services.AddKeyedSingleton<IAnisPartnersClient>(name, (provider, _) => ClientFor(provider, apiClient, options));

        // The unnamed application is also what an unkeyed resolve returns, so a host with one application
        // never meets names at all.
        if (isDefault)
        {
            services.AddSingleton(options);
            services.AddSingleton(provider => provider.GetRequiredKeyedService<ISigningKeySource>(name));
            services.AddSingleton(provider => provider.GetRequiredKeyedService<IAnisPartnersClient>(name));
        }

        return new AnisPartnersBuilder(services, name);
    }

    // A second registration under a name already taken would not make a second client: the HTTP client's
    // configuration accumulates, so it would stack a second pair of handlers onto the first and sign with
    // whichever key was registered last. Refused at startup instead.
    private static void Claim(IServiceCollection services, string name)
    {
        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(AnisPartnersRegistrations) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<AnisPartnersRegistrations>()
            .FirstOrDefault();

        if (registrations is null)
        {
            registrations = new AnisPartnersRegistrations();
            services.AddSingleton(registrations);
        }

        if (!registrations.Add(name))
        {
            throw new InvalidOperationException(
                name == AnisPartnersClientOptions.DefaultClientName
                    ? "AddAnisPartners was called twice without a name. A host acting for a second Anis application "
                      + "registers each one under its own name: AddAnisPartners(\"<name>\", ...)."
                    : $"An Anis Partner client named '{name}' is already registered. Each application needs its own name.");
        }
    }

    private static AnisPartnersClient ClientFor(IServiceProvider provider, string apiClient, AnisPartnersClientOptions options)
    {
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Built once now, so a missing signer fails when the client is resolved rather than at its first call.
        _ = factory.CreateClient(apiClient);

        return new AnisPartnersClient(() => factory.CreateClient(apiClient), options, provider.GetService<ILoggerFactory>());
    }

    private static IRequestSigner SignerFor(IServiceProvider provider, string name, bool isDefault)
        => provider.GetKeyedService<IRequestSigner>(name)
            ?? (isDefault ? provider.GetService<IRequestSigner>() : null)
            ?? throw new InvalidOperationException(
                $"No {nameof(IRequestSigner)} is registered for the Anis Partner client '{name}'. Name one with "
                + ".WithSigner(...) right after AddAnisPartners.");
}

/// <summary>Continues the registration by naming a key custody.</summary>
/// <remarks>
/// A separate step because there is no default: there is no safe guess about where a partner's private
/// key lives. A registration that never names a signer fails when the client is first resolved, with an
/// error saying no <see cref="IRequestSigner"/> is registered for it.
/// </remarks>
public sealed class AnisPartnersBuilder
{
    internal AnisPartnersBuilder(IServiceCollection services, string name)
    {
        Services = services;
        Name = name;
    }

    /// <summary>The services being configured.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The name this application was registered under.</summary>
    public string Name { get; }

    /// <summary>Signs with a key held in this process.</summary>
    public IServiceCollection WithSigner(IRequestSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);

        Services.AddKeyedSingleton(Name, signer);

        return Services;
    }

    /// <summary>Signs with a key the host resolves — a vault client, an HSM, anything.</summary>
    public IServiceCollection WithSigner(Func<IServiceProvider, IRequestSigner> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddKeyedSingleton(Name, (provider, _) => factory(provider));

        return Services;
    }
}

/// <summary>Resolves the client of one of several registered Anis applications.</summary>
/// <remarks>
/// For a host that acts for more than one application. Each name is the one given to
/// <c>AddAnisPartners(name, ...)</c>; the unnamed registration is <see cref="AnisPartnersClientOptions.DefaultClientName"/>.
/// The same clients are also keyed services: <c>[FromKeyedServices("brand-a")] IAnisPartnersClient</c>.
/// </remarks>
public interface IAnisPartnersClientFactory
{
    /// <summary>Every registered name.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>The client registered under <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">No application was registered under that name.</exception>
    IAnisPartnersClient GetClient(string name);
}

internal sealed class AnisPartnersClientFactory(IServiceProvider provider, AnisPartnersRegistrations registrations) : IAnisPartnersClientFactory
{
    public IReadOnlyCollection<string> Names => registrations.Names;

    public IAnisPartnersClient GetClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!registrations.Contains(name))
        {
            throw new InvalidOperationException(
                $"No Anis Partner client is registered under '{name}'. Registered: {string.Join(", ", registrations.Names)}.");
        }

        return provider.GetRequiredKeyedService<IAnisPartnersClient>(name);
    }
}

/// <summary>The names registered so far, so a duplicate is refused and the factory can list them.</summary>
internal sealed class AnisPartnersRegistrations
{
    private readonly List<string> _names = [];

    public IReadOnlyCollection<string> Names => _names;

    public bool Contains(string name) => _names.Contains(name, StringComparer.Ordinal);

    public bool Add(string name)
    {
        if (Contains(name))
            return false;

        _names.Add(name);

        return true;
    }
}
