# Sample: a command-line partner integration

One command per thing a partner does, each a few lines over the SDK — enroll a key, browse, buy, recover,
reveal, check a signature. Read `SampleCommands.cs` for the integration code; `Program.cs` is only wiring.

```bash
dotnet run --project samples/Anis.Partners.Sample.Console -- help
```

## Settings

`appsettings.json` next to the program (or in the current directory), then environment variables, then
`--Section:Key=value` arguments:

| Setting | Meaning |
|---|---|
| `AnisPartners:Authority` | the authority Anis gave you (the SDK's own section — see the SDK docs) |
| `Sample:KeyFile` | your private key file (PKCS#8 PEM), written by `enrol` |
| `Sample:KeyId` | the key id Anis issued at enrollment |
| `Sample:OrdersFolder` | where order intent is persisted before each order |
| `Sample:ForwardedFor` | **test-only**, see below |

```bash
export AnisPartners__Authority=https://partners.example
export Sample__KeyId=3f2a9c14-8d6e-4b21-9f07-5c8ab2d61e43
```

## Commands

| Command | Does |
|---|---|
| `enrol --invitation <id> --token <token> [--key-file f] [--days 365] [--cidrs a,b]` | generates a P-256 key, saves the private half first (mode 600), submits the public half, proves possession, prints the key id and the fingerprint to give Anis staff |
| `enrol-status --invitation <id> --token <token>` | where the key stands: `pendingApproval` until staff confirm, then `active` |
| `tour` | every read route once, following real ids from one answer to the next — moves no money |
| `profile`, `wallets`, `wallet <w>` | identity and scopes; wallets and balances |
| `categories <w>`, `subcategories <w> <c>`, `subcategory <w> <s>`, `cards <w> <s>` | the catalogue, priced for wallet `w` |
| `order <w> <s> <card> <qty> [--reference r] [--use-allowed-debt]` | reads the card's `unitPrice`, **persists the intent**, then places the order |
| `resume <operation>` | the same id and the same recorded body, freshly signed: recovery after a `202` or a timeout, and the idempotent repeat |
| `order-status <operation>` | state only; dispatches nothing |
| `owned <w>`, `owned-card <w> <sold card>` | owned cards, masked |
| `reveal <w> <sold card>`, `reveal-invoice <w> <invoice>` | credentials (masked on screen unless `--show-secrets`) |
| `diagnostic` | the signature self-check: what the gateway saw |
| `signing-keys` | Anis's published response-signing keys |

Options on every command: `--dry-run` builds and signs the request, prints it exactly as it would go on the
wire, and sends nothing (no nonce, limit or money is spent); `--preview` sends the reads a command needs (a
price, an id) and holds back the first request that changes something, printed exactly as signed; `--verbose` shows the SDK's debug logs;
`--show-secrets` prints credentials in full.

Two options exist only to demonstrate refusals: `--operation <id>` reuses an operation id (a different order
under a used id is an `idempotency_conflict`), and `--expected-unit-price <amount>` sends a price other than
the catalogue's (a `price_changed`).

Every refusal is printed with its code, the request id to quote, whether it was replayed, and what to do next.
`order` and `resume` print a returned order outcome: COMPLETED, PROCESSING, REPLAYED, OUTCOME
UNKNOWN (with the operation id to resume) or NOT PLACED (with the refusal). An `OrderProcessing` whose status is
`RecoveryExhausted` prints RECOVERY EXHAUSTED and still needs a resume. A call that gets no answer at all (a
timeout or failed connection on a read, or your cancellation of an order) says so. Exit codes: `0` for a
returned order outcome or completed read, `2` refused by Anis, `3` unverifiable read response, `4` no
answer or caller cancellation, `1` a local error.

## The order journal

`order` writes `orders/<operation id>.json` — the wallet, the exact request, and later the outcome —
**before** the order leaves. `resume` reads it back, so recovery works from a new process with the same id
and the same body. A completed order's credentials are stored there too (mode 600). A real integration keeps
the intent in its own database and the credentials in its secret store.

## Against a local stack

A local stack's gateway admits a partner only from the application's allowed networks, and it sees this
program's real connection address. Either put that address in the application's allowed networks, or — on a
local stack whose gateway trusts the loopback proxy — set the **test-only** `Sample:ForwardedFor` to an
address inside them. That header is added by this sample only, never by the SDK, and a real Anis deployment
ignores it from an untrusted connection.
