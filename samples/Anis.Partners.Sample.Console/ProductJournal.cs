using System.Text.Json;

namespace Anis.Partners.Sample;

/// <summary>Meter charge and Direct recharge intent, persisted BEFORE the request is sent.</summary>
/// <remarks>
/// Intent is immutable and includes recipient details needed to resume the identical request. Keep this folder
/// private and encrypted at rest. Outcomes contain only a state name and never replace the saved request.
/// </remarks>
internal sealed class ProductJournal(string folder)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RecordIntentAsync<T>(string kind, Guid operationId, Guid walletId, T request, CancellationToken cancellationToken)
    {
        var path = PathOf(kind, operationId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteAtomicAsync(path, new ProductIntent<T>(operationId, walletId, request, "recorded"), overwrite: false, cancellationToken);
    }

    public async Task<ProductIntent<T>> ReadAsync<T>(string kind, Guid operationId, CancellationToken cancellationToken)
    {
        var path = PathOf(kind, operationId);
        await using var stream = File.OpenRead(path);
        var intent = await JsonSerializer.DeserializeAsync<ProductIntent<T>>(stream, Options, cancellationToken)
            ?? throw new InvalidOperationException($"The intent file for {operationId} is empty.");
        var outcomePath = OutcomePathOf(kind, operationId);
        try
        {
            await using var outcome = File.OpenRead(outcomePath);
            return intent with
            {
                Outcome = await JsonSerializer.DeserializeAsync<string>(outcome, Options, cancellationToken)
                    ?? throw new InvalidOperationException($"The outcome file for {operationId} is empty."),
            };
        }
        catch (FileNotFoundException)
        {
            return intent;
        }
    }

    public Task RecordOutcomeAsync<T>(string kind, ProductIntent<T> intent, string outcome, CancellationToken cancellationToken)
        => WriteAtomicAsync(OutcomePathOf(kind, intent.OperationId), outcome, overwrite: true, cancellationToken);

    private string PathOf(string kind, Guid operationId) => Path.Combine(folder, kind, $"{operationId:D}.json");

    private string OutcomePathOf(string kind, Guid operationId) => Path.Combine(folder, kind, $"{operationId:D}.outcome.json");

    private static async Task WriteAtomicAsync<T>(string path, T value, bool overwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using (var stream = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Same directory, so readers see a complete old or new file. Intent publication refuses competitors.
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>One persisted meter charge or Direct recharge request.</summary>
internal sealed record ProductIntent<T>(Guid OperationId, Guid WalletId, T Request, string Outcome);
