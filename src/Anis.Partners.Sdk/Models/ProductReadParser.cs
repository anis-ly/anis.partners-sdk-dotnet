using System.Text.Json;

namespace Anis.Partners.Sdk.Models;

internal static class ProductReadParser
{
    internal static MeterCharge Meter(JsonElement data)
    {
        RequireObject(data);
        var value = data.Deserialize<MeterCharge>(AnisJson.Options) ?? throw Invalid();
        if (value.OperationId == Guid.Empty || value.WalletId == Guid.Empty || value.CreatedAt == default
            || value.MeterNumber is null || value.Package is null || value.Package.Id == Guid.Empty
            || value.Package.Name is null || value.Package.GecolPackageName is null)
            throw Invalid();
        var status = value.Status;
        if (value.Receipt is { } receipt)
        {
            if (receipt.Tokens is null || receipt.Taxes is null
                || receipt.Tokens.Any(token => token is null || token.Label is null || token.Code is null)
                || receipt.Taxes.Any(tax => tax is null || tax.Description is null || tax.Amount is null))
                throw Invalid();
        }
        if (value.Failure is { } failure && failure.RawCode is null)
            throw Invalid();
        if (status == MeterChargeStatus.Succeeded && value.Receipt is null)
            throw Invalid();
        return value;
    }

    internal static DirectRecharge DirectRecharge(JsonElement data)
    {
        RequireObject(data);
        var value = data.Deserialize<DirectRecharge>(AnisJson.Options) ?? throw Invalid();
        return ValidateDirectRecharge(value);
    }

    internal static DirectRecharge ValidateDirectRecharge(DirectRecharge value)
    {
        if (value.OperationId == Guid.Empty || value.WalletId == Guid.Empty || value.RequestedAt == default
            || value.Product is null || value.Product.Id == Guid.Empty || value.Product.Title is null
            || value.Payout is null || value.Payout.Amount is null || value.Payout.Currency is null || value.Payout.Currency.Name is null
            || value.ExchangeRate is null || value.Recipient is null
            || value.Recipient.Any(recipient => recipient is null || recipient.Key is null || recipient.Value is null))
            throw Invalid();
        if (value.PartialRefund is { } refund && (refund.RefundedPayout is null || refund.RemainingPayout is null))
            throw Invalid();
        return value;
    }

    private static void RequireObject(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw Invalid();
    }

    private static JsonException Invalid() => new("The product owner read does not satisfy the public model.");
}
