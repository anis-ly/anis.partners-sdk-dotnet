using Anis.Partners.Sample;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sdk.Tests;

public sealed class ProductJournalTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "anis-product-journal-" + Guid.NewGuid().ToString("N"));
    private static CreateDirectRechargeRequest Request => new()
    {
        ProductId = ProductBodies.Product, PayoutAmount = "1500.000", ExpectedTotal = new Money(237.500m, "LYD"),
        Recipient = [new DirectRechargeRecipientInput { Key = "recipient_number", Value = "01012345678" }],
    };

    [Fact]
    public async Task Outcome_replacement_and_cancellation_preserve_the_original_recovery_bytes()
    {
        var journal = new ProductJournal(_folder);
        var ct = TestContext.Current.CancellationToken;
        await journal.RecordIntentAsync("direct-recharges", ProductBodies.DirectRechargeId, ProductBodies.Wallet, Request, ct);
        var intentPath = Path.Combine(_folder, "direct-recharges", $"{ProductBodies.DirectRechargeId:D}.json");
        var original = await File.ReadAllBytesAsync(intentPath, ct);
        var intent = await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct);
        await journal.RecordOutcomeAsync("direct-recharges", intent, "unknown", ct);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.RecordOutcomeAsync("direct-recharges", intent, "completed", cancelled.Token));
        Assert.Equal("unknown", (await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct)).Outcome);
        await journal.RecordOutcomeAsync("direct-recharges", intent, "completed", ct);
        var recovered = await new ProductJournal(_folder).ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(intentPath, ct));
        Assert.Equal("completed", recovered.Outcome);
        Assert.Equal(ProductBodies.DirectRechargeId, recovered.OperationId);
        Assert.Equal(ProductBodies.Wallet, recovered.WalletId);
        Assert.Equal("01012345678", Assert.Single(recovered.Request.Recipient).Value);
        Assert.Equal(Request.ExpectedTotal, recovered.Request.ExpectedTotal);
        Assert.Equal(Request.ProductId, recovered.Request.ProductId);
        Assert.Equal(Request.PayoutAmount, recovered.Request.PayoutAmount);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp", SearchOption.AllDirectories));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(intentPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.ChangeExtension(intentPath, "outcome.json")));
        }
    }

    [Fact]
    public async Task Competing_intent_writers_publish_exactly_one_complete_immutable_request()
    {
        var journal = new ProductJournal(_folder);
        var ct = TestContext.Current.CancellationToken;
        var requests = new[] { Request, Request with { PayoutAmount = "2500.000" } };
        var attempts = await Task.WhenAll(requests.Select(async request =>
        {
            try
            {
                await journal.RecordIntentAsync("direct-recharges", ProductBodies.DirectRechargeId, ProductBodies.Wallet, request, ct);
                return true;
            }
            catch (IOException) { return false; }
        }));
        Assert.Single(attempts, success => success);
        Assert.Single(attempts, success => !success);
        var recovered = await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct);
        Assert.Equal(requests[Array.FindIndex(attempts, success => success)].PayoutAmount, recovered.Request.PayoutAmount);
        Assert.Equal("01012345678", Assert.Single(recovered.Request.Recipient).Value);
        await Assert.ThrowsAsync<IOException>(() => journal.RecordIntentAsync("direct-recharges", ProductBodies.DirectRechargeId, Guid.NewGuid(), Request, ct));
        Assert.Equal(ProductBodies.Wallet, (await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct)).WalletId);
    }

    [Fact]
    public async Task An_outcome_disk_failure_leaves_the_intent_readable()
    {
        var journal = new ProductJournal(_folder);
        var ct = TestContext.Current.CancellationToken;
        await journal.RecordIntentAsync("direct-recharges", ProductBodies.DirectRechargeId, ProductBodies.Wallet, Request, ct);
        var intent = await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct);
        var outcomePath = Path.Combine(_folder, "direct-recharges", $"{ProductBodies.DirectRechargeId:D}.outcome.json");
        Directory.CreateDirectory(outcomePath);
        await Assert.ThrowsAnyAsync<IOException>(() => journal.RecordOutcomeAsync("direct-recharges", intent, "completed", ct));
        Directory.Delete(outcomePath);
        var recovered = await journal.ReadAsync<CreateDirectRechargeRequest>("direct-recharges", ProductBodies.DirectRechargeId, ct);
        Assert.Equal("recorded", recovered.Outcome);
        Assert.Equal("01012345678", Assert.Single(recovered.Request.Recipient).Value);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
