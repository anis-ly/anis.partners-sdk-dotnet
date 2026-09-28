using System.Security.Cryptography;
using System.Text.Json;
using Anis.Partners.Sdk;
using Anis.Partners.Sdk.Enrollment;
using Anis.Partners.Sdk.Models;
using Anis.Partners.Sdk.Verification;

namespace Anis.Partners.Sample;

/// <summary>One method per thing a partner does, each one call (or one short sequence) against the SDK.</summary>
internal sealed class SampleCommands(IAnisPartnersClient anis, ISigningKeySource keys, OrderJournal journal, bool showSecrets)
{
    private static readonly JsonSerializerOptions Print = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // ---- Reads ------------------------------------------------------------------------------------

    public async Task ProfileAsync(CancellationToken ct) => Show(await anis.Profile.GetAsync(ct));

    public async Task WalletsAsync(CancellationToken ct)
    {
        // The SDK follows the cursor; this is every granted wallet.
        await foreach (var wallet in anis.Wallets.ListAsync(ct))
            Show(wallet);
    }

    public async Task WalletAsync(Guid walletId, CancellationToken ct) => Show(await anis.Wallets.GetAsync(walletId, ct));

    public async Task CategoriesAsync(Guid walletId, CancellationToken ct) => Show(await anis.Catalogue.ListCategoriesPageAsync(walletId, cursor: null, ct));

    public async Task SubcategoriesAsync(Guid walletId, Guid categoryId, CancellationToken ct)
        => Show(await anis.Catalogue.ListSubcategoriesPageAsync(walletId, categoryId, cursor: null, ct));

    public async Task SubcategoryAsync(Guid walletId, Guid subcategoryId, CancellationToken ct)
        => Show(await anis.Catalogue.GetSubcategoryAsync(walletId, subcategoryId, ct));

    public async Task CardsAsync(Guid walletId, Guid subcategoryId, CancellationToken ct)
        => Show(await anis.Catalogue.ListCardsPageAsync(walletId, subcategoryId, cursor: null, ct));

    public async Task OwnedCardsAsync(Guid walletId, CancellationToken ct)
    {
        await foreach (var card in anis.OwnedCards.ListAsync(walletId, ct))
            Show(card);
    }

    public async Task OwnedCardAsync(Guid walletId, Guid soldCardId, CancellationToken ct) => Show(await anis.OwnedCards.GetAsync(walletId, soldCardId, ct));

    public async Task OrderStatusAsync(Guid operationId, CancellationToken ct) => Show(await anis.Orders.GetAsync(operationId, ct));

    public async Task DiagnosticAsync(CancellationToken ct) => Show(await anis.Diagnostics.CheckSignatureAsync(ct));

    public async Task SigningKeysAsync(CancellationToken ct) => Show(await keys.RefreshAsync(ct));

    // ---- Reveals ----------------------------------------------------------------------------------

    public async Task RevealAsync(Guid walletId, Guid soldCardId, CancellationToken ct)
        => Show(Mask(await anis.OwnedCards.RevealAsync(walletId, soldCardId, ct)));

    public async Task RevealInvoiceAsync(Guid walletId, Guid invoiceId, CancellationToken ct)
    {
        var revealed = await anis.OwnedCards.RevealInvoiceAsync(walletId, invoiceId, ct);

        Show(revealed with { Items = [.. revealed.Items.Select(Mask)] });
    }

    // ---- Orders -----------------------------------------------------------------------------------

    /// <summary>Reads the card's price from the catalogue, persists the intent, then places the order.</summary>
    public async Task OrderAsync(
        Guid walletId,
        Guid subcategoryId,
        Guid cardId,
        int quantity,
        Guid? operationId,
        decimal? expectedUnitPriceOverride,
        string? reference,
        bool useAllowedDebt,
        CancellationToken ct)
    {
        var card = await FindCardAsync(walletId, subcategoryId, cardId, ct);

        // The price THIS wallet pays is unitPrice. It is the one the order is checked against; businessPrice
        // is for display. The override exists only to demonstrate a price_changed refusal.
        var unitPrice = card.UnitPrice
            ?? throw new InvalidOperationException($"Card {cardId} carries no unitPrice for this wallet: it cannot be sold to it.");

        if (expectedUnitPriceOverride is { } overridden)
            unitPrice = unitPrice with { Amount = overridden };

        var request = new CreateOrderRequest
        {
            CardId = cardId,
            Quantity = quantity,
            ExpectedUnitPrice = unitPrice,
            ExpectedTotal = unitPrice.Multiply(quantity),
            ExternalReference = reference,
            UseAllowedDebt = useAllowedDebt,
        };

        // Yours, and persisted BEFORE the call: this is what makes every later resume safe.
#if NET9_0_OR_GREATER
        var id = operationId ?? Guid.CreateVersion7();   // time-ordered, handy in a database index
#else
        var id = operationId ?? Guid.NewGuid();
#endif

        await journal.RecordIntentAsync(id, walletId, request, ct);

        Console.WriteLine($"operation {id} recorded; placing {quantity} × {card.Name?.En ?? card.Id.ToString()} at {unitPrice}");

        await HandleAsync(new OrderIntent(id, walletId, request, "sent"), await anis.Orders.CreateAsync(walletId, id, request, ct), ct);
    }

