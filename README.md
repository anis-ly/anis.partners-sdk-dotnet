# Anis.Partners — .NET SDK

Version **2.0.0**. **1.x is deprecated — upgrade.**

The .NET client for the Anis Partner API. Targets **net8.0** and **net10.0**.

Every request is signed, every answer Anis signs is verified, and the operations are typed. The parts that are easy
to get dangerously wrong — the idempotency key, the credential-once rule, recovery after a timeout, response
verification — are shaped so you cannot get them wrong quietly.

```bash
dotnet add package Anis.Partners --version 2.0.0
```

- [Getting started](docs/getting-started.md) — enrollment, registration (one application or several), first call
- [Orders and recovery](docs/orders-and-recovery.md) ← read this before you take money
- [Routes and permissions](docs/routes-and-permissions.md) — every route, its scope and its limits
- [Errors](docs/errors.md)
- [Security and key custody](docs/security.md)
- [Observability](docs/observability.md) — logs, metrics and traces, on by default
- [Working sample](samples/Anis.Partners.Sample.Console/README.md) — a command-line integration that drives every route

---

## Why you want this package

The Anis Partner API does not use an API key. Every request is signed with **RFC 9421 HTTP Message
Signatures** over P-256/SHA-256, the body is bound in with an **RFC 9530 `Content-Digest`**, and the
signature must be **IEEE P1363** — the DER encoding of the same signature is refused. Every answer that moves
money, delivers card codes or establishes or stops access is signed too — orders, order reads, both reveals,
access stops, enrolment and
the signature self-check, the success and every refusal — and the contract requires clients to **discard** any of
those they cannot verify. The information reads (profile, wallets, catalogue, owned cards) are answered unsigned.

None of that fits an "auth provider" hook, which sets one header and never sees the method, authority,
path, query or body bytes. All of those are signed. Get one wrong and you get a `401` with — correctly — no
hint as to which.

## Register it

```csharp
builder.Services
    .AddAnisPartners(builder.Configuration.GetSection("AnisPartners"))      // Authority, SignatureLifetime, …
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/partner-key.pem", keyId));
```

