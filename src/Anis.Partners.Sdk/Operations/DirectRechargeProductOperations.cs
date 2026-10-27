using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>Direct recharge products, recipient locations and wallet-priced quotes.</summary>
public interface IDirectRechargeProductOperations
{
    /// <summary>Gets one page of wallet-priced products.</summary>
    Task<Page<DirectRechargeProductSummary>> GetDirectRechargeProductsPageAsync(Guid walletId, DirectRechargeProductsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets wallet-priced products across pages, preserving the type filter.</summary>
    IAsyncEnumerable<DirectRechargeProductSummary> GetDirectRechargeProductsAsync(Guid walletId, DirectRechargeProductsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one product and its dynamic recipient fields.</summary>
    Task<DirectRechargeProduct> GetAsync(Guid walletId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Gets one page of recipient locations.</summary>
    Task<Page<DirectRechargeLocation>> GetDirectRechargeProductLocationsPageAsync(Guid walletId, Guid productId, DirectRechargeLocationsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets recipient locations across pages, preserving the search.</summary>
    IAsyncEnumerable<DirectRechargeLocation> GetDirectRechargeProductLocationsAsync(Guid walletId, Guid productId, DirectRechargeLocationsQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Quotes a decimal string on the LYD or payout side without reserving money.</summary>
    Task<DirectRechargeQuote> QuoteAsync(Guid walletId, Guid productId, string amount, DirectRechargeAmountIn amountIn, CancellationToken cancellationToken = default);
}

internal sealed class DirectRechargeProductOperations(PartnerTransport transport) : IDirectRechargeProductOperations
{
    public Task<Page<DirectRechargeProductSummary>> GetDirectRechargeProductsPageAsync(Guid walletId, DirectRechargeProductsQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        return transport.GetAsync<Page<DirectRechargeProductSummary>>("/v1/wallets/{walletId}/direct-recharge-products",
            ProductPaging.Query($"v1/wallets/{walletId:D}/direct-recharge-products",
                ("type", ProductPaging.EnumValue(query.Type)), ("cursor", query.Cursor)), SignatureProfile.SafeRead, cancellationToken);
    }

    public IAsyncEnumerable<DirectRechargeProductSummary> GetDirectRechargeProductsAsync(Guid walletId, DirectRechargeProductsQuery? query = null, CancellationToken cancellationToken = default)
    {
        var filters = query ?? new();
        return ProductPaging.GetAsync((cursor, token) => GetDirectRechargeProductsPageAsync(walletId, filters with { Cursor = cursor }, token), filters.Cursor, cancellationToken);
    }

    public Task<DirectRechargeProduct> GetAsync(Guid walletId, Guid productId, CancellationToken cancellationToken = default)
        => transport.GetAsync<DirectRechargeProduct>("/v1/wallets/{walletId}/direct-recharge-products/{productId}",
            $"v1/wallets/{walletId:D}/direct-recharge-products/{productId:D}", SignatureProfile.SafeRead, cancellationToken);

    public Task<Page<DirectRechargeLocation>> GetDirectRechargeProductLocationsPageAsync(Guid walletId, Guid productId, DirectRechargeLocationsQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new();
        return transport.GetAsync<Page<DirectRechargeLocation>>("/v1/wallets/{walletId}/direct-recharge-products/{productId}/locations",
            ProductPaging.Query($"v1/wallets/{walletId:D}/direct-recharge-products/{productId:D}/locations",
                ("search", query.Search), ("cursor", query.Cursor)), SignatureProfile.SafeRead, cancellationToken);
    }

    public IAsyncEnumerable<DirectRechargeLocation> GetDirectRechargeProductLocationsAsync(Guid walletId, Guid productId, DirectRechargeLocationsQuery? query = null, CancellationToken cancellationToken = default)
    {
        var filters = query ?? new();
        return ProductPaging.GetAsync((cursor, token) => GetDirectRechargeProductLocationsPageAsync(walletId, productId, filters with { Cursor = cursor }, token), filters.Cursor, cancellationToken);
    }

    public Task<DirectRechargeQuote> QuoteAsync(Guid walletId, Guid productId, string amount, DirectRechargeAmountIn amountIn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(amount);
        var side = amountIn switch
        {
            DirectRechargeAmountIn.Lyd => "lyd",
            DirectRechargeAmountIn.Fx => "fx",
            _ => throw new ArgumentOutOfRangeException(nameof(amountIn), amountIn, "Choose LYD or the payout currency."),
        };
        return transport.GetAsync<DirectRechargeQuote>("/v1/wallets/{walletId}/direct-recharge-products/{productId}/quote",
            ProductPaging.Query($"v1/wallets/{walletId:D}/direct-recharge-products/{productId:D}/quote",
                ("amount", amount), ("amountIn", side)), SignatureProfile.SafeRead, cancellationToken);
    }
}