    /// <summary>Re-drives a recorded order: the same operation id and the same body, freshly signed.</summary>
    /// <remarks>Also what an idempotent repeat is: after a completion it returns the replay, never a second sale.</remarks>
    public async Task ResumeAsync(Guid operationId, CancellationToken ct)
    {
        var intent = await journal.ReadAsync(operationId, ct);

        Console.WriteLine($"resuming operation {operationId} with its recorded request");

        await HandleAsync(intent, await anis.Orders.ResumeAsync(intent.WalletId, intent.OperationId, intent.Request, ct), ct);
    }

    // ---- The read-only tour -----------------------------------------------------------------------

    /// <summary>Every read route once, following real ids from one answer to the next. Moves no money.</summary>
    public async Task TourAsync(CancellationToken ct)
    {
        await Step("profile", () => ProfileAsync(ct));

        Wallet? wallet = null;

        await Step("wallets", async () =>
        {
            await foreach (var candidate in anis.Wallets.ListAsync(ct))
            {
                Show(candidate);
                wallet ??= candidate;
            }
        });

        if (wallet is null)
        {
            Console.WriteLine("No wallet is granted to this application; the tour stops here.");
            return;
        }

        await Step("wallet", () => WalletAsync(wallet.Id, ct));

        var categories = await anis.Catalogue.ListCategoriesPageAsync(wallet.Id, cursor: null, ct);
        Section("catalogue categories");
        Show(categories);

        if (categories.Items.Count > 0)
        {
            var subcategories = await anis.Catalogue.ListSubcategoriesPageAsync(wallet.Id, categories.Items[0].Id, cursor: null, ct);
            Section("subcategories of the first category");
            Show(subcategories);

            if (subcategories.Items.Count > 0)
            {
                var subcategory = subcategories.Items[0].Id;

                await Step("subcategory", () => SubcategoryAsync(wallet.Id, subcategory, ct));
                await Step("cards with this wallet's price", () => CardsAsync(wallet.Id, subcategory, ct));
            }
        }

        MaskedCard? owned = null;

        await Step("owned cards", async () =>
        {
            await foreach (var card in anis.OwnedCards.ListAsync(wallet.Id, ct))
            {
                Show(card);
                owned ??= card;
            }
        });

        if (owned is not null)
            await Step("one owned card", () => OwnedCardAsync(wallet.Id, owned.Id, ct));

        await Step("signature self-check", () => DiagnosticAsync(ct));
        await Step("published signing keys", () => SigningKeysAsync(ct));
    }

    // ---- Enrollment -------------------------------------------------------------------------------

    /// <summary>Generates a key, submits its public half, proves possession, and saves the private half.</summary>
    public static async Task EnrolAsync(AnisEnrollmentClient enrollment, string keyFile, int validityDays, IReadOnlyList<string> cidrs, CancellationToken ct)
    {
        if (File.Exists(keyFile))
            throw new InvalidOperationException(
                $"{keyFile} already exists. Enrollment generates a NEW key; choose another --key-file. If an earlier enrol "
                + "was interrupted after sending its key, that invitation has taken it: ask Anis staff to restart the enrollment first.");

        Section("the invitation");
        Show(await enrollment.GetAsync(ct));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // The private half is saved BEFORE Anis hears about the key, readable by this user only from the moment
        // the file exists. An invitation takes exactly one key: saving after the submission could spend it on a
        // key nobody holds.
        await SavePrivateKeyAsync(keyFile, key, ct);

        var now = DateTimeOffset.UtcNow;

        Section("submitting the public half");
        var submitted = await enrollment.SubmitKeyAsync(
            new EnrollmentKeyRequest
            {
                PublicJwk = AnisEnrollmentClient.PublicJwkOf(key),
                NotBefore = now,
                ExpiresAt = now.AddDays(validityDays),
                Cidrs = cidrs.Count > 0 ? cidrs : null,
            },
            ct);

        Section("proving possession");
        var proof = await enrollment.ProveAsync(submitted, key, ct);
        Show(proof);

        // A proof that did not verify, or came after the challenge expired, is an answer, not a refusal: the key is
        // still waiting for a proof and there is nothing for staff to confirm.
        if (proof.ProofState != "accepted")
        {
            Console.WriteLine($"""

                The proof was NOT accepted (proofState "{proof.ProofState}"). Nothing is waiting for Anis staff yet.
                This command proves right after submitting, so the key and its proof did not match or the challenge
                expired: ask Anis staff to restart the enrollment, then enrol again. The private key stays in
                {Path.GetFullPath(keyFile)}.
                """);

            return;
        }

        Console.WriteLine($"""

            key id      {submitted.KeyId:D}      (set Sample:KeyId to this)
            fingerprint {submitted.Thumbprint}
            private key {Path.GetFullPath(keyFile)}

            Give the fingerprint to Anis staff through the channel you agreed. The key signs requests once they
            record it and confirm it; `enrol-status` then reports state "active".
            """);
    }

