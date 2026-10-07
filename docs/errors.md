# Errors

## Branch on `Code`, never on the message

For a read, log a thrown API refusal:

```csharp
catch (AnisApiException failure)
{
    logger.LogWarning(
        "anis {Code} {Status} request={RequestId} retryable={Retryable} replayed={Replayed}",
        failure.Code, failure.Status, failure.RequestId, failure.IsRetryable, failure.IsReplayed);
}
```

`Code` is the machine contract. `Title` and `Detail` behind the message are **localized presentation** —
they change with `Accept-Language` and with copy edits, and branching on them breaks the first time
somebody improves the wording.

`RequestId` is what to quote when you ask Anis about a call.

## What every refusal tells you

| Member | Meaning |
|---|---|
| `Code` / `RawCode` | the public code (`RawCode` keeps the spelling of one this SDK version does not know) |
| `Status` | the HTTP status |
| `RequestId` | the id to quote to Anis |
| `RetryAfter` | the signed `Retry-After`, when Anis knows when to try again |
| `IsRetryable` | whether the catalogue marks the **code** retryable |
| `IsReplayed` | the refusal is the **recorded** answer of an earlier order with the same operation id — the order is closed: it arrives as `OrderNotPlaced` |
| `OrderOutcome` | the refusal classification from its code and replay marker, before the create/resume rule is applied: `NotPlaced` or `Unknown`. `Unknown` includes the access refusals `invalid_credentials`, `insufficient_scope` and `wallet_not_granted`. The SDK has already applied it: the refusal arrives inside `OrderNotPlaced` or `OrderOutcomeUnknown` |

