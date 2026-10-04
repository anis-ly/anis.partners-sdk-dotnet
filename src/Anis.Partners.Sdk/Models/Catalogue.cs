using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>Owner-supplied Arabic and English text. The gateway invents no fallback, so either may be absent.</summary>
public sealed record LocalizedText
{
    /// <summary>Arabic.</summary>
    [JsonPropertyName("ar")] public string? Ar { get; init; }

    /// <summary>English.</summary>
    [JsonPropertyName("en")] public string? En { get; init; }
}

/// <summary>Where a category's cards come from.</summary>
[JsonConverter(typeof(LenientEnumConverter<CatalogueCategoryType>))]
public enum CatalogueCategoryType
{
    /// <summary>Absent, or a value this SDK version does not know.</summary>
    Unknown = 0,

    /// <summary>Local.</summary>
    Local = 1,

    /// <summary>International.</summary>
    International = 2,
}

/// <summary>A published catalogue category.</summary>
public sealed record CatalogueCategory
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")] public LocalizedText? Description { get; init; }

    /// <summary>Logo URL.</summary>
    [JsonPropertyName("logo")] public string? Logo { get; init; }

    /// <summary>Local or international.</summary>
    [JsonPropertyName("type")] public CatalogueCategoryType Type { get; init; }

    /// <summary>Live stock, under the current owner rules.</summary>
    [JsonPropertyName("inStock")] public bool InStock { get; init; }

    /// <summary>Owner-defined ordering.</summary>
    [JsonPropertyName("displayOrder")] public int DisplayOrder { get; init; }
}

/// <summary>A published catalogue subcategory.</summary>
public sealed record CatalogueSubcategory
{
    /// <summary>Identifier.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>The category this belongs to.</summary>
    [JsonPropertyName("categoryId")] public Guid CategoryId { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")] public LocalizedText? Description { get; init; }

    /// <summary>Logo URL.</summary>
    [JsonPropertyName("logo")] public string? Logo { get; init; }

    /// <summary>Owner-flagged best seller.</summary>
    [JsonPropertyName("isBestSelling")] public bool IsBestSelling { get; init; }

    /// <summary>Owner-defined ordering.</summary>
    [JsonPropertyName("displayOrder")] public int DisplayOrder { get; init; }

    /// <summary>Live availability.</summary>
    [JsonPropertyName("available")] public bool Available { get; init; }

    /// <summary>
    /// Anis's note for buyers of this subcategory's cards (terms, region, how to redeem). Show it to your end
    /// customer before they buy. Absent when there is none in either language.
    /// </summary>
    [JsonPropertyName("disclaimer")] public LocalizedText? Disclaimer { get; init; }
}

/// <summary>A purchasable card and the price THIS wallet pays for it.</summary>
/// <remarks>
/// The four price members come from the one owner pricing result this wallet's business pricing produced.
/// None is re-derived, here or in the gateway — a second calculation would be a second authority on what a
/// card costs. Send <see cref="UnitPrice"/> back as <c>expectedUnitPrice</c>, exactly as read; do not
/// compute one and do not substitute another price member.
/// </remarks>
public sealed record CatalogueCard
{
    /// <summary>Identifier. This is the <c>cardId</c> an order carries.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }

    /// <summary>The subcategory this belongs to.</summary>
    [JsonPropertyName("subcategoryId")] public Guid SubcategoryId { get; init; }

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")] public LocalizedText? Name { get; init; }

    /// <summary>Printed face value, when the card has one.</summary>
    [JsonPropertyName("faceValue")] public string? FaceValue { get; init; }

    /// <summary>
    /// The price THIS wallet pays for one card now — the one an order is checked against. Send it as
    /// <c>expectedUnitPrice</c>. Absent when the card cannot be sold to this wallet.
    /// </summary>
    [JsonPropertyName("unitPrice")] public Money? UnitPrice { get; init; }

    /// <summary>
    /// The wallet's business price without any special offer. For display and comparison only — on some
    /// wallets it differs from <see cref="UnitPrice"/>, and an order sent at this price is refused as
    /// <c>price_changed</c>.
    /// </summary>
    [JsonPropertyName("businessPrice")] public Money? BusinessPrice { get; init; }

    /// <summary>The retail price, present only while it is above the business price. Display only.</summary>
    [JsonPropertyName("personalPrice")] public Money? PersonalPrice { get; init; }

    /// <summary>Whether a special offer applies.</summary>
    [JsonPropertyName("hasSpecialOffer")] public bool HasSpecialOffer { get; init; }

    /// <summary>The offer price, present only together with <see cref="HasSpecialOffer"/>. Display only.</summary>
    [JsonPropertyName("specialOfferPrice")] public Money? SpecialOfferPrice { get; init; }

    /// <summary>Live availability under the current owner rules.</summary>
    [JsonPropertyName("available")] public bool Available { get; init; }

    /// <summary>
    /// The fewest of this card one order may carry. Check it before you order: a quantity below it is refused.
    /// If it is above <see cref="MaximumQuantity"/> the card cannot be bought at all.
    /// </summary>
    [JsonPropertyName("minimumQuantity")] public int? MinimumQuantity { get; init; }

    /// <summary>
    /// The most of this card one order may carry — the card's own limit or Anis's order cap, whichever is lower.
    /// Check it before you order: a quantity above it is refused.
    /// </summary>
    [JsonPropertyName("maximumQuantity")] public int? MaximumQuantity { get; init; }
}

/// <summary>One page of a cursor-paged list.</summary>
/// <typeparam name="T">The item type this route returns.</typeparam>
public sealed record Page<T>
{
    /// <summary>The items on this page.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Pass to the next call to continue; null when the list is exhausted.</summary>
    [JsonPropertyName("nextCursor")] public string? NextCursor { get; init; }
}
