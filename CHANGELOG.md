# Changelog

All notable changes to the `Anis.Partners` package. Versions follow [Semantic Versioning](https://semver.org).

## [Unreleased]

## [1.1.0]

A rate-limited order is no longer reported as "nothing was bought".

- **Changed:** `Orders.CreateAsync` now returns `OrderOutcomeUnknown` (resume with the same operation id) instead
  of `OrderNotPlaced` when Anis answers `rate_limited` (429). `SuggestedDelay` is the signed `Retry-After`, or
  5 seconds when there is none, and `Cause` is the `RateLimitedException`. `ResumeAsync` already behaved this way.
- **Why:** a first attempt refused for its rate placed nothing, but the SDK cannot tell a first attempt from a copy
  of an earlier one: a retry handler in your host (for example an Aspire or Polly pipeline outside the SDK) may have
  resent a create whose first attempt is still selling, and a create may reuse an id whose earlier attempt timed out.
  Placing a new order under a new id after such a 429 could buy the cards twice. Resuming the same id is always safe.
- **Effect on your code:** the two result types are unchanged, so nothing stops compiling. If you branch on
  `OrderNotPlaced` to retry with a new id after a rate limit, resume the same id instead. Every other refusal is
  classified as before; `OrderRefusalOutcome` of `RateLimitedException` is now `Unknown`.
- **Effect on telemetry:** each rate-limited create now counts as an `unknown` order outcome (the
  `Anis.Partners.Sdk` order-outcome metric, tagged `error.type=rate_limited`) and writes the Warning log that
  every unknown outcome writes. An alarm on unknown outcomes will therefore also count rate limits.
- Docs and doc comments corrected to match: an order's rate limit is a resume, never a new id.
- **Documented:** `OrderCompleted.Credentials` can be empty. Anis now answers an order that was placed and paid,
  but whose codes cannot be released (a card invalidated or refunded), as completed with its codes withheld,
  instead of a refusal. The order is complete: do not buy it again. No code change in the SDK; a test pins it.

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
