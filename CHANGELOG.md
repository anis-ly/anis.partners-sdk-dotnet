# Changelog

All notable changes to the `Anis.Partners` package. Versions follow [Semantic Versioning](https://semver.org).

## 1.3.0 — unreleased

Enrolment now ends with a phone call in which you read a short safety code, instead of sending a fingerprint, and the
SDK checks that Anis holds the key you submitted.

- **Added:** `EnrollmentKeyResult.SafetyCode` — the 16-character code (`XXXX-XXXX-XXXX-XXXX`) derived from your key's
  fingerprint. Anis staff phone your technical contact and ask you to read it before your key goes live. The SDK
  derives it from the thumbprint it has just verified against your own key.
- **Added:** `KeyThumbprint.Compute(PartnerJwk)` — the RFC 7638 thumbprint of a P-256 public key — and
  `SafetyCode.FromThumbprint(string)`.
- **Added:** `EnrollmentKeyMismatchException`. `AnisEnrollmentClient.SubmitKeyAsync` computes the thumbprint of the key
  you send and compares it with the one Anis answers with; if they differ it throws and returns no result, so no
  possession proof is built on a key you did not submit. Ask Anis staff to restart the enrollment.
- **Changed:** `SubmitKeyAsync` throws an `ArgumentException`, before sending anything, for a public JWK that is not
  a complete P-256 key (it used to leave that to Anis, which refused it with `key_proof_invalid`).
- **Changed:** you no longer send anyone your key's fingerprint. The enrolment guide, the security notes and the
  sample describe the phone call instead.

## [1.2.0] - 2026-10-04

More of what Anis knows about a card and an order now reaches your code. Every addition is optional, so nothing
you have written stops compiling or changes behaviour.

- **Added:** `CatalogueSubcategory.Disclaimer` — Anis's note for buyers of that subcategory's cards. Show it to
  your end customer before they buy.
- **Added:** `CatalogueCard.MinimumQuantity` and `MaximumQuantity` — the fewest and most of a card one order may
  carry. Check them before you order; a quantity outside them is refused.
- **Added:** `Order.ExternalReference` — your own reference, returned on every answer for the order, so a lookup or a
  replay tells you which of your sales it was.
- **Added:** `Order.FailureCode` — why an order you looked up failed (the refusal code Anis recorded). Only a lookup
  of a failed order carries it.
- **Added:** `Order.CodesWithheld` — Anis says in so many words that the order was paid but its codes were withheld.
  Do not buy it again; write to support@anis.ly. `OrderCompleted.CodesWithheld` is now true when Anis says so **or**
  when the answer carries no credentials, so the 1.1.0 behaviour still holds for answers without the flag.
- **Added:** `RevealedCredential.ExpiryDate` — the last day the card can be used; tell your end customer. On a reveal
  (single card or whole invoice) the credential also carries `InvoiceId`, `Card` and `PurchasedAt`.
- **Added:** on `MaskedCard`: `UnitPrice` (what one card cost), `ExpiryDate`, `InvoiceNumber` (the number printed
  on the invoice; quote it to support), `FaceValue` and `Subcategory` (new `MaskedCardSubcategory`).
- **Changed (meaning):** `MaskedCard.CredentialAvailable` is now true only when a reveal would release a code; a
  refunded card reads false.
- The SDK adds no validation of its own for the quantity limits; it only reports them.

## [1.1.0] - 2026-10-03

An order refused for its rate or for its access is no longer reported as "nothing was bought".

- **Changed:** `Orders.CreateAsync` now returns `OrderOutcomeUnknown` (resume with the same operation id) instead
  of `OrderNotPlaced` when Anis answers `rate_limited` (429). `SuggestedDelay` is the signed `Retry-After`, or
  5 seconds when there is none, and `Cause` is the `RateLimitedException`. `ResumeAsync` already returned `OrderOutcomeUnknown` here.
- **Why:** a first attempt refused for its rate placed nothing, but the SDK cannot tell a first attempt from a copy
  of an earlier one: a retry handler in your host (for example an Aspire or Polly pipeline outside the SDK) may have
  resent a create whose first attempt is still selling, and a create may reuse an id whose earlier attempt timed out.
  Placing a new order under a new id after such a 429 could buy the cards twice. Resuming the same id is always safe.
- **Effect on your code:** the two result types are unchanged, so nothing stops compiling. If you branch on
  `OrderNotPlaced` to retry with a new id after a rate limit, resume the same id instead. The other refusals this
  release reclassifies are listed below; `OrderRefusalOutcome` of `RateLimitedException` is now `Unknown`.
- **Effect on telemetry:** each rate-limited create now counts as an `unknown` order outcome (the
  `Anis.Partners.Sdk` order-outcome metric, tagged `error.type=rate_limited`) and writes the Warning log that
  every unknown outcome writes. An alarm on unknown outcomes will therefore also count rate limits.
- Docs and doc comments corrected to match: an order's rate limit is a resume, never a new id.
- **Documented:** `OrderCompleted.Credentials` can be empty. Anis now answers an order that was placed and paid,
  but whose codes cannot be released (a card invalidated or refunded), as completed with its codes withheld,
  instead of a refusal. The order is complete: do not buy it again. No code change in the SDK; a test pins it.
- **Changed:** on an order, `invalid_credentials` (401), `insufficient_scope` (403) and `wallet_not_granted` (404) —
  and the reserved `signature_expired` — now return `OrderOutcomeUnknown` from `CreateAsync` too, with `SuggestedDelay`
  of 60 seconds unless Anis sends a
  `Retry-After` (`ResumeAsync` returns the same, with the same delay). Each is decided before the order is looked
  up, and staff can withdraw a key, a permission or a wallet grant while an earlier attempt with the same id is
  selling. Restore access, then resume the same id. A refusal marked `Idempotency-Replayed` is still `OrderNotPlaced`.
  **Effect on your code:** if you place a new order under a new id after these refusals, resume the same id
  instead. **Effect on telemetry:** each such create counts as an `unknown` order outcome (`error.type` is the code).
- **Added:** `EnrollmentStatus.KeyExpiresAt` — the date your active key stops working. Ask for a replacement weeks
  before it.
- **Added:** `OrderCompleted.CodesWithheld` — true when the order completed and was paid but Anis released no
  codes. Do not buy it again; the withheld cards cannot be revealed; write to support@anis.ly.
- **Changed:** `Orders.CreateAsync` and `ResumeAsync` throw `ArgumentOutOfRangeException` for a unit price that is
  not greater than zero, before anything is sent (it used to come back as `validation_failed` and spend the id).
- **Removed:** `EnrollmentKeyRequest.Cidrs`. Anis never used the proposed networks; tell Anis staff your calling
  networks when you ask for access. Code that sets it no longer compiles; delete the line.
- **Added:** `PartnerErrorCode.MalformedSignedRequest` (`malformed_signed_request`, 400): a signed request that was
  badly built — a missing or unreadable signature header, signed parts not in the route's order, or a
  `Content-Digest` that does not describe the body — is no longer answered as `invalid_credentials`. It arrives as
  the base `AnisApiException`; on an order it is `OrderOutcomeUnknown` with `SuggestedDelay` of 60 seconds (with the
  SDK only something rewriting the request on its way causes it, and that may have been a resend): fix it, then
  resume the same id. Released `PartnerErrorCode` numbers are unchanged; the new member is 37.
- **Changed:** the error descriptions (`PartnerErrorCode` IntelliSense) are regenerated from the current catalogue:
  `key_duplicate`, `insufficient_scope`, `operation_processing`, `source_ip_not_allowed` and `validation_failed` (the
  ways the request itself is refused: body over 64 KB, chunked body, headers over 32 KB, …) now say what to do, and
  `signature_expired` / `invalid_content_digest` say they are reserved.
- **Docs:** the signature troubleshooting steps follow what the self-check answers; the clock limits are stated
  exactly (about 30 seconds fast or 60 seconds slow).

## [1.0.0] - 2026-09-28

The first stable release. Unchanged from `1.0.0-preview.1` except for the version.

- Every request signed with RFC 9421 HTTP Message Signatures over P-256/SHA-256, the body bound with an
  RFC 9530 `Content-Digest`; any key custody through `IRequestSigner`, and `EcdsaP256Signer` for a PEM file.
- Every response verified against Anis's published signing keys before it is returned; anything that cannot
  be verified is discarded and raised as `UnverifiableResponseException`.
- All 19 public routes: profile, wallets, catalogue, owned cards and reveals, orders, order status and the
  signature self-check, with cursor paging followed for you.
- Orders that cannot be placed twice: the operation id is the idempotency key, a repeat returns the recorded
  outcome, and an unknown outcome is resumed with the same id.
- Key enrolment through `AnisEnrollmentClient`: submit the public key and prove possession of the private key.
- Typed refusals for every public error code, with the retry guidance each one carries.
- Logs, traces and metrics under `Anis.Partners.Sdk`, with no secret in any of them.
- Targets .NET 8 and .NET 10.
