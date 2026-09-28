using System.Globalization;
using System.Runtime.CompilerServices;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Signing;

namespace Anis.Partners.Sdk.Operations;

/// <summary>The application's own identity and effective scopes.</summary>
public interface IProfileOperations
{
    /// <summary>Reads the current profile. Scopes come from the live policy, so re-read rather than cache.</summary>
    Task<PartnerProfile> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Wallets this application may act on.</summary>
public interface IWalletOperations
{
    /// <summary>Every granted and eligible wallet, following the cursor for you.</summary>
    IAsyncEnumerable<Wallet> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One page, when you want the cursor yourself.</summary>
    Task<Page<Wallet>> ListPageAsync(string? cursor, CancellationToken cancellationToken = default);

    /// <summary>One wallet.</summary>
    Task<Wallet> GetAsync(Guid walletId, CancellationToken cancellationToken = default);
}

/// <summary>The published catalogue, priced for one wallet.</summary>
public interface ICatalogueOperations
{
    /// <summary>Every category, following the cursor for you.</summary>
    IAsyncEnumerable<CatalogueCategory> ListCategoriesAsync(Guid walletId, CancellationToken cancellationToken = default);

    /// <summary>One page of categories, when you want the cursor yourself.</summary>
    Task<Page<CatalogueCategory>> ListCategoriesPageAsync(Guid walletId, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>Every subcategory of a category, following the cursor for you.</summary>
    IAsyncEnumerable<CatalogueSubcategory> ListSubcategoriesAsync(Guid walletId, Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>One page of subcategories, when you want the cursor yourself.</summary>
    Task<Page<CatalogueSubcategory>> ListSubcategoriesPageAsync(Guid walletId, Guid categoryId, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>One subcategory.</summary>
    Task<CatalogueSubcategory> GetSubcategoryAsync(Guid walletId, Guid subcategoryId, CancellationToken cancellationToken = default);

    /// <summary>Every card listed for this wallet in a subcategory, with this wallet's price, following the cursor for you. A listed card may still be refused at sale time.</summary>
    IAsyncEnumerable<CatalogueCard> ListCardsAsync(Guid walletId, Guid subcategoryId, CancellationToken cancellationToken = default);

    /// <summary>One page of cards, when you want the cursor yourself.</summary>
    Task<Page<CatalogueCard>> ListCardsPageAsync(Guid walletId, Guid subcategoryId, string? cursor, CancellationToken cancellationToken = default);
}

/// <summary>Buying, and recovering a purchase whose outcome was not delivered.</summary>
public interface IOrderOperations
{
    /// <summary>
    /// Places an order under an operation identity the CALLER owns and has already persisted. Needs
    /// <c>orders:create</c>; counts toward a staff-set <c>orders</c> limit.
    /// </summary>
    /// <remarks>
    /// <paramref name="operationId"/> is required and the SDK will not invent one. A generated key turns a
    /// dropped response plus an ordinary retry into a second purchase.
    ///
    /// Every outcome is a case of <see cref="OrderResult"/>, including a refusal, a timeout and an answer that could
    /// not be verified. The call throws only for a mistake in the call itself (an invalid request, a signer that
    /// failed before anything was sent) or when <paramref name="cancellationToken"/> is cancelled.
    /// </remarks>
    Task<OrderResult> CreateAsync(Guid walletId, Guid operationId, CreateOrderRequest order, CancellationToken cancellationToken = default);

    /// <summary>Re-drives an operation whose outcome is unknown. Same id, same body, freshly signed. Returns the same five cases as <see cref="CreateAsync"/>.</summary>
    /// <remarks>
    /// This is the recovery protocol: an exact repeated signed POST performs current-policy recovery.
    /// Reading <see cref="GetAsync"/> in a loop is NOT recovery — a GET reports state and dispatches
    /// nothing, so an operation that was never re-driven stays where it is forever.
    /// </remarks>
    Task<OrderResult> ResumeAsync(Guid walletId, Guid operationId, CreateOrderRequest order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports state. Dispatches nothing, and never carries credentials. Needs <c>orders:read</c> — or
    /// <c>orders:create</c>, which also reads this application's own orders.
    /// </summary>
    Task<Order> GetAsync(Guid operationId, CancellationToken cancellationToken = default);
}

/// <summary>Cards this application's wallets own, and their credentials.</summary>
public interface IOwnedCardOperations
{
    /// <summary>Every owned card in a wallet, following the cursor for you.</summary>
    IAsyncEnumerable<MaskedCard> ListAsync(Guid walletId, CancellationToken cancellationToken = default);

    /// <summary>One page, when you want the cursor yourself.</summary>
    Task<Page<MaskedCard>> ListPageAsync(Guid walletId, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>One owned card, masked.</summary>
    Task<MaskedCard> GetAsync(Guid walletId, Guid soldCardId, CancellationToken cancellationToken = default);

    /// <summary>Reveals one credential. Needs <c>cards:reveal</c>; counts toward a staff-set <c>reveals</c> limit.</summary>
    Task<RevealedCredential> RevealAsync(Guid walletId, Guid soldCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reveals every credential on an invoice: all of them or none, capped at 100. Needs <c>cards:reveal</c>;
    /// counts toward a staff-set <c>reveals</c> limit. The invoice id comes from an order or a masked card.
    /// </summary>
    Task<RevealedCredentialCollection> RevealInvoiceAsync(Guid walletId, Guid invoiceId, CancellationToken cancellationToken = default);
}

/// <summary>The signature self-check.</summary>
/// <remarks>
/// Needs <c>diagnostics:use</c>, and is served by every Anis deployment. It runs the full admission path and
/// has no side effect: no order, no evidence, no owner call. It is the right first call when a signature
/// will not verify, because it reports the exact facts the gateway built its base from — the method,
/// authority, path and canonical query it actually saw — plus the key, application and scopes it resolved.
/// It still counts toward a staff-set <c>requests</c> limit.
/// </remarks>
public interface IDiagnosticsOperations
{
    /// <summary>Reports what the gateway saw.</summary>
    Task<SignatureDiagnostic> CheckSignatureAsync(CancellationToken cancellationToken = default);
}

internal sealed class ProfileOperations(PartnerTransport transport) : IProfileOperations
{
    public Task<PartnerProfile> GetAsync(CancellationToken cancellationToken = default)
        => transport.GetAsync<PartnerProfile>("/v1/profile", "v1/profile", SignatureProfile.SafeRead, cancellationToken);
}

internal sealed class WalletOperations(PartnerTransport transport) : IWalletOperations
{
    public async IAsyncEnumerable<Wallet> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var page = await ListPageAsync(cursor, cancellationToken).ConfigureAwait(false);

            foreach (var wallet in page.Items)
                yield return wallet;

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    public Task<Page<Wallet>> ListPageAsync(string? cursor, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<Wallet>>("/v1/wallets", Routes.Append("v1/wallets", cursor), SignatureProfile.SafeRead, cancellationToken);

    public Task<Wallet> GetAsync(Guid walletId, CancellationToken cancellationToken = default)
        => transport.GetAsync<Wallet>("/v1/wallets/{walletId}", $"v1/wallets/{walletId:D}", SignatureProfile.SafeRead, cancellationToken);
}

internal sealed class CatalogueOperations(PartnerTransport transport) : ICatalogueOperations
{
    public async IAsyncEnumerable<CatalogueCategory> ListCategoriesAsync(Guid walletId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var page = await ListCategoriesPageAsync(walletId, cursor, cancellationToken).ConfigureAwait(false);

            foreach (var category in page.Items)
                yield return category;

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    public Task<Page<CatalogueCategory>> ListCategoriesPageAsync(Guid walletId, string? cursor, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<CatalogueCategory>>(
            "/v1/wallets/{walletId}/catalog/categories",
            Routes.Append($"v1/wallets/{walletId:D}/catalog/categories", cursor), SignatureProfile.SafeRead, cancellationToken);

    public async IAsyncEnumerable<CatalogueSubcategory> ListSubcategoriesAsync(Guid walletId, Guid categoryId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var page = await ListSubcategoriesPageAsync(walletId, categoryId, cursor, cancellationToken).ConfigureAwait(false);

            foreach (var subcategory in page.Items)
                yield return subcategory;

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    public Task<Page<CatalogueSubcategory>> ListSubcategoriesPageAsync(Guid walletId, Guid categoryId, string? cursor, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<CatalogueSubcategory>>(
            "/v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories",
            Routes.Append($"v1/wallets/{walletId:D}/catalog/categories/{categoryId:D}/subcategories", cursor), SignatureProfile.SafeRead, cancellationToken);

    public Task<CatalogueSubcategory> GetSubcategoryAsync(Guid walletId, Guid subcategoryId, CancellationToken cancellationToken = default)
        => transport.GetAsync<CatalogueSubcategory>(
            "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}",
            $"v1/wallets/{walletId:D}/catalog/subcategories/{subcategoryId:D}", SignatureProfile.SafeRead, cancellationToken);

    public async IAsyncEnumerable<CatalogueCard> ListCardsAsync(Guid walletId, Guid subcategoryId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var page = await ListCardsPageAsync(walletId, subcategoryId, cursor, cancellationToken).ConfigureAwait(false);

            foreach (var card in page.Items)
                yield return card;

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    public Task<Page<CatalogueCard>> ListCardsPageAsync(Guid walletId, Guid subcategoryId, string? cursor, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<CatalogueCard>>(
            "/v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards",
            Routes.Append($"v1/wallets/{walletId:D}/catalog/subcategories/{subcategoryId:D}/cards", cursor), SignatureProfile.SafeRead, cancellationToken);
}

internal sealed class OwnedCardOperations(PartnerTransport transport) : IOwnedCardOperations
{
    public async IAsyncEnumerable<MaskedCard> ListAsync(Guid walletId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var page = await ListPageAsync(walletId, cursor, cancellationToken).ConfigureAwait(false);

            foreach (var card in page.Items)
                yield return card;

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    public Task<Page<MaskedCard>> ListPageAsync(Guid walletId, string? cursor, CancellationToken cancellationToken = default)
        => transport.GetAsync<Page<MaskedCard>>(
            "/v1/wallets/{walletId}/cards",
            Routes.Append($"v1/wallets/{walletId:D}/cards", cursor), SignatureProfile.SafeRead, cancellationToken);

    public Task<MaskedCard> GetAsync(Guid walletId, Guid soldCardId, CancellationToken cancellationToken = default)
        => transport.GetAsync<MaskedCard>("/v1/wallets/{walletId}/cards/{soldCardId}", $"v1/wallets/{walletId:D}/cards/{soldCardId:D}", SignatureProfile.SafeRead, cancellationToken);

    public Task<RevealedCredential> RevealAsync(Guid walletId, Guid soldCardId, CancellationToken cancellationToken = default)
        => transport.PostWithoutBodyAsync<RevealedCredential>("/v1/wallets/{walletId}/cards/{soldCardId}/reveal", $"v1/wallets/{walletId:D}/cards/{soldCardId:D}/reveal", cancellationToken);

    public Task<RevealedCredentialCollection> RevealInvoiceAsync(Guid walletId, Guid invoiceId, CancellationToken cancellationToken = default)
        => transport.PostWithoutBodyAsync<RevealedCredentialCollection>("/v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal", $"v1/wallets/{walletId:D}/invoices/{invoiceId:D}/cards/reveal", cancellationToken);
}

internal sealed class DiagnosticsOperations(PartnerTransport transport) : IDiagnosticsOperations
{
    public Task<SignatureDiagnostic> CheckSignatureAsync(CancellationToken cancellationToken = default)
        => transport.PostEmptyObjectAsync<SignatureDiagnostic>("/v1/diagnostics/signature", "v1/diagnostics/signature", cancellationToken);
}

/// <summary>Builds route paths. The query is appended verbatim, because it is signed verbatim.</summary>
internal static class Routes
{
    public static string Append(string path, string? cursor)
        => string.IsNullOrEmpty(cursor) ? path : $"{path}?cursor={Uri.EscapeDataString(cursor)}";

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
