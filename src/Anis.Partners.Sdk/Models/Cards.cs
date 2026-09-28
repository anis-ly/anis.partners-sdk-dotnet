using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>A sold card as the masked projection shows it. Never carries plaintext.</summary>
public sealed record MaskedCard
{
    /// <summary>The sold-card identifier. This is what a single reveal takes.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>The order operation that produced it.</summary>
    [JsonPropertyName("orderOperationId")] public Guid? OrderOperationId { get; init; }

    /// <summary>
    /// The owner invoice it belongs to, and the ONLY way a Partner learns an invoice id — the invoice
    /// reveal route is unreachable without one.
    /// </summary>
    [JsonPropertyName("invoiceId")] public Guid? InvoiceId { get; init; }

    /// <summary>The catalogue card this was sold from.</summary>
    [JsonPropertyName("card")] public MaskedCardProduct? Card { get; init; }

    /// <summary>The masked serial. Plaintext is never present on a masked projection.</summary>
    [JsonPropertyName("serialNumberMasked")] public string? SerialNumberMasked { get; init; }

    /// <summary>Whether a credential can still be revealed.</summary>
    [JsonPropertyName("credentialAvailable")] public bool CredentialAvailable { get; init; }

    /// <summary>When it was purchased.</summary>
    [JsonPropertyName("purchasedAt")] public DateTimeOffset? PurchasedAt { get; init; }
}

/// <summary>The catalogue card a sold card came from.</summary>
public sealed record MaskedCardProduct
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }
}

/// <summary>Credential plaintext.</summary>
/// <remarks>
/// The only representation on the whole surface that carries a secret. It exists inside the protected
/// response and reaches Anis's logs, traces, metrics and error metadata nowhere. Treat it the same way:
/// persist it where you keep secrets, and do not log the object.
/// </remarks>
public sealed record RevealedCredential
{
    /// <summary>The sold card this credential belongs to.</summary>
    [JsonPropertyName("soldCardId")] public Guid SoldCardId { get; init; }

    /// <summary>The serial number.</summary>
    [JsonPropertyName("serialNumber")] public string? SerialNumber { get; init; }

    /// <summary>The voucher code.</summary>
    [JsonPropertyName("voucher")] public string? Voucher { get; init; }

    /// <summary>When it was revealed.</summary>
    [JsonPropertyName("revealedAt")] public DateTimeOffset? RevealedAt { get; init; }

    /// <summary>Redacted on purpose: a credential must not reach a log through a careless interpolation.</summary>
    public override string ToString() => $"RevealedCredential {{ SoldCardId = {SoldCardId}, Secret = <redacted> }}";
}

/// <summary>The credentials of one invoice. All of them or none.</summary>
public sealed record RevealedCredentialCollection
{
    /// <summary>Every credential on the invoice, capped at 100 by the contract.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<RevealedCredential> Items { get; init; } = [];
}
