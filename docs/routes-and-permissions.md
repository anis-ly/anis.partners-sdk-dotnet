# Routes and permissions

Every route the SDK calls, the method that calls it, the scope it needs, and what it counts toward. The
SDK chooses the signature profile and the body for you; they are listed so a failure can be read.

| SDK call | Route | Scope | Signed as | Body | Counts toward |
|---|---|---|---|---|---|
| `Profile.GetAsync` | `GET /v1/profile` | `profile:read` | read | none | `requests` |
| `Wallets.ListAsync` / `ListPageAsync` | `GET /v1/wallets` | `wallets:read` | read | none | `requests` |
| `Wallets.GetAsync` | `GET /v1/wallets/{walletId}` | `wallets:read` | read | none | `requests` |
| `Catalogue.ListCategoriesAsync` / `ListCategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories` | `catalogue:read` | read | none | `requests` |
| `Catalogue.ListSubcategoriesAsync` / `ListSubcategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories` | `catalogue:read` | read | none | `requests` |
| `Catalogue.GetSubcategoryAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}` | `catalogue:read` | read | none | `requests` |
| `Catalogue.ListCardsAsync` / `ListCardsPageAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards` | `catalogue:read` | read | none | `requests` |
| `Orders.CreateAsync` / `ResumeAsync` | `POST /v1/wallets/{walletId}/orders` | `orders:create` | order | the order JSON | `requests`, `orders` |
| `Orders.GetAsync` | `GET /v1/orders/{operationId}` | `orders:read`, or `orders:create` for this application's own orders | read | none | `requests` |
| `OwnedCards.ListAsync` / `ListPageAsync` | `GET /v1/wallets/{walletId}/cards` | `cards:read` | read | none | `requests` |
| `OwnedCards.GetAsync` | `GET /v1/wallets/{walletId}/cards/{soldCardId}` | `cards:read` | read | none | `requests` |
| `OwnedCards.RevealAsync` | `POST /v1/wallets/{walletId}/cards/{soldCardId}/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | `requests`, `reveals` |
| `OwnedCards.RevealInvoiceAsync` | `POST /v1/wallets/{walletId}/invoices/{invoiceId}/cards/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | `requests`, `reveals` |
| `Diagnostics.CheckSignatureAsync` | `POST /v1/diagnostics/signature` | `diagnostics:use` | nonce mutation | exactly `{}` | `requests` |
| `AnisEnrollmentClient.GetAsync` | `GET /v1/enrollments/{invitationId}` | enrollment token | — | none | — |
| `AnisEnrollmentClient.SubmitKeyAsync` | `POST /v1/enrollments/{invitationId}/keys` | enrollment token | — | the public key | — |
| `AnisEnrollmentClient.SubmitProofAsync` / `ProveAsync` | `POST /v1/enrollments/{invitationId}/proof` | enrollment token | — | the proof | — |
| `AnisEnrollmentClient.GetStatusAsync` | `GET /v1/enrollments/{invitationId}/status` | enrollment token | — | none | — |
| (response verification) | `GET /.well-known/partner-signing-keys.json` | public | — | none | — |

**Signed as.** *read*: method, authority, path, query and date. *nonce mutation*: adds a `Content-Digest`
and a one-time nonce. *order*: adds the `Idempotency-Key` (your operation id). Every response on every route
except the key document is signed by Anis and verified by the SDK before you see it.

**Body.** The two reveals send no body at all — the gateway refuses any byte there — and still sign the
digest of the empty body. The self-check sends exactly `{}`.

## Before a call is looked at

Anis checks all of these before any business decision, so an order refused by one of them was never placed.
The order in which they run is not part of the contract — do not branch on which one you meet first:

| Check | Refusal |
|---|---|
| the request's shape and body (including a body where none is allowed) | `validation_failed` 422 |
| the key exists, is active and signed this request; the digest describes the body; the signature is fresh | `invalid_credentials` 401 |
| the nonce was not seen before | `replay_detected` 409 |
| a staff-set limit, or the gateway's own protection | `rate_limited` 429 |
| the caller's address is inside the application's allowed networks | `insufficient_scope` 403 |
| the application's policy grants the route's scope | `insufficient_scope` 403 |
| the wallet in the path is granted to the application | `wallet_not_granted` 404 |

An address outside the allowed networks and a missing permission are deliberately the same
`insufficient_scope`, and a revoked key, an unknown key, a bad signature, a digest that does not describe the body and a stale signature are deliberately the same
`invalid_credentials`: a refusal never tells a caller which condition to work around. Run the
signature self-check: if it succeeds, the key is fine and the failing call was signed over something the
gateway saw differently (it reports what it saw); if it fails too, the key itself is not usable.

## Paging

The list routes page with an opaque cursor. Every `List…Async` follows it for you; its `List…PageAsync` form hands you one page and the `cursor`. Pass a cursor back exactly as received — it is signed as part of the
query, and a malformed or altered one is refused with `validation_failed`.
