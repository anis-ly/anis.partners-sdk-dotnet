# Changelog

All notable changes to the `Anis.Partners` package. Versions follow [Semantic Versioning](https://semver.org).

## [Unreleased]

The first public release.

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
