using System.Text.Json;
using Anis.Partners.Sdk.Models;

namespace Anis.Partners.Sample;

/// <summary>Order intent, persisted BEFORE the order is sent. The partner's side of safe recovery.</summary>
/// <remarks>
/// The operation id is the purchase's identity at Anis. It is written to disk, with the exact request, before
/// the call leaves — so a crash, a timeout or a lost response is recovered by resuming the SAME id with the
/// SAME body, even from a new process. Generating a fresh id after a failure is how a card gets bought twice.
///
/// A real integration keeps this in its own database, in the same transaction as its own order row, and keeps
/// credentials in its secret store rather than a file.
/// </remarks>
internal sealed class OrderJournal(string folder)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RecordIntentAsync(Guid operationId, Guid walletId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);

        var path = PathOf(operationId);

        if (File.Exists(path))
            throw new InvalidOperationException($"Operation {operationId} already has recorded intent; resume it instead of placing it again.");

        await WriteAsync(path, new OrderIntent(operationId, walletId, request, "sent"), cancellationToken);
    }

    public async Task<OrderIntent> ReadAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var path = PathOf(operationId);

        if (!File.Exists(path))
            throw new InvalidOperationException($"No recorded intent for operation {operationId} in {folder}.");

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<OrderIntent>(stream, Options, cancellationToken)
            ?? throw new InvalidOperationException($"The intent file for {operationId} is empty.");
    }

    public async Task RecordOutcomeAsync(OrderIntent intent, string outcome, IReadOnlyList<RevealedCredential>? credentials, CancellationToken cancellationToken)
        => await WriteAsync(PathOf(intent.OperationId), intent with { Outcome = outcome, Credentials = credentials }, cancellationToken);

    private string PathOf(Guid operationId) => Path.Combine(folder, $"{operationId:D}.json");

    private static async Task WriteAsync(string path, OrderIntent intent, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(intent, Options), cancellationToken);

        // The file can hold credentials after a completion: readable by this user only.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

/// <summary>One persisted order.</summary>
internal sealed record OrderIntent(
    Guid OperationId,
    Guid WalletId,
    CreateOrderRequest Request,
    string Outcome,
    IReadOnlyList<RevealedCredential>? Credentials = null);
