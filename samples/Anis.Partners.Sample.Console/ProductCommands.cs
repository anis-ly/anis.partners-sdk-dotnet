using System.Text.Json;
using Anis.Partners.Sdk;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sample;

/// <summary>Meter charges, Direct recharge and notification management: one short sequence per thing a partner does.</summary>
/// <remarks>
/// Purchases persist the operation id and the request first, then branch on the returned case. Tokens and recipient
/// details are masked on screen unless the command is run with <c>--show-secrets</c>. The private intent journal
/// stores recipient input for recovery; wire logs redact product bodies.
/// </remarks>
internal sealed class ProductCommands(IAnisPartnersClient anis, ProductJournal journal, bool showSecrets)
{
    private const string MeterKind = "meter-charges";
    private const string RechargeKind = "direct-recharges";

    private static readonly JsonSerializerOptions Print = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // ---- Meter charges ----------------------------------------------------------------------------

    public async Task MeterPackagesAsync(Guid walletId, CancellationToken ct)
    {
        await foreach (var package in anis.MeterCharges.GetMeterPackagesAsync(walletId, ct))
            Show(package);
    }

    public async Task MeterChargesAsync(Guid walletId, CancellationToken ct)
    {
        await foreach (var summary in anis.MeterCharges.GetMeterChargesAsync(walletId, query: null, ct))
            Show(summary);
    }

    public async Task MeterChargeAsync(Guid walletId, Guid packageId, string meterNumber, string reference, Guid? operationId, CancellationToken ct)
    {
        var package = await FindPackageAsync(walletId, packageId, ct);

        // unitPrice is what THIS wallet pays; the charge is checked against it.
        var request = new CreateMeterChargeRequest
        {
            PackageId = packageId,
            MeterNumber = meterNumber,
            ExpectedTotal = package.UnitPrice,
            ExternalReference = reference,
        };

        var id = operationId ?? Guid.NewGuid();

        await journal.RecordIntentAsync(MeterKind, id, walletId, request, ct);

        Console.WriteLine($"operation {id} recorded; charging meter {meterNumber} with {package.Name} at {package.UnitPrice}");

        await HandleMeterAsync(new ProductIntent<CreateMeterChargeRequest>(id, walletId, request, "sent"), await anis.MeterCharges.CreateAsync(walletId, id, request, ct), ct);
    }

    public async Task MeterResumeAsync(Guid operationId, CancellationToken ct)
    {
        var intent = await journal.ReadAsync<CreateMeterChargeRequest>(MeterKind, operationId, ct);

        // The same id and the same recorded body; never a new id.
        await HandleMeterAsync(intent, await anis.MeterCharges.ResumeAsync(intent.WalletId, intent.OperationId, intent.Request, ct), ct);
    }

    public async Task MeterStatusAsync(Guid operationId, CancellationToken ct)
    {
        try
        {
            Show(Mask(await anis.MeterCharges.GetAsync(operationId, ct)));
        }
        catch (MeterChargeProcessingException processing)
        {
            Console.WriteLine($"PROCESSING — read again after {processing.RetryAfter.TotalSeconds:F0}s");
        }
    }

    private async Task HandleMeterAsync(ProductIntent<CreateMeterChargeRequest> intent, MeterChargeResult outcome, CancellationToken ct)
    {
        if (outcome is MeterChargeOutcomeUnknown { Cause: DryRunException })
            throw new DryRunException();

        switch (outcome)
        {
            case MeterChargeFinal { Charge.Status: MeterChargeStatus.Succeeded } succeeded:
                await journal.RecordOutcomeAsync(MeterKind, intent, "succeeded", ct);
                Section("SUCCEEDED — store the receipt and every token, in order, in your secret store");
                Show(Mask(succeeded.Charge));
                break;

            case MeterChargeFinal failed:
                await journal.RecordOutcomeAsync(MeterKind, intent, "failed", ct);
                Section($"FAILED — refund {failed.Charge.Refund?.Status}; nothing more to resend");
                Show(failed.Charge);
                break;

            case MeterChargeProcessing processing:
                await journal.RecordOutcomeAsync(MeterKind, intent, "processing", ct);
                Section($"PROCESSING — resume after {processing.RetryAfter.TotalSeconds:F0}s with: meter-resume {intent.OperationId:D}");
                break;

            case MeterChargeOutcomeUnknown unknown:
                await journal.RecordOutcomeAsync(MeterKind, intent, "unknown", ct);
                Section($"OUTCOME UNKNOWN ({unknown.Cause.GetType().Name}) — it may have gone through. Resume after {unknown.SuggestedDelay.TotalSeconds:F0}s with: meter-resume {intent.OperationId:D}. Never charge it again under a new id.");
                break;

            case MeterChargeNotPlaced notPlaced:
                await journal.RecordOutcomeAsync(MeterKind, intent, "not-placed", ct);
                Section("NOT PLACED — nothing was charged. Fix the cause, then charge again with a NEW operation id.");
                Refusal.Print(notPlaced.Refusal);
                break;
        }
    }

