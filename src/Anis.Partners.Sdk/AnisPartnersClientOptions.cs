using System.ComponentModel.DataAnnotations;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk;

/// <summary>Which Arabic/English presentation the surface should use. Presentation only.</summary>
public enum AnisLanguage
{
    /// <summary>Send no preference.</summary>
    Unspecified = 0,

    /// <summary>Arabic.</summary>
    Arabic = 1,

    /// <summary>English.</summary>
    English = 2,
}

/// <summary>Everything the client needs, and deliberately nothing else.</summary>
/// <remarks>
/// There is no environment setting. No enum, no flag, no <c>.Staging</c>. Every Anis deployment is a fully
/// isolated copy with its own authority, keys and data, so which one you talk to is decided by the
/// <see cref="Authority"/> you were given and by nothing else — an SDK-level switch would be a single line
/// that repoints live traffic.
///
/// There is no private key here either. It reaches the client through an <see cref="IRequestSigner"/>, so
/// configuration files, environment variables and log dumps never contain one.
/// </remarks>
public sealed class AnisPartnersClientOptions
{
    /// <summary>The HTTPS authority Anis issued for this application.</summary>
    [Required]
    public Uri? Authority { get; set; }

    /// <summary>How long a request signature is valid: 1 to 60 seconds, 60 by default.</summary>
    /// <remarks>
    /// The contract allows up to 300 seconds, and this SDK allows 60 — the same 60 seconds within which it
    /// accepts Anis's answers. A longer window buys nothing and sets a trap: a host clock one to five minutes
    /// slow would still get an order admitted and completed, and then discard the answer that carries the
    /// card codes as too old. Capped at 60, such a clock is refused before anything is bought.
    /// </remarks>
    public TimeSpan SignatureLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The longest <see cref="SignatureLifetime"/> this SDK accepts: its own answer-freshness window.</summary>
    public static TimeSpan MaxSignatureLifetime => Verification.PartnerResponseSignatureBase.MaxAge;

    /// <summary>Optional presentation preference sent as <c>Accept-Language</c>.</summary>
    public AnisLanguage AcceptLanguage { get; set; } = AnisLanguage.Unspecified;

    /// <summary>How long the published signing-key document is cached before a scheduled re-fetch.</summary>
    /// <remarks>A rotation does not wait for this: an unknown key version triggers one immediate refresh.</remarks>
    public TimeSpan SigningKeyCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Per-request timeout.</summary>
    /// <remarks>
    /// A timed-out ORDER has an unknown outcome: resume it with the same operation id, never a new one.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The configuration section name the settings-file overload reads by convention.</summary>
    public const string SectionName = "AnisPartners";

    /// <summary>The name of the application registered without one — the only one most hosts have.</summary>
    public const string DefaultClientName = "default";

    /// <summary>The name this application was registered under; tags its traces and metrics.</summary>
    internal string ClientName { get; set; } = DefaultClientName;

    /// <summary>Throws when the options cannot produce a working client.</summary>
    public void Validate()
    {
        if (Authority is null || !Authority.IsAbsoluteUri)
            throw new InvalidOperationException("AnisPartnersClientOptions.Authority must be the absolute authority Anis issued, e.g. https://partners.example.");

        if (Authority.Scheme != Uri.UriSchemeHttps && Authority.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("AnisPartnersClientOptions.Authority must be an http or https URI.");

        if (Timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("AnisPartnersClientOptions.Timeout must be positive.");

        if (SigningKeyCacheDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("AnisPartnersClientOptions.SigningKeyCacheDuration must be positive.");

        if (SignatureLifetime < TimeSpan.FromSeconds(1) || SignatureLifetime > MaxSignatureLifetime)
        {
            throw new InvalidOperationException(
                $"SignatureLifetime must be between 1 and {MaxSignatureLifetime.TotalSeconds:F0} seconds. Anis would admit up "
                + $"to {PartnerRequestSignatureBase.MaxSignatureLifetimeSeconds}, but this SDK accepts answers only within "
                + $"{MaxSignatureLifetime.TotalSeconds:F0} seconds: with a longer signature and a slow clock an order can "
                + "complete and its answer — with the card codes — be discarded.");
        }
    }

    internal string? AcceptLanguageHeader => AcceptLanguage switch
    {
        AnisLanguage.Arabic => "ar",
        AnisLanguage.English => "en",
        _ => null,
    };
}
