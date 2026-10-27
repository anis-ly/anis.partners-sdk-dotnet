using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>The product category.</summary>
[JsonConverter(typeof(LenientEnumConverter<DirectRechargeProductType>))]
public enum DirectRechargeProductType
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>External.</summary>
    External = 1,

    /// <summary>Billing.</summary>
    Billing = 2,
}

/// <summary>The recipient field input kind.</summary>
[JsonConverter(typeof(LenientEnumConverter<DirectRechargeFieldKind>))]
public enum DirectRechargeFieldKind
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Text.</summary>
    Text = 1,

    /// <summary>List.</summary>
    List = 2,

    /// <summary>Note.</summary>
    Note = 3,
}

/// <summary>The person a field describes.</summary>
[JsonConverter(typeof(LenientEnumConverter<DirectRechargeFieldFor>))]
public enum DirectRechargeFieldFor
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Recipient.</summary>
    Recipient = 1,

    /// <summary>Sender.</summary>
    Sender = 2,
}

/// <summary>The side of a direct recharge quote supplied by the caller.</summary>
[JsonConverter(typeof(LenientEnumConverter<DirectRechargeAmountIn>))]
public enum DirectRechargeAmountIn
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Lyd.</summary>
    Lyd = 1,

    /// <summary>Fx.</summary>
    Fx = 2,
}

/// <summary>A direct recharge product priced for the selected wallet.</summary>
public sealed record DirectRechargeProductSummary
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Title.</summary>
    [JsonPropertyName("title")] public required string Title { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }

    /// <summary>Type.</summary>
    [JsonPropertyName("type")] public required DirectRechargeProductType Type { get; init; }

    /// <summary>Category.</summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeCategory? Category { get; init; }

    /// <summary>PayoutCurrency.</summary>
    [JsonPropertyName("payoutCurrency")] public required DirectRechargeCurrency PayoutCurrency { get; init; }

    /// <summary>Available.</summary>
    [JsonPropertyName("available")] public required bool Available { get; init; }

    /// <summary>Limits.</summary>
    [JsonPropertyName("limits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeLimits? Limits { get; init; }
}

/// <summary>A direct recharge product and its dynamic recipient fields.</summary>
public sealed record DirectRechargeProduct
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Title.</summary>
    [JsonPropertyName("title")] public required string Title { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }

    /// <summary>Type.</summary>
    [JsonPropertyName("type")] public required DirectRechargeProductType Type { get; init; }

    /// <summary>Category.</summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeCategory? Category { get; init; }

    /// <summary>PayoutCurrency.</summary>
    [JsonPropertyName("payoutCurrency")] public required DirectRechargeCurrency PayoutCurrency { get; init; }

    /// <summary>Available.</summary>
    [JsonPropertyName("available")] public required bool Available { get; init; }

    /// <summary>Limits.</summary>
    [JsonPropertyName("limits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeLimits? Limits { get; init; }

    /// <summary>RecipientFields.</summary>
    [JsonPropertyName("recipientFields")] public required IReadOnlyList<DirectRechargeRecipientField> RecipientFields { get; init; }
}

/// <summary>A direct recharge category.</summary>
public sealed record DirectRechargeCategory
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>Icon.</summary>
    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Icon { get; init; }
}

/// <summary>The payout currency display name; it has no implied ISO code.</summary>
public sealed record DirectRechargeCurrency
{
    /// <summary>Name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }
}

/// <summary>The selected tier limits; missing limits remain absent.</summary>
public sealed record DirectRechargeLimits
{
    /// <summary>The lower LYD limit, when the selected tier defines one.</summary>
    [JsonPropertyName("minimumTotal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Money? MinimumTotal { get; init; }

    /// <summary>The upper LYD limit, when the selected tier defines one.</summary>
    [JsonPropertyName("maximumTotal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Money? MaximumTotal { get; init; }

    /// <summary>The lower payout limit, when supplied by the owner.</summary>
    [JsonPropertyName("minimumPayout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MinimumPayout { get; init; }

    /// <summary>The upper payout limit, when the selected tier defines one.</summary>
    [JsonPropertyName("maximumPayout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MaximumPayout { get; init; }
}

/// <summary>A dynamic recipient field defined by the product.</summary>
public sealed record DirectRechargeRecipientField
{
    /// <summary>Key.</summary>
    [JsonPropertyName("key")] public required string Key { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LocalizedText? Name { get; init; }

    /// <summary>Kind.</summary>
    [JsonPropertyName("kind")] public required DirectRechargeFieldKind Kind { get; init; }

    /// <summary>The person this field describes.</summary>
    [JsonPropertyName("for")] public required DirectRechargeFieldFor For { get; init; }

    /// <summary>Order.</summary>
    [JsonPropertyName("order")] public required int Order { get; init; }

    /// <summary>Validation.</summary>
    [JsonPropertyName("validation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Validation { get; init; }

    /// <summary>ValidationMessage.</summary>
    [JsonPropertyName("validationMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LocalizedText? ValidationMessage { get; init; }
}

/// <summary>An available recipient location.</summary>
public sealed record DirectRechargeLocation
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>Name.</summary>
    [JsonPropertyName("name")] public required string Name { get; init; }
}

/// <summary>A payout in the product currency, without decimal conversion.</summary>
public sealed record DirectRechargePayout
{
    /// <summary>Amount.</summary>
    [JsonPropertyName("amount")] public required string Amount { get; init; }

    /// <summary>Currency.</summary>
    [JsonPropertyName("currency")] public required DirectRechargeCurrency Currency { get; init; }
}

/// <summary>A current quote; no money is reserved.</summary>
public sealed record DirectRechargeQuote
{
    /// <summary>ProductId.</summary>
    [JsonPropertyName("productId")] public required Guid ProductId { get; init; }

    /// <summary>Payout.</summary>
    [JsonPropertyName("payout")] public required DirectRechargePayout Payout { get; init; }

    /// <summary>The exact decimal rate, including its precision.</summary>
    [JsonPropertyName("exchangeRate")] public required string ExchangeRate { get; init; }

    /// <summary>Price.</summary>
    [JsonPropertyName("price")] public required Money Price { get; init; }

    /// <summary>Commission.</summary>
    [JsonPropertyName("commission")] public required Money Commission { get; init; }

    /// <summary>Total.</summary>
    [JsonPropertyName("total")] public required Money Total { get; init; }
}

/// <summary>Optional filters; cursors must be reused with the same filters.</summary>
public sealed record DirectRechargeProductsQuery
{
    /// <summary>Type.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DirectRechargeProductType? Type { get; init; }

    /// <summary>Cursor.</summary>
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cursor { get; init; }
}

/// <summary>Optional filters; cursors must be reused with the same filters.</summary>
public sealed record DirectRechargeLocationsQuery
{
    /// <summary>Search.</summary>
    [JsonPropertyName("search")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Search { get; init; }

    /// <summary>Cursor.</summary>
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cursor { get; init; }
}