    public static async Task EnrolStatusAsync(AnisEnrollmentClient enrollment, CancellationToken ct) => Show(await enrollment.GetStatusAsync(ct));

    private static async Task SavePrivateKeyAsync(string keyFile, ECDsa key, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var file = new StreamWriter(keyFile, options);
        await file.WriteAsync(key.ExportPkcs8PrivateKeyPem().AsMemory(), ct);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private async Task<CatalogueCard> FindCardAsync(Guid walletId, Guid subcategoryId, Guid cardId, CancellationToken ct)
    {
        await foreach (var card in anis.Catalogue.ListCardsAsync(walletId, subcategoryId, ct))
        {
            if (card.Id == cardId)
                return card;
        }

        throw new InvalidOperationException($"Card {cardId} is not in subcategory {subcategoryId} for wallet {walletId}.");
    }

    private async Task HandleAsync(OrderIntent intent, OrderResult outcome, CancellationToken ct)
    {
        // A preview or dry run never sent the request; the SDK reports that as an unknown outcome, but nothing was placed.
        if (outcome is OrderOutcomeUnknown { Cause: DryRunException })
            throw new DryRunException();

        switch (outcome)
        {
            case OrderCompleted completed:
                // The ONLY response that carries the credentials: persist them before anything else.
                await journal.RecordOutcomeAsync(intent, "completed", completed.Credentials, ct);
                Section($"COMPLETED — {completed.Credentials.Count} credential(s), stored in the journal");
                Show(completed.Order with { SoldCards = [.. completed.Credentials.Select(Mask)] });
                break;

            case OrderProcessing { Order.Status: OrderStatus.RecoveryExhausted } exhausted:
                await journal.RecordOutcomeAsync(intent, "recovery-exhausted", null, ct);
                Section($"RECOVERY EXHAUSTED — Anis could not learn the outcome yet; it may have completed. Resume again in a few minutes (a later resume can still return the codes) and tell Anis operation {intent.OperationId:D}. Never place it again under a new id.");
                Show(exhausted.Order);
                break;

            case OrderProcessing processing:
                await journal.RecordOutcomeAsync(intent, "processing", null, ct);
                Section($"PROCESSING — no outcome yet. Resume after {processing.RetryAfter.TotalSeconds:F0}s with: resume {intent.OperationId:D}");
                Show(processing.Order);
                break;

            case OrderReplayed replayed:
                Section("REPLAYED — this order's outcome was delivered earlier; no second sale, no credentials here");
                Show(replayed.Order);
                break;

            case OrderOutcomeUnknown unknown:
                await journal.RecordOutcomeAsync(intent, "unknown", intent.Credentials, ct);
                Section($"OUTCOME UNKNOWN ({unknown.Cause.GetType().Name}) — it may have gone through. Resume after {unknown.SuggestedDelay.TotalSeconds:F0}s with: resume {intent.OperationId:D}. Never place it again under a new id.");
                break;

            case OrderNotPlaced notPlaced:
                await journal.RecordOutcomeAsync(intent, "not-placed", intent.Credentials, ct);
                Section("NOT PLACED — nothing was bought and nothing was charged. Fix the cause, then place a NEW order with a NEW operation id.");
                Refusal.Print(notPlaced.Refusal);
                break;
        }
    }

    private RevealedCredential Mask(RevealedCredential credential)
        => showSecrets
            ? credential
            : credential with { Voucher = MaskValue(credential.Voucher), SerialNumber = MaskValue(credential.SerialNumber) };

    private static string? MaskValue(string? value)
        => value is null ? null : value.Length <= 4 ? "****" : value[..2] + new string('*', value.Length - 4) + value[^2..];

    private static async Task Step(string title, Func<Task> action)
    {
        Section(title);
        await action();
    }

    private static void Section(string title) => Console.WriteLine($"\n=== {title}");

    private static void Show<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Print));
}
