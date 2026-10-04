using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>The members added in 1.2.0 read from the wire as the contract spells them, and stay null when Anis omits them.</summary>
/// <remarks>
/// Every addition is optional on the wire, so an older answer (or a card that has no expiry) must still read cleanly:
/// absent means null here, never a default that could be mistaken for a value.
/// </remarks>
public sealed class ModelFieldTests
{
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, AnisJson.Options)!;

    [Fact]
    public void A_subcategory_reads_its_disclaimer()
    {
        var subcategory = Read<CatalogueSubcategory>(
            """{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c","disclaimer":{"ar":"ملاحظة","en":"Valid in Libya only."}}""");

        Assert.Equal("Valid in Libya only.", subcategory.Disclaimer?.En);
        Assert.Equal("ملاحظة", subcategory.Disclaimer?.Ar);
    }

    [Fact]
    public void A_subcategory_without_a_disclaimer_has_none()
        => Assert.Null(Read<CatalogueSubcategory>("""{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c"}""").Disclaimer);

    [Fact]
    public void A_catalogue_card_reads_its_quantity_limits()
    {
        var card = Read<CatalogueCard>("""{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","minimumQuantity":2,"maximumQuantity":50}""");

        Assert.Equal(2, card.MinimumQuantity);
        Assert.Equal(50, card.MaximumQuantity);
    }

    [Fact]
    public void A_catalogue_card_without_quantity_limits_has_none()
    {
        var card = Read<CatalogueCard>("""{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"}""");

        Assert.Null(card.MinimumQuantity);
        Assert.Null(card.MaximumQuantity);
    }

    [Fact]
    public void An_order_reads_its_reference_failure_code_and_withheld_flag()
    {
        var order = Read<Order>(
            """{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","status":"failed","externalReference":"INV-77","failureCode":"out_of_stock","codesWithheld":true}""");

        Assert.Equal("INV-77", order.ExternalReference);
        Assert.Equal("out_of_stock", order.FailureCode);
        Assert.True(order.CodesWithheld);
    }

    [Fact]
    public void An_order_without_the_new_members_leaves_them_null()
    {
        var order = Read<Order>("""{"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","status":"completed"}""");

        Assert.Null(order.ExternalReference);
        Assert.Null(order.FailureCode);
        Assert.Null(order.CodesWithheld);
    }

    [Fact]
    public void A_revealed_credential_reads_its_expiry_and_reveal_details()
    {
        var credential = Read<RevealedCredential>(
            """
            {"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","voucher":"1234","expiryDate":"2027-03-31",
             "invoiceId":"c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51",
             "card":{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","name":{"ar":"بطاقة","en":"Card"}},
             "purchasedAt":"2026-09-19T08:00:00Z"}
            """);

        Assert.Equal(new DateOnly(2027, 3, 31), credential.ExpiryDate);
        Assert.Equal(Guid.Parse("c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51"), credential.InvoiceId);
        Assert.Equal(Guid.Parse("8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48"), credential.Card?.Id);
        Assert.Equal("Card", credential.Card?.Name?.En);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero), credential.PurchasedAt);
    }

    [Fact]
    public void A_revealed_credential_without_the_new_members_leaves_them_null()
    {
        var credential = Read<RevealedCredential>("""{"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","voucher":"1234"}""");

        Assert.Null(credential.ExpiryDate);
        Assert.Null(credential.InvoiceId);
        Assert.Null(credential.Card);
        Assert.Null(credential.PurchasedAt);
    }

    [Fact]
    public void A_masked_card_reads_its_price_expiry_invoice_number_face_value_and_subcategory()
    {
        var card = Read<MaskedCard>(
            """
            {"id":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","credentialAvailable":false,
             "unitPrice":{"amount":"10.500","currency":"LYD"},"expiryDate":"2027-03-31","invoiceNumber":1042,
             "faceValue":"10 USD","subcategory":{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c","name":{"ar":"فئة","en":"Games"}}}
            """);

        Assert.Equal(new Money(10.500m, "LYD"), card.UnitPrice);
        Assert.Equal(new DateOnly(2027, 3, 31), card.ExpiryDate);
        Assert.Equal(1042, card.InvoiceNumber);
        Assert.Equal("10 USD", card.FaceValue);
        Assert.Equal(Guid.Parse("7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c"), card.Subcategory?.Id);
        Assert.Equal("Games", card.Subcategory?.Name?.En);
        Assert.False(card.CredentialAvailable);
    }

    [Fact]
    public void A_masked_card_without_the_new_members_leaves_them_null()
    {
        var card = Read<MaskedCard>("""{"id":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","credentialAvailable":true}""");

        Assert.Null(card.UnitPrice);
        Assert.Null(card.ExpiryDate);
        Assert.Null(card.InvoiceNumber);
        Assert.Null(card.FaceValue);
        Assert.Null(card.Subcategory);
    }
}
