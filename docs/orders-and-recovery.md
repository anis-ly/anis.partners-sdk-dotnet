# Orders and recovery

The part where money moves. Read it once properly.

## The operation id is yours

```csharp
var operationId = Guid.NewGuid();   // or Guid.CreateVersion7() on .NET 9+
await db.RecordOrderIntentAsync(operationId, walletId, request, ct);   // BEFORE the call

var outcome = await anis.Orders.CreateAsync(walletId, operationId, request, ct);
```

`operationId` becomes the public `Idempotency-Key`. It is globally unique across Anis — not scoped to your
application — and it is the identity Anis uses to recognise the same purchase twice.

**The SDK will not generate it for you.** If it did, a dropped response plus an ordinary retry would mint a
second key, and a second key is a second purchase. Persisting it — with the exact request — before the call
is what makes every later retry safe, including one that happens after a process restart.

## The price to send

```csharp
var card = /* from anis.Catalogue.ListCardsAsync(walletId, subcategoryId) */;

var request = new CreateOrderRequest
{
    CardId            = card.Id,
    Quantity          = 2,
    ExpectedUnitPrice = card.UnitPrice!.Value,              // exactly as read
    ExpectedTotal     = card.UnitPrice!.Value.Multiply(2),  // exact decimal, never a double
    ExternalReference = "your-order-123",                   // optional, 1–100 characters
};
```

Send **`unitPrice`** — the price this wallet pays now, and the one Anis checks the order against. The other
price members (`businessPrice`, `personalPrice`, `specialOfferPrice`) are for display: on some wallets they
differ from `unitPrice`, and an order sent at one of them is refused as `price_changed`. A card with no
`unitPrice` cannot be sold to this wallet.

The total must equal unit times quantity **computed without floating point**. `Money` is a `decimal` and
`Multiply` is exact, and the SDK checks the arithmetic, the currencies and `Quantity >= 1` before the request
leaves — a mistake there is an `ArgumentException` at the call site, not a round trip.

`ExternalReference` is echoed onto the owner's invoice and read by people, so the gateway accepts only
letters, digits, space and `- _ . : / #`; anything else is refused with `validation_failed`.

`UseAllowedDebt` defaults to `false` and is never switched on for you. When covering the purchase would need
the account's allowed debt, Anis refuses with `allowed_debt_consent_required` (402); place a new order with
`UseAllowedDebt = true` only if you intend to spend debt.

## Five outcomes

```csharp
switch (outcome)
{
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

| You get | When | What it carries |
|---|---|---|
| `OrderCompleted` | a fresh `201`; or the `200` a **resume** receives when the first answer was lost | the credentials, in `Credentials` |
| `OrderProcessing` | `202` | `RetryAfter`, `Location` |
| `OrderReplayed` | the same id sent again after it completed: the **original** status (usually `201`) with `Idempotency-Replayed: true` | the operation id, the state and the invoice id — no credentials |
| `OrderOutcomeUnknown` | no answer (timeout, lost connection), an answer that could not be verified, a refusal that reached no decision, a rate limit, or — on a resume — a refusal that is not the order's recorded answer | `SuggestedDelay`, and `Cause` for your logs |
| `OrderNotPlaced` | a refusal that closes the order | `Refusal`: the typed refusal with `Code`, `RequestId`, `RetryAfter`, `IsReplayed` |

Five records rather than one object with nullable members, because the difference between them decides whether you have the credentials — and whether the order was bought.

## The credential-once rule

**Only the response that FIRST reports the order completed carries the credentials** — the fresh `201`, or
the `200` a resume receives when the first answer was lost. The SDK returns any order response that carries
credentials as `OrderCompleted`, so they are never hidden behind another type.

A later repeat of the same id returns `OrderReplayed` with **no credentials**, and `GET /v1/orders/{id}` is
state only. Reading them again is a *reveal* — `RevealInvoiceAsync(walletId, replayed.Order.InvoiceId!.Value)` — and
a reveal needs `cards:reveal`, a scope your application may not have.

So:

```csharp
case OrderCompleted completed:
    await vault.StoreAsync(completed.Credentials, ct);   // do this first, before anything that can throw
    break;
```

## Recovery is a POST, not a GET

A `202` carries `Location: /v1/orders/{operationId}`, so the instinct is to poll it. **That is not
recovery.** A GET reports state and **dispatches nothing** — an operation that was never re-driven stays
exactly where it is, forever.

The protocol is an *exact repeated signed POST*:

```csharp
case OrderProcessing processing:
    await Task.Delay(processing.RetryAfter, ct);

    var resumed = await anis.Orders.ResumeAsync(walletId, operationId, request, ct);  // same id, same body
    // resumed is one of the same five cases: handle it with the same switch
    break;
