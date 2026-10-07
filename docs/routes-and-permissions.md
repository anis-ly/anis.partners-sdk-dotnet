# Routes and permissions

Every route the SDK calls, the method that calls it, the scope it needs, and what it counts toward. The
SDK chooses the signature profile and the body for you; they are listed so a failure can be read.

| SDK call | Route | Scope | Signed as | Body | Answer | Counts toward |
|---|---|---|---|---|---|---|
| `Profile.GetAsync` | `GET /v1/profile` | `profile:read` | read | none | unsigned | `requests` |
| `Wallets.ListAsync` / `ListPageAsync` | `GET /v1/wallets` | `wallets:read` | read | none | unsigned | `requests` |
| `Wallets.GetAsync` | `GET /v1/wallets/{walletId}` | `wallets:read` | read | none | unsigned | `requests` |
| `Catalogue.ListCategoriesAsync` / `ListCategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.ListSubcategoriesAsync` / `ListSubcategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.GetSubcategoryAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.ListCardsAsync` / `ListCardsPageAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards` | `catalogue:read` | read | none | unsigned | `requests` |
| `Orders.CreateAsync` / `ResumeAsync` | `POST /v1/wallets/{walletId}/orders` | `orders:create` | order | the order JSON | **signed, verified** | `requests`, `orders` |
| `Orders.GetAsync` | `GET /v1/orders/{operationId}` | `orders:read`, or `orders:create` for this application's own orders | read | none | **signed, verified** | `requests` |
| `OwnedCards.ListAsync` / `ListPageAsync` | `GET /v1/wallets/{walletId}/cards` | `cards:read` | read | none | unsigned | `requests` |
| `OwnedCards.GetAsync` | `GET /v1/wallets/{walletId}/cards/{soldCardId}` | `cards:read` | read | none | unsigned | `requests` |
| `OwnedCards.RevealAsync` | `POST /v1/wallets/{walletId}/cards/{soldCardId}/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | **signed, verified** | `requests`, `reveals` |
| `OwnedCards.RevealInvoiceAsync` | `POST /v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | **signed, verified** | `requests`, `reveals` |
| `Diagnostics.CheckSignatureAsync` | `POST /v1/diagnostics/signature` | `diagnostics:use` | nonce mutation | exactly `{}` | **signed, verified** | `requests` |
| `AnisEnrollmentClient.GetAsync` | `GET /v1/enrollments/{invitationId}` | enrollment token | — | none | **signed, verified** | — |
| `AnisEnrollmentClient.SubmitKeyAsync` | `POST /v1/enrollments/{invitationId}/keys` | enrollment token | — | the public key | **signed, verified** | — |
| `AnisEnrollmentClient.SubmitProofAsync` / `ProveAsync` | `POST /v1/enrollments/{invitationId}/proof` | enrollment token | — | the proof | **signed, verified** | — |
| `AnisEnrollmentClient.GetStatusAsync` | `GET /v1/enrollments/{invitationId}/status` | enrollment token | — | none | **signed, verified** | — |
| (response verification) | `GET /.well-known/partner-signing-keys.json` | public | — | none | unsigned | — |

**Signed as.** *read*: method, authority, path, query and date. *nonce mutation*: adds a `Content-Digest`
and a one-time nonce. *order*: adds the `Idempotency-Key` (your operation id). Every request you send is signed,
on every route but enrolment and the key document.

**Answer.** Anis signs the answers that move money, deliver card codes or establish a key — orders, order reads,
both reveals and enrolment — and the signature self-check: every one of them, the success and each refusal. The SDK
verifies those before you see them and discards one it cannot verify, including one that arrives with no signature
(`SignatureMissing`). The information reads are answered unsigned — `Content-Digest` and `X-Request-Id`, no
`Signature` — and the SDK returns them as they arrive, refusals included, with no verification and no call for
Anis's published keys. Which is which is fixed per route in the SDK, never decided from what an answer carries.

**Body.** The two reveals send no body at all — the gateway refuses any byte there — and still sign the
digest of the empty body. The self-check sends exactly `{}`.

## Before a call is looked at

Anis checks all of these before any business decision, so an order refused by one of them was never placed.
The order in which they run is not part of the contract — do not branch on which one you meet first:

| Check | Refusal |
|---|---|
| the request's shape and body (including a body where none is allowed) | `validation_failed` 422 |
| the signature headers are present and well formed, sign the parts this route needs in its order, and the digest describes the body | `malformed_signed_request` 400 |
| the key exists, is active and signed this request; the signature is fresh | `invalid_credentials` 401 |
| the nonce was not seen before | `replay_detected` 409 |
| a staff-set limit, or the gateway's own protection | `rate_limited` 429 |
| the caller's address is inside the application's allowed networks | `insufficient_scope` 403 |
| the application's policy grants the route's scope | `insufficient_scope` 403 |
| the wallet in the path is granted to the application | `wallet_not_granted` 404 |

An address outside the allowed networks and a missing permission are deliberately the same
`insufficient_scope`, and a revoked key, an unknown key, a bad signature and a stale signature are deliberately the same
`invalid_credentials`: a refusal never tells a caller which condition to work around. Run the
signature self-check: if it succeeds, the key, the clock and the address are fine and the failing call was signed
over something the gateway saw differently (it reports what it saw). If it fails with `invalid_credentials`, the
cause is the key (wrong id, wrong key file, not yet active, revoked, replaced or past its end date), the host clock,
or the address — it must be the address Anis gave you, not rewritten by a proxy. If it fails with
`insufficient_scope`, the call comes from a network not agreed with Anis or the application lacks
`diagnostics:use`.

## Paging

The list routes page with an opaque cursor. Every `List…Async` follows it for you; its `List…PageAsync` form hands you one page and the `cursor`. Pass a cursor back exactly as received — it is signed as part of the
query, and a malformed or altered one is refused with `validation_failed`.