    // ---- Direct recharge --------------------------------------------------------------------------

    public async Task RechargeProductsAsync(Guid walletId, CancellationToken ct)
    {
        await foreach (var product in anis.DirectRechargeProducts.GetDirectRechargeProductsAsync(walletId, query: null, ct))
            Show(product);
    }

    public async Task RechargeProductAsync(Guid walletId, Guid productId, CancellationToken ct)
        => Show(await anis.DirectRechargeProducts.GetAsync(walletId, productId, ct));

    public async Task RechargeLocationsAsync(Guid walletId, Guid productId, string? search, CancellationToken ct)
    {
        await foreach (var location in anis.DirectRechargeProducts.GetDirectRechargeProductLocationsAsync(
            walletId, productId, new DirectRechargeLocationsQuery { Search = search }, ct))
            Show(location);
    }

    public async Task RechargeQuoteAsync(Guid walletId, Guid productId, string amount, DirectRechargeAmountIn amountIn, CancellationToken ct)
        => Show(await anis.DirectRechargeProducts.QuoteAsync(walletId, productId, amount, amountIn, ct));

    public async Task RechargeListAsync(Guid walletId, CancellationToken ct)
    {
        await foreach (var summary in anis.DirectRecharges.GetDirectRechargesAsync(walletId, query: null, ct))
            Show(summary);
    }

    public async Task RechargeAsync(Guid walletId, Guid productId, string payoutAmount, IReadOnlyList<string> recipient, string? note, string? reference, Guid? operationId, CancellationToken ct)
    {
        // Quote first: the total the wallet will pay is the amount the purchase is checked against.
        var quote = await anis.DirectRechargeProducts.QuoteAsync(walletId, productId, payoutAmount, DirectRechargeAmountIn.Fx, ct);

        var request = new CreateDirectRechargeRequest
        {
            ProductId = productId,
            PayoutAmount = quote.Payout.Amount,
            ExpectedTotal = quote.Total,
            Recipient = ToInputs(recipient),
            Note = note,
            ExternalReference = reference,
        };

        var id = operationId ?? Guid.NewGuid();

        await journal.RecordIntentAsync(RechargeKind, id, walletId, request, ct);

        Console.WriteLine($"operation {id} recorded; paying out {quote.Payout.Amount} {quote.Payout.Currency.Name} for {quote.Total}");

        await HandleRechargeAsync(new ProductIntent<CreateDirectRechargeRequest>(id, walletId, request, "sent"), await anis.DirectRecharges.CreateAsync(walletId, id, request, ct), ct);
    }

    public async Task RechargeResumeAsync(Guid operationId, CancellationToken ct)
    {
        var intent = await journal.ReadAsync<CreateDirectRechargeRequest>(RechargeKind, operationId, ct);

        await HandleRechargeAsync(intent, await anis.DirectRecharges.ResumeAsync(intent.WalletId, intent.OperationId, intent.Request, ct), ct);
    }

    public async Task RechargeStatusAsync(Guid operationId, CancellationToken ct)
    {
        try
        {
            Show(Mask(await anis.DirectRecharges.GetAsync(operationId, ct)));
        }
        catch (DirectRechargeProcessingException processing)
        {
            Console.WriteLine($"PROCESSING — read again after {processing.RetryAfter.TotalSeconds:F0}s");
        }
    }

    /// <summary>Corrects a paused direct recharge; prints the same processing acknowledgement as create, then its read location.</summary>
    public async Task RecipientAsync(Guid operationId, IReadOnlyList<string> recipient, CancellationToken ct)
    {
        var update = await anis.DirectRecharges.UpdateRecipientAsync(operationId,
            new UpdateDirectRechargeRecipientRequest { Recipient = ToInputs(recipient) }, ct);
        Show(update.Accepted);
        Console.WriteLine($"Correction accepted; read later at {update.Location} or wait for a webhook. Read with: recharge-status {operationId:D}");
    }