Then take `IAnisPartnersClient` from the container. Settings can also be given in code — see
[Getting started](docs/getting-started.md#2-register-the-client). They are validated when the host starts.

`WithSigner` is a separate step on purpose: there is no default, because there is no safe guess about where
your private key lives. It can be any custody — `IRequestSigner` takes bytes and returns 64 bytes.

## Use it

```csharp
var profile = await anis.Profile.GetAsync(ct);

await foreach (var wallet in anis.Wallets.ListAsync(ct))       // cursor paging, handled
    Console.WriteLine($"{wallet.Name} {wallet.Balance}");

await foreach (var card in anis.Catalogue.ListCardsAsync(walletId, subcategoryId, ct))   // every page, followed for you
    Console.WriteLine($"{card.Name?.En} {card.UnitPrice}");

var catalogueCard = await anis.Catalogue.GetCardAsync(walletId, cardId, ct);
var ownedCard = await anis.OwnedCards.GetAsync(walletId, soldCardId, ct);
Console.WriteLine($"{ownedCard.SoldCardId} {ownedCard.Card?.FaceValue} {ownedCard.Card?.Category?.Name?.En}");

var credential  = await anis.OwnedCards.RevealAsync(walletId, soldCardId, ct);
var credentials = await anis.OwnedCards.RevealInvoiceAsync(walletId, invoiceId, ct); // all or none, max 100
```

To stop access, supply a required reason (1–500 characters after trimming). These calls need no scope and
verify their signed answers. Resumption is staff-only; revocation is terminal.

```csharp
var suspended = await anis.Access.SuspendApplicationAsync("Pause while investigating an incident", ct);
```

To revoke one key while the application is admitted:

```csharp
var revoked = await anis.Access.RevokeKeyAsync(keyId, "Key exposed", ct);
```

`MaskedCard` uses `SoldCardId` and `OperationId`; product details are in `CardSummary` (`Card`). The same summary
appears in reveals and order `SoldCards`. Catalogue cards include `Logo` and `Category`; subcategory lists include
`Description`. Wallet currency is `Balance.Currency`. All amounts use `Money`, a nonnegative decimal string at
scale three with an uppercase three-letter currency. Owned-card routes use `/owned-cards`.

Signed answers require `tag="anis-response-v2"` and bind both request signature headers. The verifier refuses
compromised keys, signatures after a key's retirement and signatures before any published key's activation.
The key document is cached for five minutes by default and at most; an unknown key triggers a refresh at most
once every 30 seconds. Retired keys remain published for 90 days after retirement. See
[Security](docs/security.md) for the closed verification failure vocabulary.

Buying is deliberately less convenient — see [Orders and recovery](docs/orders-and-recovery.md):

```csharp
var operationId = Guid.NewGuid();                  // yours; persist it, with the request, BEFORE sending
var request = new CreateOrderRequest
{
    CardId            = card.Id,
    Quantity          = 2,
    ExpectedUnitPrice = card.UnitPrice!.Value,                 // the price THIS wallet pays
    ExpectedTotal     = card.UnitPrice!.Value.Multiply(2),     // exact decimal, never a double
};
await db.RecordOrderIntentAsync(operationId, walletId, request, ct);

var outcome = await anis.Orders.CreateAsync(walletId, operationId, request, ct);

switch (outcome)
{
    case OrderCompleted { CodesWithheld: true } w:
        await support.ReportWithheldAsync(operationId, ct); // paid, but no codes released: never buy it again
        break;
    case OrderCompleted c:
        await vault.StoreAsync(c.Credentials, ct); // paid; codes here, ONCE — store them first
        break;
    case OrderReplayed r:
        await vault.FetchByInvoiceAsync(r.Order.InvoiceId!.Value, ct); // paid earlier; revealing again needs cards:reveal
        break;
    case OrderProcessing p:
        await scheduler.ResumeAfterAsync(operationId, p.RetryAfter, ct);
        break;
    case OrderOutcomeUnknown u:
        await scheduler.ResumeAfterAsync(operationId, u.SuggestedDelay, ct);
        break;
    case OrderNotPlaced n:
        await CloseAsNotPlacedAsync(operationId, n.Refusal, ct); // NOT charged: fix cause, then use a new id
        break;
    default:
        throw new InvalidOperationException($"Unhandled order outcome {outcome}");
}
```

Every outcome is a value — a refusal, a timeout and an answer that could not be verified included — so one
`switch` handles them all and `ResumeAsync` returns the same five cases. The call throws only for a mistake in the
call itself (an invalid request, a signer that failed before anything was sent) or when your own token cancels it.
(C# does not check a `switch` over a class hierarchy for completeness, hence the `default` case.)

## Refusals built into the design

**The SDK will not generate your idempotency key.** `operationId` is a required parameter. If the SDK made
one up, a dropped response plus an ordinary retry would mint a second key and buy the cards twice.

**`OrderResult` is five records, not one object with a nullable credential list.** Only the response that
*first* reports completion carries the credentials — and the SDK returns any response that carries them as
`OrderCompleted`, so they can never be hidden behind the "already delivered" case.

**Every order outcome tells you the next safe action.** `OrderNotPlaced` means nothing was bought (a new attempt takes a new id); `OrderOutcomeUnknown` means resume the same id; `OrderProcessing` also needs a resume. On a resume, a refusal that is not the order's recorded answer comes back as `OrderOutcomeUnknown`, because it says nothing about the earlier attempt. So do `invalid_credentials`, `insufficient_scope` and `wallet_not_granted` on a create: restore access, then resume the same id.

**Observability is on by default and leaks nothing.** One `ActivitySource` and one `Meter`, both named
`Anis.Partners.Sdk`, plus structured logging through your own `ILoggerFactory`. Tests capture every log
line, span tag and metric tag during a reveal, an order and an enrollment and assert that no voucher,
serial, signature, signature base, nonce or enrollment token appears in any of them.

**Response verification has no off switch.** The contract says clients discard what they cannot verify, so
there is no option to disable it and no way to ship with it accidentally off. Which answers are verified is fixed
per route, in the SDK's route table, never guessed from the answer: a signed route's answer that arrives with no
signature is discarded (`SignatureMissing`), and an information read's answer is returned as it arrives. On a signed
read, an unverifiable response raises `UnverifiableResponseException`; order calls return `OrderOutcomeUnknown`. Both
discard the unverified content.

## What is covered

All 22 published routes, in seven operation groups — `Access`, `Profile`, `Wallets`, `Catalogue`, `Orders`,
`OwnedCards`, `Diagnostics` — plus `AnisEnrollmentClient` for bootstrap. 39 public error codes, generated
from the error catalogue, each pinned by a test to its exception type and its order outcome. The signature
self-check (`Diagnostics`) needs `diagnostics:use` and is the right first call when a signature will not
verify, because it reports the exact facts the gateway built its base from.

## How it is proven

Not "reviewed and looks right":

| Evidence | What it holds |
|---|---|
| **Request vectors** | The SDK signs, then **Anis's own** parser, base builder and verifier run over the result. A vector exists only if Anis rebuilt a **byte-identical** base and accepted the signature. They also record the body each nonce mutation sends — zero bytes on a reveal, `{}` on the self-check — a rule the **gateway** enforces, so the live run is its proof. |
| **Response vectors** | Signed by the **gateway's production signer**. Accept and reject cases — each rejection a single-change derivation, so a failure names the rule. They include the real repeat of a completed order (`201` + `Idempotency-Replayed`), the recovered completion, and a recorded refusal. |
| **Enrollment proof vectors** | The SDK builds the possession proof, and **Anis's own** proof check rebuilds the message byte for byte and accepts it; the DER form of the same signature is refused. |
| **Safety-code vectors** | A real key, its thumbprint, the 16-character code Anis staff ask you to read, and every way an entry may be typed (case, dashes, spaces, look-alike letters, the full thumbprint, wrong and short input), derived by an independent Python implementation. |
| **Contract drift tests** | The route table is diffed against `contracts/partner-public-v1.json` **in both directions**, request profiles and signed answers included; the error codes against `contracts/error-catalogue.json`. |
| **Pipeline tests** | Every order outcome as the live gateway sends it, every refusal code, cursor paging, the body of each nonce mutation, the idempotency header, money at scale three, settings binding, several applications in one host, a host that retries every call, an order that times out, enrollment end to end, and a tampered response being discarded. |
| **Telemetry redaction** | Every log line, span tag and metric tag captured, asserted to contain no secret — and asserted non-empty, so it cannot pass vacuously. |
| **Python cross-checks** | Independent implementations reproduce every request base, every response verdict, every enrollment proof and every safety code — proof the contract is expressible outside .NET. |

```bash
dotnet test Anis.Partners.Sdk.slnx      # on net8.0 AND net10.0
```

The suites read the vectors from `tests/Anis.Partners.Sdk.Tests/vectors` — the same published set as
<https://developers.anis.ly/vectors/index.json> (override with `-p:ConformanceVectors=<path>`) — and fail loudly
rather than run zero vectors. Group manifests, when present, determine the expected inventory; no vector count is
hard-coded. The response corpus requires regeneration for the v2 profile before its conformance gate can pass.

### Proven live

On 2026-09-23 this SDK and its sample ran against a complete Anis stack: enrollment with a real key through
staff confirmation, all 19 routes (every answer verified — before 1.4.0, when Anis still signed the information
reads), twelve kinds of refusal, a staff-set orders
limit, a key revoked in the middle of a run, the overlap while a key is replaced, a caller outside the
allowed networks, and the same run on .NET 8. Four defects that no vector could see were found there and
fixed before this version.

On 2026-10-07 1.4.0 ran against a complete stack with the gateway that signs only money, card and key answers:
enrollment through staff confirmation, the eight information reads answered unsigned and accepted, an order and its
replay, both reveals, a signed refusal (a changed price) and an unsigned one (a wallet not granted).

### What is NOT yet proven live

Version 2.0.0 has no live certification from this lane; the dated runs above cover 1.x.

- **Orders whose outcome is unknown** — a `202` still processing, a timeout, an owner-side outage, the
  recovered completion. They cannot be provoked by hand on a healthy stack; they are covered by Anis's
  composed end-to-end suite and by this SDK's own tests.
- **Refusals that need special account setup** — a reveal the owner forbids, the owner's daily and total
  spending limits, debt consent, an invoice of more than 100 cards, a disabled or expired wallet, an
  inactive account. Each is mapped and tested here; none has yet been seen from a live stack.
- **Paging past the first page, a rotation of Anis's own response key, telemetry into a real collector** —
  tested here, not yet live.

## Regenerating the error types

`src/.../Errors/PartnerErrors.g.cs` is generated. The catalogue names `sdk-constants` as one of its own
generation targets, so the direction is fixed: the catalogue is the source.

```bash
python3 tools/generate-errors.py
```

Never hand-edit the generated file — the drift test will fail on it, which is the point.

## License

[MIT](LICENSE) © 2026 Aniscom for Technical Services (Anis).