On an order a refusal is returned, not thrown: it arrives as `OrderNotPlaced.Refusal` or as `OrderOutcomeUnknown.Cause`. On a read, Anis API refusals are thrown — see [Orders and recovery](orders-and-recovery.md#when-an-order-is-refused-was-it-placed). The typed exceptions below are the refusal types.

## The typed exceptions

One base type plus a small set of subclasses for the refusals callers actually branch on — not one exception type per code, which would be a `catch` list nobody maintains.

| Type | Codes | Meaning |
|---|---|---|
| `InsufficientBalanceException` | `insufficient_balance` | the wallet cannot cover the purchase |
| `PriceChangedException` | `price_changed` | the price moved since the catalogue read |
| `OutOfStockException` | `quantity_unavailable`, `card_unavailable` | not sellable in that quantity, or not at all, now |
| `LimitExceededException` | `owner_limit_exceeded`, `daily_limit_exceeded` | the owner's spending allowance is used up — **do not poll** |
| `RateLimitedException` | `rate_limited` | a request-rate limit; wait for `RetryAfter`, then send again — an order **resumes the same id** (it comes back as `OrderOutcomeUnknown`), never a new one |
| `IdempotencyConflictException` | `idempotency_conflict` | one operation id used for two different orders |
| `InvalidCredentialsException` | `invalid_credentials` | authentication failed (unknown, revoked, expired or replaced key; bad or stale signature; clock) |
| `ReplayDetectedException` | `replay_detected` | the same signed bytes arrived twice; calling again is safe — on an order the first copy may have bought, so **resume the same id** (an order comes back as `OrderOutcomeUnknown`) |
| `AuthorizationException` | `insufficient_scope` (a missing permission **or** a source address outside the application's allowed networks — deliberately the same answer), `source_ip_not_allowed` (Anis's own edge block list), `binding_not_authorized`, `account_inactive`, `business_subscription_required`, `wallet_disabled`, `wallet_expired`, `purchase_not_allowed`, `reveal_not_allowed` | the policy or the owner's state does not allow this |
| `ResourceNotFoundException` | `resource_not_found`, `card_not_found`, `wallet_not_granted` | absent, or not yours — the two are deliberately indistinguishable |
| `ValidationFailedException` | `validation_failed`, `currency_not_supported` | the request broke a contract rule |
| `DependencyUnavailableException` | `dependency_unavailable`, `request_timeout`, `internal_error` | no decision was reached — retry a read, **resume** an order |
| `EnrollmentRefusedException` | `invitation_invalid`, `challenge_expired`, `key_proof_invalid`, `key_duplicate` | an enrollment step was refused |
| `EnrollmentKeyMismatchException` | none — raised by the SDK, not by Anis (it derives from `Exception`, not `AnisApiException`) | the key Anis holds has another thumbprint than the key you submitted; no proof was built — ask Anis staff to restart the enrollment |
| `AnisApiException` | everything else (`allowed_debt_consent_required`, `invoice_reveal_limit_exceeded`, `malformed_signed_request`, …) | read `Code` |

`malformed_signed_request` (400) means the signed request was badly built — a signature header missing or unreadable, the signed parts not the ones the route needs or not in its order, or a `Content-Digest` that cannot be read or does not describe the body. The SDK builds these itself, so with the SDK it points at something between you and Anis rewriting the request (a proxy that changes headers or the body). It is decided before any key is looked at and says nothing about your key. On an order it comes back as `OrderOutcomeUnknown` (suggested delay 60 seconds): fix what rewrites the request, then resume the same id — never a new one. `signature_expired` and `invalid_content_digest` are in the catalogue — and in `PartnerErrorCode` — but Anis does not send them: an expired signature arrives as `invalid_credentials`, a digest that does not describe the body as `malformed_signed_request`.

All 37 public codes are in `PartnerErrorCode`, generated from the error catalogue, and a test pins every one
of them to its exception type and its order outcome. A code this SDK version does not know arrives as
`PartnerErrorCode.Unknown`, with its wire spelling in `RawCode`. On an order, a refusal marked replayed returns
`OrderNotPlaced`; otherwise it returns `OrderOutcomeUnknown`, requiring a resume with the same operation id.

## `validation_failed` never names the field

By design: naming the failing member would let anyone probe for valid card ids and price points. The SDK
already refuses, before sending, a total that is not unit × quantity, mismatched currencies, a quantity
below one and a unit price that is not greater than zero. Anis also refuses:

- a price that is not written with exactly three decimals, or one Cards will not accept (a currency other than
  the wallet's);
- an `externalReference` longer than 100 characters, or with a character outside letters, digits, space
  and `- _ . : / #`;
- a malformed or tampered paging cursor;
- any body at all on a reveal (the SDK never sends one).

## `IsRetryable` describes the code, not your call

Seven public codes are marked retryable in the catalogue (`dependency_unavailable`, `internal_error`,
`operation_processing`, `rate_limited`, `replay_detected`, `request_timeout`, `signature_expired`). That is a
property of the refusal, not permission to re-send anything:

- A **read** may be retried as-is.
- An **order** is never retried with a fresh operation id while its outcome is unknown. It is *resumed* with
  the same one. See [Orders and recovery](orders-and-recovery.md).

An unknown code is never assumed retryable.

## Limits

Anis staff can set limits on an application, per application or per wallet, each counting one kind of
request:

| Limit type | Counts |
|---|---|
| `requests` | every call |
| `orders` | order placements only (`CreateAsync` / `ResumeAsync`) |
| `reveals` | both reveals |

A limit reached is `rate_limited` (429) with a signed `Retry-After` when the reset time is known — an
`orders` limit is therefore only ever hit by an order, and browsing never spends it. The gateway also
protects itself with limits that give no reset time; back off exponentially then. An owner's spending
allowance is different (`LimitExceededException`) and does not reset by waiting.

## `RequestSigningException`: your signer failed, nothing was sent

When your `IRequestSigner` fails — a vault or HSM that is unreachable or refuses, or a signer that returns
something other than 64 bytes — the request is never sent. The SDK raises `RequestSigningException` with the
signer's own exception inside. On an order nothing was placed; call again (the same operation id is fine)
once the signer works. It is counted with `error.type = signing`, never as an unknown order outcome, so a
vault outage does not look like an Anis outage.

## A response you cannot verify is not an error — it is discarded

Only the routes whose answers Anis signs can end this way: orders, order reads, both reveals, enrolment and the
signature self-check. The information reads — profile, wallets, catalogue, owned cards — are answered unsigned and
never verified, so their refusals always arrive as the `AnisApiException` types above.

For a signed read, catch the verification exception:

```csharp
catch (UnverifiableResponseException failure)
{
    // failure.Failure names the rule: ContentDigestMismatch, SignatureInvalid,
    // CoveredComponentsMismatch, UnknownKey, KeyRejected, CreatedOutOfWindow, SignatureMissing, ...
}
```

This is a different category from an API error. An `AnisApiException` is Anis telling you something; an
`UnverifiableResponseException` means what arrived could not be shown to have come from Anis at all. Its
content is discarded and never reaches you.

Treat it as an infrastructure or integrity problem, not a business one. The usual causes are an intercepting
proxy that rewrites headers, a clock more than 60 seconds out, or a key rotation your key cache has not
caught up with — the SDK already refreshes once on an unknown key version. One cause is Anis itself: when its
response signer is unavailable, the gateway answers a signed route with a deliberately unsigned `503`
(`SignatureMissing`). The information reads do not need the signer and keep answering.

On an **order**, an unverifiable response comes back as `OrderOutcomeUnknown`: resume with the same operation id.
On a read, retry.