    private async Task HandleRechargeAsync(ProductIntent<CreateDirectRechargeRequest> intent, DirectRechargeResult outcome, CancellationToken ct)
    {
        if (outcome is DirectRechargeOutcomeUnknown { Cause: DryRunException })
            throw new DryRunException();

        switch (outcome)
        {
            case DirectRechargeFinal final:
                await journal.RecordOutcomeAsync(RechargeKind, intent, final.Recharge.Status.ToString(), ct);
                Section($"FINAL — {final.Recharge.Status}");
                Show(Mask(final.Recharge));
                break;

            case DirectRechargeProcessing { Recharge.Status: DirectRechargeStatus.SuspendedDataError } paused:
                await journal.RecordOutcomeAsync(RechargeKind, intent, "suspended", ct);
                Section($"PAUSED — the recipient details are wrong ({paused.Recharge!.Reason}); correct them with: recipient {intent.OperationId:D} Key=value ...");
                break;

            case DirectRechargeProcessing processing:
                await journal.RecordOutcomeAsync(RechargeKind, intent, "processing", ct);
                Section($"PROCESSING — resume after {processing.RetryAfter.TotalSeconds:F0}s with: recharge-resume {intent.OperationId:D}");
                break;

            case DirectRechargeOutcomeUnknown unknown:
                await journal.RecordOutcomeAsync(RechargeKind, intent, "unknown", ct);
                Section($"OUTCOME UNKNOWN ({unknown.Cause.GetType().Name}) — it may have gone through. Resume after {unknown.SuggestedDelay.TotalSeconds:F0}s with: recharge-resume {intent.OperationId:D}. Never create it again under a new id.");
                break;

            case DirectRechargeNotPlaced notPlaced:
                await journal.RecordOutcomeAsync(RechargeKind, intent, "not-placed", ct);
                Section("NOT PLACED — nothing was charged. Fix the cause, then create again with a NEW operation id.");
                Refusal.Print(notPlaced.Refusal);
                break;
        }
    }

    // ---- Notifications ----------------------------------------------------------------------------

    public async Task WebhookStatusAsync(CancellationToken ct) => Show(await anis.WebhookEndpoint.GetAsync(ct));

    public async Task WebhookSetAsync(string url, CancellationToken ct)
        => Show(await anis.WebhookEndpoint.SetAsync(new SetWebhookEndpointRequest { Url = url }, ct));

    public async Task WebhookTestAsync(CancellationToken ct) => Show(await anis.WebhookEndpoint.TestAsync(ct));

    public async Task WebhookEventsAsync(WebhookDeliveryStatus? status, CancellationToken ct)
    {
        await foreach (var delivery in anis.WebhookEvents.GetWebhookEventsAsync(new WebhookEventsQuery { Status = status }, ct))
            Show(delivery);
    }

    public async Task WebhookRedeliverAsync(Guid eventId, CancellationToken ct) => Show(await anis.WebhookEvents.RedeliverAsync(eventId, ct));

    // ---- Helpers ----------------------------------------------------------------------------------

    private async Task<MeterPackage> FindPackageAsync(Guid walletId, Guid packageId, CancellationToken ct)
    {
        await foreach (var package in anis.MeterCharges.GetMeterPackagesAsync(walletId, ct))
        {
            if (package.Id == packageId)
                return package;
        }

        throw new InvalidOperationException($"Package {packageId} is not offered to wallet {walletId}.");
    }

    private static List<DirectRechargeRecipientInput> ToInputs(IReadOnlyList<string> pairs)
        => [.. pairs.Select(pair =>
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);

            return split > 0
                ? new DirectRechargeRecipientInput { Key = pair[..split], Value = pair[(split + 1)..] }
                : throw new ArgumentException($"A recipient field is written Key=value, not '{pair}'.");
        })];

    private MeterCharge Mask(MeterCharge charge)
        => showSecrets || charge.Receipt is null
            ? charge
            : charge with
            {
                Receipt = charge.Receipt with
                {
                    Tokens = [.. charge.Receipt.Tokens.Select(token => token with { Code = MaskValue(token.Code) })],
                },
            };

    private DirectRecharge Mask(DirectRecharge recharge)
        => showSecrets
            ? recharge
            : recharge with { Recipient = [.. recharge.Recipient.Select(value => value with { Value = MaskValue(value.Value) })] };

    private static string MaskValue(string value)
        => value.Length <= 4 ? "****" : value[..2] + new string('*', value.Length - 4) + value[^2..];

    private static void Section(string title) => Console.WriteLine($"\n=== {title}");

    private static void Show<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Print));
}
