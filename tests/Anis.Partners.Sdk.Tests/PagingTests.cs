namespace Anis.Partners.Sdk.Tests;

/// <summary>Cursor paging, through the real signing and verifying pipeline.</summary>
/// <remarks>
/// The second page is the interesting one: it is the first request whose <c>@query</c> is not empty, and
/// the query is signed EXACTLY as transmitted. A client that rebuilt it from a parsed dictionary would
/// re-order or re-encode it and fail at the gateway, and no single-page test would ever notice.
/// </remarks>
public sealed class PagingTests
{
    private static readonly Guid Wallet = Guid.Parse("2f1c8a94-6d37-4e52-b8a1-0c9e5d3f7b26");
    private static readonly Guid Subcategory = Guid.Parse("7a1c3e5f-2b4d-4f68-8a0c-9e1b3d5f7a2c");

    [Fact]
    public async Task ListAsync_follows_the_cursor_and_signs_the_query_it_transmits()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"11111111-1111-1111-1111-111111111111","name":"A","currency":"LYD","balance":{"amount":"1.000","currency":"LYD"}}],"nextCursor":"eyJwIjoyfQ"}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"22222222-2222-2222-2222-222222222222","name":"B","currency":"LYD","balance":{"amount":"2.000","currency":"LYD"}}],"nextCursor":null}""");

        var wallets = new List<string?>();

        await foreach (var wallet in fixture.Client.Wallets.ListAsync(TestContext.Current.CancellationToken))
            wallets.Add(wallet.Name);

        Assert.Equal(["A", "B"], wallets);
        Assert.Equal(2, fixture.Stub.Requests.Count);

        // First page: no query at all. The signature base covers "?" for it, which is the rule that trips
        // most first implementations.
        Assert.Equal(string.Empty, fixture.Stub.Requests[0].RequestUri!.Query);

        // Second page: the cursor is on the wire, and the signature covers exactly these bytes.
        Assert.Equal("?cursor=eyJwIjoyfQ", fixture.Stub.Requests[1].RequestUri!.Query);

        var signatureInput = fixture.Stub.Requests[1].Headers.GetValues("Signature-Input").Single();

        Assert.Contains("\"@query\"", signatureInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owned_cards_page_the_same_way()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"33333333-3333-3333-3333-333333333333","credentialAvailable":true}],"nextCursor":"next"}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"44444444-4444-4444-4444-444444444444","credentialAvailable":false}],"nextCursor":null}""");

        var cards = new List<Guid>();

        await foreach (var card in fixture.Client.OwnedCards.ListAsync(Wallet, TestContext.Current.CancellationToken))
            cards.Add(card.Id);

        Assert.Equal(2, cards.Count);
        Assert.Equal("?cursor=next", fixture.Stub.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Catalogue_cards_page_the_same_way()
    {
        using var fixture = new PipelineFixture();

        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"55555555-5555-5555-5555-555555555555"}],"nextCursor":"more"}""");
        fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"66666666-6666-6666-6666-666666666666"}],"nextCursor":null}""");

        var cards = new List<Guid>();

        await foreach (var card in fixture.Client.Catalogue.ListCardsAsync(Wallet, Subcategory, TestContext.Current.CancellationToken))
            cards.Add(card.Id);

        Assert.Equal([Guid.Parse("55555555-5555-5555-5555-555555555555"), Guid.Parse("66666666-6666-6666-6666-666666666666")], cards);
        Assert.Equal(2, fixture.Stub.Requests.Count);
        Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/subcategories/{Subcategory:D}/cards", fixture.Stub.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/subcategories/{Subcategory:D}/cards", fixture.Stub.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, fixture.Stub.Requests[0].RequestUri!.Query);
        Assert.Equal("?cursor=more", fixture.Stub.Requests[1].RequestUri!.Query);
        var signatureInput = fixture.Stub.Requests[1].Headers.GetValues("Signature-Input").Single();
        Assert.Contains("\"@query\"", signatureInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalogue_categories_and_subcategories_page_the_same_way()
    {
        using (var fixture = new PipelineFixture())
        {
            fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"77777777-7777-7777-7777-777777777777"}],"nextCursor":"c2"}""");
            fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"88888888-8888-8888-8888-888888888888"}],"nextCursor":null}""");

            var categories = new List<Guid>();

            await foreach (var category in fixture.Client.Catalogue.ListCategoriesAsync(Wallet, TestContext.Current.CancellationToken))
                categories.Add(category.Id);

            Assert.Equal([Guid.Parse("77777777-7777-7777-7777-777777777777"), Guid.Parse("88888888-8888-8888-8888-888888888888")], categories);
            Assert.Equal(2, fixture.Stub.Requests.Count);
            Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/categories", fixture.Stub.Requests[0].RequestUri!.AbsolutePath);
            Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/categories", fixture.Stub.Requests[1].RequestUri!.AbsolutePath);
            Assert.Equal(string.Empty, fixture.Stub.Requests[0].RequestUri!.Query);
            Assert.Equal("?cursor=c2", fixture.Stub.Requests[1].RequestUri!.Query);
            var signatureInput = fixture.Stub.Requests[1].Headers.GetValues("Signature-Input").Single();
            Assert.Contains("\"@query\"", signatureInput, StringComparison.Ordinal);
        }

        using (var fixture = new PipelineFixture())
        {
            fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"99999999-9999-9999-9999-999999999999"}],"nextCursor":"s2"}""");
            fixture.Stub.Bodies.Enqueue("""{"items":[{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}],"nextCursor":null}""");
            var categoryId = Guid.Parse("77777777-7777-7777-7777-777777777777");
            var subcategories = new List<Guid>();

            await foreach (var subcategory in fixture.Client.Catalogue.ListSubcategoriesAsync(Wallet, categoryId, TestContext.Current.CancellationToken))
                subcategories.Add(subcategory.Id);

            Assert.Equal([Guid.Parse("99999999-9999-9999-9999-999999999999"), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")], subcategories);
            Assert.Equal(2, fixture.Stub.Requests.Count);
            Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/categories/{categoryId:D}/subcategories", fixture.Stub.Requests[0].RequestUri!.AbsolutePath);
            Assert.Equal($"/v1/wallets/{Wallet:D}/catalog/categories/{categoryId:D}/subcategories", fixture.Stub.Requests[1].RequestUri!.AbsolutePath);
            Assert.Equal(string.Empty, fixture.Stub.Requests[0].RequestUri!.Query);
            Assert.Equal("?cursor=s2", fixture.Stub.Requests[1].RequestUri!.Query);
            var signatureInput = fixture.Stub.Requests[1].Headers.GetValues("Signature-Input").Single();
            Assert.Contains("\"@query\"", signatureInput, StringComparison.Ordinal);
        }
    }
}
