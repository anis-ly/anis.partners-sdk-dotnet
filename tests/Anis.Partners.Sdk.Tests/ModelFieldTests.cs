using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Card and order members read from the wire as the contract spells them, and optional members stay null when omitted.</summary>
/// <remarks>
/// Optional members on the wire, such as a card's expiry, must read cleanly when absent:
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
        var card = Read<CatalogueCard>("""{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","minimumQuantity":1,"maximumQuantity":100}""");

        Assert.Equal(1, card.MinimumQuantity);
        Assert.Equal(100, card.MaximumQuantity);
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
    public void An_order_with_optional_members_absent_leaves_them_null()
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
    public void A_revealed_credential_with_optional_members_absent_leaves_them_null()
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
            {"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","credentialAvailable":false,
             "unitPrice":{"amount":"10.500","currency":"LYD"},"expiryDate":"2027-03-31","invoiceNumber":1042,
             "card":{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","faceValue":"10 USD","subcategory":{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c","name":{"ar":"فئة","en":"Games"}},"logo":"https://assets.anis.ly/card.png","category":{"id":"6e3b9d27-1f84-4a5c-b0d6-8c2e7f4a9b13","name":{"en":"Local"}}}}
            """);

        Assert.Equal(new Money(10.500m, "LYD"), card.UnitPrice);
        Assert.Equal(new DateOnly(2027, 3, 31), card.ExpiryDate);
        Assert.Equal(1042, card.InvoiceNumber);
        Assert.Equal("10 USD", card.Card?.FaceValue);
        Assert.Equal(Guid.Parse("7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c"), card.Card?.Subcategory?.Id);
        Assert.Equal("Games", card.Card?.Subcategory?.Name?.En);
        Assert.False(card.CredentialAvailable);
        Assert.Equal(Guid.Parse("4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046"), card.SoldCardId);
        Assert.Equal(Guid.Parse("9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34"), card.OperationId);
        Assert.Equal("https://assets.anis.ly/card.png", card.Card?.Logo);
        Assert.Equal("Local", card.Card?.Category?.Name?.En);
    }

    [Fact]
    public void A_masked_card_with_optional_members_absent_leaves_them_null()
    {
        var card = Read<MaskedCard>("""{"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","credentialAvailable":true}""");

        Assert.Null(card.UnitPrice);
        Assert.Null(card.ExpiryDate);
        Assert.Null(card.InvoiceNumber);
        Assert.Null(card.Card?.FaceValue);
        Assert.Null(card.Card?.Subcategory);
    }

    [Fact]
    public void A_catalogue_card_reads_its_logo_and_category()
    {
        var card = Read<CatalogueCard>("""{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","logo":"https://assets.anis.ly/card.png","category":{"id":"6e3b9d27-1f84-4a5c-b0d6-8c2e7f4a9b13","name":{"en":"Local"}}}""");
        Assert.Equal("https://assets.anis.ly/card.png", card.Logo);
        Assert.Equal("Local", card.Category?.Name?.En);
    }

    [Fact]
    public void A_subcategory_list_item_reads_its_description()
    {
        var page = Read<Page<CatalogueSubcategory>>("""{"items":[{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c","description":{"en":"Games"}}]}""");
        Assert.Equal("Games", Assert.Single(page.Items).Description?.En);
    }

    [Fact]
    public void Order_sold_cards_carry_the_full_revealed_credential()
    {
        var order = Read<Order>("""
            {"operationId":"9b2e4f17-3c6a-4d58-b0e1-7a5c8d2f6b34","status":"completed","soldCards":[
             {"soldCardId":"4a6c2e81-7b39-4d15-a2f8-3e7b9c1d5046","serialNumber":"serial","voucher":"secret",
              "revealedAt":"2026-09-19T08:00:00Z","expiryDate":"2027-03-31","invoiceId":"c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51",
              "purchasedAt":"2026-09-19T08:00:00Z","card":{"id":"8d4b1e73-9a25-4c60-8f37-6b2e9d5a1c48","faceValue":"10 USD",
               "logo":"https://assets.anis.ly/card.png","subcategory":{"id":"7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c","name":{"en":"Games"}},
               "category":{"id":"6e3b9d27-1f84-4a5c-b0d6-8c2e7f4a9b13","name":{"en":"Local"}}}}]}
            """);
        var credential = Assert.Single(order.SoldCards!);
        Assert.Equal("serial", credential.SerialNumber);
        Assert.Equal("secret", credential.Voucher);
        Assert.Equal(PipelineFixture.Now, credential.RevealedAt);
        Assert.Equal(PipelineFixture.Now, credential.PurchasedAt);
        Assert.Equal(new DateOnly(2027, 3, 31), credential.ExpiryDate);
        Assert.Equal(Guid.Parse("c1a7e2d9-5b64-4f18-9e03-2d7a6c4b8f51"), credential.InvoiceId);
        Assert.Equal("10 USD", credential.Card?.FaceValue);
        Assert.Equal("https://assets.anis.ly/card.png", credential.Card?.Logo);
        Assert.Equal("Games", credential.Card?.Subcategory?.Name?.En);
        Assert.Equal("Local", credential.Card?.Category?.Name?.En);
    }

    [Fact]
    public void Card_and_wallet_models_have_only_the_v2_wire_members()
    {
        static string[] Members<T>() => typeof(T).GetProperties()
            .Select(property => property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Single().Name)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "soldCardId", "operationId", "invoiceId", "invoiceNumber", "card", "serialNumberMasked", "credentialAvailable", "purchasedAt", "unitPrice", "expiryDate" }.Order(StringComparer.Ordinal), Members<MaskedCard>());
        Assert.Equal(new[] { "id", "name", "faceValue", "logo", "subcategory", "category" }.Order(StringComparer.Ordinal), Members<CardSummary>());
        Assert.Equal(new[] { "soldCardId", "serialNumber", "voucher", "revealedAt", "expiryDate", "invoiceId", "card", "purchasedAt" }.Order(StringComparer.Ordinal), Members<RevealedCredential>());
        Assert.Equal(new[] { "id", "name", "balance" }.Order(StringComparer.Ordinal), Members<Wallet>());
    }

    [Fact]
    public void Empty_strings_and_localized_text_are_omitted()
    {
        var json = JsonSerializer.Serialize(new CardSummary
        {
            Logo = "", FaceValue = "", Name = new LocalizedText { Ar = "", En = "" },
            Category = new CardClassification { Name = new LocalizedText { Ar = "", En = "Local" } },
        }, AnisJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("logo", out _));
        Assert.False(document.RootElement.TryGetProperty("faceValue", out _));
        Assert.False(document.RootElement.TryGetProperty("name", out _));
        var name = document.RootElement.GetProperty("category").GetProperty("name");
        Assert.False(name.TryGetProperty("ar", out _));
        Assert.Equal("Local", name.GetProperty("en").GetString());
    }

    [Theory]
    [InlineData("{\"amount\":\"-1\",\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":\"+1.000\",\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":\"--1.000\",\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":\"-1.0000\",\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":\"1.00\",\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":1.000,\"currency\":\"LYD\"}")]
    [InlineData("{\"amount\":\"1.000\",\"currency\":\"lyd\"}")]
    [InlineData("{\"currency\":\"LYD\"}")]
    public void Money_refuses_values_outside_the_wire_contract(string json)
        => Assert.Throws<JsonException>(() => Read<Money>(json));

    [Theory]
    [InlineData(1, "lyd")]
    [InlineData(1, "LY")]
    public void Money_does_not_write_invalid_amounts_or_currencies(int amount, string currency)
        => Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(amount, currency), AnisJson.Options));

    [Fact]
    public void Money_does_not_write_an_amount_finer_than_three_places()
        => Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(1.0004m, "LYD"), AnisJson.Options));

    [Fact]
    public void A_wallet_in_allowed_debt_reads_and_writes_its_negative_balance()
    {
        var wallet = Read<Wallet>("""{"id":"00000000-0000-0000-0000-000000000001","balance":{"amount":"-1250.075","currency":"LYD","asOf":"2026-10-14T08:00:00Z"}}""");

        Assert.Equal(-1250.075m, wallet.Balance.Amount);
        Assert.Equal("-1250.075", wallet.Balance.ToWireAmount());
        Assert.Equal(
            """{"amount":"-1250.075","currency":"LYD","asOf":"2026-10-14T08:00:00Z"}""",
            JsonSerializer.Serialize(wallet.Balance, AnisJson.Options));
    }

    [Fact]
    public void Negative_zero_on_the_wire_reads_as_zero()
        => Assert.Equal("0.000", Read<Money>("""{"amount":"-0.000","currency":"LYD"}""").ToWireAmount());

}
