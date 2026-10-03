using System.Globalization;
using Anis.Partners.Sample;
using Anis.Partners.Sdk;
using Anis.Partners.Sdk.DependencyInjection;
using Anis.Partners.Sdk.Enrollment;
using Anis.Partners.Sdk.Errors;
using Anis.Partners.Sdk.Signing;
using Anis.Partners.Sdk.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A complete partner integration, one command per thing a partner does. It is in the solution so it must
// compile: a sample that no longer builds is worse than no sample.
//
// Settings come from appsettings.json, then environment variables (AnisPartners__Authority, Sample__KeyId),
// then --Section:Key=value arguments. See the README next to this file for every command.

var (command, positional, flags, overrides) = Arguments.Parse(args);

if (command is null or "help" or "--help")
{
    Console.WriteLine(Arguments.Usage);
    return 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(overrides)
    .Build();

var sample = configuration.GetSection(SampleSettings.SectionName).Get<SampleSettings>() ?? new SampleSettings();
var dryRun = flags.ContainsKey("dry-run");
var preview = flags.ContainsKey("preview");
var wire = () => new WireLog(dryRun, sample.ForwardedFor, preview);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;

try
{
    if (command is "enrol" or "enrol-status")
        return await EnrolAsync(command, configuration, flags, sample, wire, ct);

    var services = new ServiceCollection();

    services.AddLogging(logging => logging
        .AddSimpleConsole(console => console.SingleLine = true)
        .SetMinimumLevel(flags.ContainsKey("verbose") ? LogLevel.Debug : LogLevel.Warning));

    // Beneath the SDK's own handlers, so it sees each request exactly as signed.
    services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(wire));

    services
        .AddAnisPartners(configuration.GetSection(AnisPartnersClientOptions.SectionName))
        .WithSigner(_ => EcdsaP256Signer.FromPemFile(
            sample.KeyFile,
            sample.KeyId ?? throw new InvalidOperationException("Set Sample:KeyId to the key id Anis issued at enrollment.")));

    await using var provider = services.BuildServiceProvider();

    var commands = new SampleCommands(
        provider.GetRequiredService<IAnisPartnersClient>(),
        provider.GetRequiredService<ISigningKeySource>(),
        new OrderJournal(sample.OrdersFolder),
        showSecrets: flags.ContainsKey("show-secrets"));

    Task run = command switch
    {
        "tour" => commands.TourAsync(ct),
        "profile" => commands.ProfileAsync(ct),
        "wallets" => commands.WalletsAsync(ct),
        "wallet" => commands.WalletAsync(Arguments.Id(positional, 0, "wallet id"), ct),
        "categories" => commands.CategoriesAsync(Arguments.Id(positional, 0, "wallet id"), ct),
        "subcategories" => commands.SubcategoriesAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "category id"), ct),
        "subcategory" => commands.SubcategoryAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "subcategory id"), ct),
        "cards" => commands.CardsAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "subcategory id"), ct),
        "order" => commands.OrderAsync(
            Arguments.Id(positional, 0, "wallet id"),
            Arguments.Id(positional, 1, "subcategory id"),
            Arguments.Id(positional, 2, "card id"),
            int.Parse(Arguments.At(positional, 3, "quantity"), CultureInfo.InvariantCulture),
            flags.TryGetValue("operation", out var operation) ? Guid.Parse(operation!) : null,
            flags.TryGetValue("expected-unit-price", out var price) ? decimal.Parse(price!, CultureInfo.InvariantCulture) : null,
            flags.GetValueOrDefault("reference"),
            flags.ContainsKey("use-allowed-debt"),
            ct),
        "resume" => commands.ResumeAsync(Arguments.Id(positional, 0, "operation id"), ct),
        "order-status" => commands.OrderStatusAsync(Arguments.Id(positional, 0, "operation id"), ct),
        "owned" => commands.OwnedCardsAsync(Arguments.Id(positional, 0, "wallet id"), ct),
        "owned-card" => commands.OwnedCardAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "sold card id"), ct),
        "reveal" => commands.RevealAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "sold card id"), ct),
        "reveal-invoice" => commands.RevealInvoiceAsync(Arguments.Id(positional, 0, "wallet id"), Arguments.Id(positional, 1, "invoice id"), ct),
        "diagnostic" => commands.DiagnosticAsync(ct),
        "signing-keys" => commands.SigningKeysAsync(ct),
        _ => throw new ArgumentException($"Unknown command '{command}'. Run with no arguments for the list."),
    };

    await run;

    return 0;
}
catch (DryRunException)
{
    Console.WriteLine("\nDry run: nothing was sent.");
    return 0;
}
catch (AnisApiException failure)
{
    Refusal.Print(failure);
    return 2;
}
catch (UnverifiableResponseException failure)
{
    Console.WriteLine($"\nUNVERIFIABLE RESPONSE ({failure.Failure}) — discarded, its content was not used.");
    return 3;
}
catch (Exception failure) when (failure is TaskCanceledException or HttpRequestException)
{
    // No answer at all. On an order the purchase may have happened; the journal holds the id to resume.
    var why = failure is TaskCanceledException
        ? cancellation.IsCancellationRequested ? "stopped by you" : "timed out"
        : $"connection failed: {failure.Message}";

    Console.WriteLine($"\nNO ANSWER ({why}).");
    Console.WriteLine(command is "order" or "resume"
        ? "The order's outcome is UNKNOWN — it may have gone through. Resume it with the SAME operation id: "
          + "`resume <operation>` (the id is printed above and kept in the order journal). Never place it again under a new id."
        : "Nothing was changed by a read; run it again.");
    return 4;
}
catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or FormatException or IOException)
{
    Console.WriteLine($"\n{failure.Message}");
    return 1;
}

