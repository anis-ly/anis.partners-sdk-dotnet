using Anis.Partners.Sdk.Operations;
using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anis.Partners.Sdk;

/// <summary>The Anis Partner API.</summary>
/// <remarks>
/// Every call through this client is signed on the way out and verified on the way back. A response that
/// cannot be verified is discarded and never reaches you — that is the contract, not a setting.
/// </remarks>
public interface IAnisPartnersClient
{
    /// <summary>This application's identity and the scopes its current policy grants.</summary>
    IProfileOperations Profile { get; }

    /// <summary>The wallets this application may act on.</summary>
    IWalletOperations Wallets { get; }

    /// <summary>The published catalogue, priced for one wallet.</summary>
    ICatalogueOperations Catalogue { get; }

    /// <summary>Buying, and recovering a purchase whose outcome was never delivered.</summary>
    IOrderOperations Orders { get; }

    /// <summary>Owned cards and their credentials.</summary>
    IOwnedCardOperations OwnedCards { get; }

    /// <summary>
    /// The signature self-check. Needs <c>diagnostics:use</c>; served by every deployment and free of side
    /// effects. It is the right first call when a signature will not verify.
    /// </summary>
    IDiagnosticsOperations Diagnostics { get; }
}

/// <inheritdoc cref="IAnisPartnersClient"/>
public sealed class AnisPartnersClient : IAnisPartnersClient
{
    /// <summary>Builds a client over an already-configured <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// The <paramref name="http"/> must carry the verifying handler around the signing handler and have its
    /// <c>BaseAddress</c> set to the issued authority — exactly what <see cref="Create"/> builds.
    /// </remarks>
    internal AnisPartnersClient(HttpClient http, AnisPartnersClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(Fixed(http), options, loggerFactory)
    {
    }

    /// <summary>Builds a client that takes a fresh <see cref="HttpClient"/> for every call.</summary>
    /// <remarks>
    /// What <c>AddAnisPartners</c> uses, with <c>IHttpClientFactory.CreateClient</c>. A client built once and kept
    /// for the life of the process keeps its connections for that long too, so a change of Anis's address is
    /// never seen until a restart; one taken per call lets the factory rotate its handlers every few minutes.
    /// </remarks>
    internal AnisPartnersClient(Func<HttpClient> http, AnisPartnersClientOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        // Optional throughout: a partner who wires no logging gets a working client and silent no-ops,
        // and one who wires a factory gets every event without configuring anything else.
        var loggers = loggerFactory ?? NullLoggerFactory.Instance;

        var transport = new PartnerTransport(http, options, loggers.CreateLogger<PartnerTransport>());

        Profile = new ProfileOperations(transport);
        Wallets = new WalletOperations(transport);
        Catalogue = new CatalogueOperations(transport);
        Orders = new OrderOperations(transport, loggers.CreateLogger<OrderOperations>());
        OwnedCards = new OwnedCardOperations(transport);
        Diagnostics = new DiagnosticsOperations(transport);
    }

    /// <summary>Builds a client without dependency injection: every request signed, every answer verified.</summary>
    /// <param name="options">The application's settings. Validated here, as <c>AddAnisPartners</c> does at startup.</param>
    /// <param name="signer">The key custody that signs every request.</param>
    /// <param name="inner">
    /// Where requests finally leave — a proxy or a test stub. By default a pooled handler that renews its connections
    /// every two minutes, so new connections can resolve a changed address without a restart.
    /// </param>
    /// <param name="loggerFactory">Optional. Without it every log is a no-op and the client still works.</param>
    /// <remarks>
    /// For hosts that do not use <c>AddAnisPartners</c>, for instance a container without keyed services. It builds the
    /// same handler order: the published-key client carries no handler, and the API client verifies around signing. Build it
    /// once and keep it for the life of the process.
    /// </remarks>
    public static AnisPartnersClient Create(
        AnisPartnersClientOptions options,
        IRequestSigner signer,
        HttpMessageHandler? inner = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signer);

        options.Validate();

        var loggers = loggerFactory ?? NullLoggerFactory.Instance;
        var wire = inner ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };

        // The published key document is unsigned, so it is fetched with no handler at all: verifying it would need the
        // keys it is being fetched to provide.
        var keyClient = new HttpClient(wire, disposeHandler: false) { BaseAddress = options.Authority, Timeout = options.Timeout };
        var keys = new HttpSigningKeySource(keyClient, options.SigningKeyCacheDuration, TimeProvider.System, loggers.CreateLogger<HttpSigningKeySource>());

        var signing = new PartnerSigningHandler(
            new PartnerRequestSigner(signer),
            TimeProvider.System,
            new RandomNonceFactory(),
            options.SignatureLifetime,
            loggers.CreateLogger<PartnerSigningHandler>())
        {
            InnerHandler = wire,
        };

        var verifying = new PartnerVerifyingHandler(new PartnerResponseVerifier(keys, TimeProvider.System, loggers.CreateLogger<PartnerResponseVerifier>()))
        {
            InnerHandler = signing,
        };

        var api = new HttpClient(verifying) { BaseAddress = options.Authority, Timeout = options.Timeout };

        return new AnisPartnersClient(api, options, loggerFactory);
    }

    /// <inheritdoc/>
    public IProfileOperations Profile { get; }

    /// <inheritdoc/>
    public IWalletOperations Wallets { get; }

    /// <inheritdoc/>
    public ICatalogueOperations Catalogue { get; }

    /// <inheritdoc/>
    public IOrderOperations Orders { get; }

    /// <inheritdoc/>
    public IOwnedCardOperations OwnedCards { get; }

    /// <inheritdoc/>
    public IDiagnosticsOperations Diagnostics { get; }

    private static Func<HttpClient> Fixed(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return () => http;
    }
}