```

`ResumeAsync` and `CreateAsync` are the same call on the wire. Two names because the intent differs and the
wrong one is expensive: `CreateAsync` with a **new** operation id after a `202` buys the cards a second
time. `ResumeAsync` has no overload that mints an id.

The SDK caches nothing for you here. You persisted the intent in step one, so you supply the same request
on resume — a copy held by the SDK would be gone exactly when recovery runs.

Resume the same way when a call **times out** on your side, when the connection drops, and when the response
**cannot be verified**: in each case you do not know what happened, and a resume is the only move that is
always safe. The SDK returns all three as `OrderOutcomeUnknown` — there is nothing to catch. It also counts each
as order outcome `unknown` in its metrics and logs a warning naming the operation id — see
[Observability](observability.md).

Two things still throw, and neither is an order outcome: a mistake in the call itself (`ArgumentException` for a
bad request, `RequestSigningException` when your signer failed — nothing was sent), and your own cancellation
(`OperationCanceledException` while *your* token is cancelled). After a cancellation the outcome is just as
unknown as after a timeout: resume the same id when you start again.

**Recovery exhausted.** A resume can answer `OrderProcessing` whose `Order.Status` is
`OrderStatus.RecoveryExhausted` (and `GetAsync` can report it too). It means Anis tried to learn the
outcome from the owner to its limit and could not. The order is **not** failed and may have completed. Do
not place it again under a new id. Keep resuming the same id, but slowly — minutes, not seconds: every resume
asks the owner again, and one of them may return the completed order **with its card codes**
(`OrderCompleted`). Tell Anis the operation id as well, so the order can be resolved on their side.

## When an order is refused: was it placed?

The SDK answers that for you, by the case it returns:

- **`OrderNotPlaced`** — nothing was bought and nothing was charged. Fix the cause (`n.Refusal` says which),
  then place the order again under a **new** operation id.
- **`OrderOutcomeUnknown`** — no decision was reached and the purchase **may still complete**. Resume with the
  **same** operation id and the same body. Never a new id. A **rate limit** lands here too: a first attempt
  refused for its rate placed nothing, but the SDK cannot tell that from a copy of an earlier attempt (a
  retry handler in your host, or a create sent again after a timeout) that is still selling, so resume the same
  id once the wait is over (`SuggestedDelay` is the signed `Retry-After`, or 5 seconds when there is none). On a **resume**, a refusal that is not the order's
  recorded answer lands here too: it was decided before Anis looked at the order, so the earlier attempt's
  outcome is still unknown.

`n.Refusal.IsReplayed` is `true` when the refusal is the **recorded** answer of an earlier attempt with the
same id — typically what a resume after a timeout returns when the order had in fact been refused. The order is
closed; the same id will return this answer forever.

This table classifies a first attempt. On a resume, every fresh (non-replayed) refusal is `OrderOutcomeUnknown`; a refusal marked `Idempotency-Replayed` is the recorded `OrderNotPlaced` answer.

| Code | Status | Refusal type | Case on create | What to do |
|---|---|---|---|---|
| `price_changed` | 409 | `PriceChangedException` | OrderNotPlaced | Re-read the catalogue; new order at the new `unitPrice`, new id |
| `insufficient_balance` | 409 | `InsufficientBalanceException` | OrderNotPlaced | Top up; new order, new id |
| `quantity_unavailable` | 409 | `OutOfStockException` | OrderNotPlaced | Sold out, not enough stock, or outside the card's own minimum/maximum. Re-read the catalogue; new order |
| `card_unavailable` | 409 | `OutOfStockException` | OrderNotPlaced | The card is not sellable to this wallet now (or does not exist). Re-read the catalogue |
| `allowed_debt_consent_required` | 402 | `AnisApiException` | OrderNotPlaced | New order with `UseAllowedDebt = true` — only if you mean to spend debt |
| `owner_limit_exceeded` | 409 | `LimitExceededException` | OrderNotPlaced | The owner's allowance is used up. **Do not poll**; new order once it changes |
| `daily_limit_exceeded` | 429 | `LimitExceededException` | OrderNotPlaced | The owner's daily limit. No reset time is given; **do not poll** |
| `purchase_not_allowed` | 403 / 409 | `AuthorizationException` | OrderNotPlaced | An owner rule forbids this purchase |
| `wallet_disabled`, `wallet_expired`, `business_subscription_required`, `account_inactive`, `binding_not_authorized` | 403 / 409 | `AuthorizationException` | OrderNotPlaced | The owner account, wallet or subscription needs attention on Anis's side |
| `idempotency_conflict` | 409 | `IdempotencyConflictException` | OrderNotPlaced | This id already belongs to a **different** order — a bug on your side. That earlier order is untouched; use a new id |
| `validation_failed`, `currency_not_supported` | 422 | `ValidationFailedException` | OrderNotPlaced | A contract rule was broken; the field is never named — see [Errors](errors.md) |
| `wallet_not_granted` | 404 | `ResourceNotFoundException` | OrderNotPlaced | The wallet is not granted to this application (or does not exist) |
| `insufficient_scope` | 403 | `AuthorizationException` | OrderNotPlaced | The application lacks `orders:create` — **or** the call came from outside its allowed networks: Anis gives both the same answer on purpose (see [Routes and permissions](routes-and-permissions.md#before-a-call-is-looked-at)) |
| `source_ip_not_allowed` | 403 | `AuthorizationException` | OrderNotPlaced | Anis's own edge refused the address outright; ask Anis |
| `invalid_credentials` | 401 | `InvalidCredentialsException` | OrderNotPlaced | Key id, key or clock — see [Getting started](getting-started.md#when-a-signature-will-not-verify) |
| `replay_detected` | 409 | `ReplayDetectedException` | OrderOutcomeUnknown | The same signed bytes arrived twice (a proxy or retry resent them). The copy that arrived first was admitted and may have bought the cards: **resume with the same id**, never a new one |
| `rate_limited` | 429 | `RateLimitedException` | OrderOutcomeUnknown | Wait for `RetryAfter` (or back off), then **resume with the same id**. A first-attempt rate limit placed nothing, but the SDK cannot tell it from a resend of an attempt that is still selling, so never a new id |
| `dependency_unavailable`, `request_timeout`, `internal_error` | 503 / 504 / 500 | `DependencyUnavailableException` | OrderOutcomeUnknown | **Resume with the same id.** Never a new id |

A timeout is never `failed`. Only a definitive owner business refusal is `failed`, and a completed order
never becomes failed afterwards.