static async Task<int> EnrolAsync(
    string command,
    IConfiguration configuration,
    IReadOnlyDictionary<string, string?> flags,
    SampleSettings sample,
    Func<WireLog> wire,
    CancellationToken ct)
{
    var authority = configuration.GetSection(AnisPartnersClientOptions.SectionName)["Authority"] is { } value
        ? new Uri(value)
        : throw new InvalidOperationException("Set AnisPartners:Authority.");

    var invitation = Guid.Parse(flags.GetValueOrDefault("invitation") ?? throw new ArgumentException("--invitation <id> is required."));
    var token = flags.GetValueOrDefault("token") ?? throw new ArgumentException("--token <enrollment token> is required.");

    using var keyClient = new HttpClient(wire()) { BaseAddress = authority };
    var keys = new HttpSigningKeySource(keyClient, TimeSpan.FromMinutes(10), TimeProvider.System);

    using var enrollment = AnisEnrollmentClient.Create(authority, invitation, token, keys, wire());

    if (command == "enrol-status")
    {
        await SampleCommands.EnrolStatusAsync(enrollment, ct);
        return 0;
    }

    await SampleCommands.EnrolAsync(
        enrollment,
        flags.GetValueOrDefault("key-file") ?? sample.KeyFile,
        flags.TryGetValue("days", out var days) ? int.Parse(days!, CultureInfo.InvariantCulture) : 365,
        ct);

    return 0;
}

/// <summary>Prints a refusal the way a partner's on-call needs it: what happened, and what to do next.</summary>
internal static class Refusal
{
    public static void Print(AnisApiException failure)
    {
        Console.WriteLine($"""

            REFUSED   {failure.RawCode} ({(int)failure.Status})  [{failure.GetType().Name}]
            request   {failure.RequestId}
            replayed  {(failure.IsReplayed ? "yes — the recorded answer of an earlier attempt with this operation id" : "no")}
            retry     {(failure.RetryAfter is { } wait ? $"after {wait.TotalSeconds:F0}s (signed Retry-After)" : failure.IsRetryable ? "yes, with backoff" : "no")}
            do        {Advice(failure)}
            """);
    }

