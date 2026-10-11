using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>A sold card as the masked projection shows it. Never carries plaintext.</summary>
public sealed record MaskedCard
{
    /// <summary>The sold-card identifier. This is what a single reveal takes.</summary>
    [JsonPropertyName("soldCardId")] public Guid SoldCardId { get; init; }

    /// <summary>The order operation that produced it.</summary>
    [JsonPropertyName("operationId")] public Guid? OperationId { get; init; }

    /// <summary>The owner invoice it belongs to, also usable for an invoice reveal.</summary>
    [JsonPropertyName("invoiceId")] public Guid? InvoiceId { get; init; }

    /// <summary>The catalogue card this was sold from.</summary>
    [JsonPropertyName("card")] public CardSummary? Card { get; init; }

    /// <summary>The masked serial. Plaintext is never present on a masked projection.</summary>
    [JsonPropertyName("serialNumberMasked")] public string? SerialNumberMasked { get; init; }

    /// <summary>Whether a credential can still be revealed.</summary>
    [JsonPropertyName("credentialAvailable")] public bool CredentialAvailable { get; init; }

    /// <summary>When it was purchased.</summary>
    [JsonPropertyName("purchasedAt")] public DateTimeOffset? PurchasedAt { get; init; }

    /// <summary>What one card cost, as originally charged (also after a refund). Use it for your own records.</summary>
    [JsonPropertyName("unitPrice")] public Money? UnitPrice { get; init; }

    /// <summary>The last day the card can be used. Tell your end customer. Absent when Anis has no expiry date for this card.</summary>
    [JsonPropertyName("expiryDate")] public DateOnly? ExpiryDate { get; init; }

    /// <summary>The number printed on the invoice this card was sold on. Quote it when you write to support.</summary>
    [JsonPropertyName("invoiceNumber")] public int? InvoiceNumber { get; init; }
}

/// <summary>A card's category or subcategory identity and localized name.</summary>
public sealed record CardClassification
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }
}

/// <summary>The catalogue card a sold card came from.</summary>
public sealed record CardSummary
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }

    /// <summary>The card's printed face value. Absent when it has none.</summary>
    [JsonPropertyName("faceValue")] public string? FaceValue { get; init; }

    /// <summary>The subcategory's absolute logo URL.</summary>
    [JsonPropertyName("logo")] public string? Logo { get; init; }

    /// <summary>The subcategory the card belongs to.</summary>
    [JsonPropertyName("subcategory")] public CardClassification? Subcategory { get; init; }

    /// <summary>The category the card belongs to.</summary>
    [JsonPropertyName("category")] public CardClassification? Category { get; init; }
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

    /// <summary>The last day the card can be used. Tell your end customer. Absent when Anis has no expiry date for this card.</summary>
    [JsonPropertyName("expiryDate")] public DateOnly? ExpiryDate { get; init; }

    /// <summary>The invoice the card was sold on, including in an order's <c>soldCards</c>.</summary>
    [JsonPropertyName("invoiceId")] public Guid? InvoiceId { get; init; }

    /// <summary>The catalogue card this credential belongs to, including in an order's <c>soldCards</c>.</summary>
    [JsonPropertyName("card")] public CardSummary? Card { get; init; }

    /// <summary>When the card was purchased, including in an order's <c>soldCards</c>.</summary>
    [JsonPropertyName("purchasedAt")] public DateTimeOffset? PurchasedAt { get; init; }

    /// <summary>Redacted on purpose: a credential must not reach a log through a careless interpolation.</summary>
    public override string ToString() => $"RevealedCredential {{ SoldCardId = {SoldCardId}, Secret = <redacted> }}";
}

/// <summary>The credentials of one invoice. All of them or none.</summary>
public sealed record RevealedCredentialCollection
{
    /// <summary>Every credential on the invoice, capped at 100 by the contract.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<RevealedCredential> Items { get; init; } = [];
}