    private static string Advice(AnisApiException failure) => failure switch
    {
        PriceChangedException => "re-read the catalogue and place a NEW order with a NEW operation id",
        InsufficientBalanceException => "top up the wallet, then place a NEW order with a NEW operation id",
        OutOfStockException => "re-read the catalogue; place a NEW order for an available card or quantity",
        LimitExceededException => "do not poll: the owner allowance is used up; try later with a NEW operation id",
        RateLimitedException => "wait for Retry-After, then send again (an order: RESUME the same operation id, never a new one)",
        IdempotencyConflictException => "this operation id belongs to a different order; use a new id",
        DependencyUnavailableException => "a read: retry shortly. An order: resume the SAME operation id",
        AuthorizationException => "needs a change on Anis's side (scope, allowed network, account or wallet state)",
        ResourceNotFoundException => "check the id, and that the wallet is granted to this application",
        InvalidCredentialsException => "check the key id, the key file and the clock; run `diagnostic`",
        ReplayDetectedException => "the same signed bytes arrived twice. A read: call again. An order: the first copy may have bought — resume the SAME operation id",
        ValidationFailedException => "the request broke a contract rule (the field is never named); see docs/errors.md",
        EnrollmentRefusedException => "see docs/getting-started.md for the enrollment refusals",
        _ => "see docs/errors.md",
    };
}

/// <summary>A small argument parser: <c>command positional... --flag [value]</c> plus <c>--Section:Key=value</c>.</summary>
internal static class Arguments
{
    // Flags that take no value.
    private static readonly HashSet<string> Switches = ["dry-run", "preview", "verbose", "show-secrets", "use-allowed-debt"];

    public const string Usage = """
        Anis Partner SDK sample — one command per thing a partner does.

          enrol --invitation <id> --token <token> [--key-file <path>] [--days 365]
          enrol-status --invitation <id> --token <token>

          tour                                        every read route once, following real ids
          profile | wallets | wallet <wallet>
          categories <wallet> | subcategories <wallet> <category>
          subcategory <wallet> <subcategory> | cards <wallet> <subcategory>
          order <wallet> <subcategory> <card> <quantity> [--reference <text>] [--use-allowed-debt]
                [--operation <id>] [--expected-unit-price <amount>]
          resume <operation>                          same id, same recorded body (also an idempotent repeat)
          order-status <operation>
          owned <wallet> | owned-card <wallet> <sold card>
          reveal <wallet> <sold card> | reveal-invoice <wallet> <invoice>
          diagnostic | signing-keys

        Options: --dry-run (build and sign, print, send nothing)   --preview (send the reads, hold the first change)
                 --show-secrets   --verbose
        Settings: appsettings.json, env (AnisPartners__Authority, Sample__KeyId), or --AnisPartners:Authority=...
        """;

    public static (string? Command, List<string> Positional, Dictionary<string, string?> Flags, string[] Overrides) Parse(string[] args)
    {
        var positional = new List<string>();
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        var overrides = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains(':', StringComparison.Ordinal))
            {
                overrides.Add(arg);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg[2..];
                flags[name] = Switches.Contains(name) || i + 1 >= args.Length ? null : args[++i];
            }
            else
            {
                positional.Add(arg);
            }
        }

        var command = positional.Count > 0 ? positional[0] : null;

        return (command, positional.Skip(1).ToList(), flags, [.. overrides]);
    }

    public static string At(List<string> positional, int index, string name)
        => index < positional.Count ? positional[index] : throw new ArgumentException($"Missing <{name}>.");

    public static Guid Id(List<string> positional, int index, string name)
        => Guid.TryParse(At(positional, index, name), out var id) ? id : throw new ArgumentException($"<{name}> must be a UUID.");
}
