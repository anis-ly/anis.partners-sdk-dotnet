# Routes and permissions

Every route the SDK calls, the method that calls it, the scope it needs, and what it counts toward. The
SDK chooses the signature profile and the body for you; they are listed so a failure can be read.

| SDK call | Route | Scope | Signed as | Body | Answer | Counts toward |
|---|---|---|---|---|---|---|
| `Access.SuspendApplicationAsync` | `POST /v1/application/suspend` | none | nonce mutation | `{ "reason": "..." }` | **signed, verified** | `requests` (application) |
| `Access.RevokeKeyAsync` | `POST /v1/keys/{keyId}/revoke` | none | nonce mutation | `{ "reason": "..." }` | **signed, verified** | `requests` (application) |
| `Catalogue.GetCardAsync` | `GET /v1/wallets/{walletId}/catalog/cards/{cardId}` | `catalogue:read` | read | none | unsigned | `requests` |
| `Profile.GetAsync` | `GET /v1/profile` | `profile:read` | read | none | unsigned | `requests` |
| `Wallets.ListAsync` / `ListPageAsync` | `GET /v1/wallets` | `wallets:read` | read | none | unsigned | `requests` |
| `Wallets.GetAsync` | `GET /v1/wallets/{walletId}` | `wallets:read` | read | none | unsigned | `requests` |
| `Catalogue.ListCategoriesAsync` / `ListCategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.ListSubcategoriesAsync` / `ListSubcategoriesPageAsync` | `GET /v1/wallets/{walletId}/catalog/categories/{categoryId}/subcategories` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.GetSubcategoryAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}` | `catalogue:read` | read | none | unsigned | `requests` |
| `Catalogue.ListCardsAsync` / `ListCardsPageAsync` | `GET /v1/wallets/{walletId}/catalog/subcategories/{subcategoryId}/cards` | `catalogue:read` | read | none | unsigned | `requests` |
| `Orders.CreateAsync` / `ResumeAsync` | `POST /v1/wallets/{walletId}/orders` | `orders:create` | order | the order JSON | **signed, verified** | `requests`, `orders` |
| `Orders.GetAsync` | `GET /v1/orders/{operationId}` | `orders:read`, or `orders:create` for this application's own orders | read | none | **signed, verified** | `requests` |
| `OwnedCards.ListAsync` / `ListPageAsync` | `GET /v1/wallets/{walletId}/owned-cards` | `cards:read` | read | none | unsigned | `requests` |
| `OwnedCards.GetAsync` | `GET /v1/wallets/{walletId}/owned-cards/{soldCardId}` | `cards:read` | read | none | unsigned | `requests` |
| `OwnedCards.RevealAsync` | `POST /v1/wallets/{walletId}/owned-cards/{soldCardId}/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | **signed, verified** | `requests`, `reveals` |
| `OwnedCards.RevealInvoiceAsync` | `POST /v1/wallets/{walletId}/invoices/{invoiceId}/owned-cards/reveal` | `cards:reveal` | nonce mutation | **zero bytes** | **signed, verified** | `requests`, `reveals` |
| `Diagnostics.CheckSignatureAsync` | `POST /v1/diagnostics/signature` | `diagnostics:use` | nonce mutation | exactly `{}` | **signed, verified** | `requests` |
| `MeterCharges.GetMeterPackagesAsync` / `GetMeterPackagesPageAsync` | `GET /v1/wallets/{walletId}/meter-packages` | `meter_charges:read` | read | none | unsigned | `requests` |
| `MeterCharges.GetMeterChargesAsync` / `GetMeterChargesPageAsync` | `GET /v1/wallets/{walletId}/meter-charges` | `meter_charges:read` | read | none | unsigned | `requests` |
| `MeterCharges.CreateAsync` / `ResumeAsync` | `POST /v1/wallets/{walletId}/meter-charges` | `meter_charges:create` | order (your operation id as `Idempotency-Key`) | the charge JSON | **signed, verified** | `requests`, `orders` |
| `MeterCharges.GetAsync` | `GET /v1/meter-charges/{operationId}` | `meter_charges:read` | read | none | **signed, verified** | `requests` |
| `DirectRechargeProducts.GetDirectRechargeProductsAsync` / `GetDirectRechargeProductsPageAsync` | `GET /v1/wallets/{walletId}/direct-recharge-products` | `direct_recharges:read` | read | none | unsigned | `requests` |
| `DirectRechargeProducts.GetAsync` | `GET /v1/wallets/{walletId}/direct-recharge-products/{productId}` | `direct_recharges:read` | read | none | unsigned | `requests` |
| `DirectRechargeProducts.GetDirectRechargeProductLocationsAsync` / `…PageAsync` | `GET /v1/wallets/{walletId}/direct-recharge-products/{productId}/locations` | `direct_recharges:read` | read | none | unsigned | `requests` |
| `DirectRechargeProducts.QuoteAsync` | `GET /v1/wallets/{walletId}/direct-recharge-products/{productId}/quote` | `direct_recharges:read` | read | none | unsigned | `requests` |
| `DirectRecharges.GetDirectRechargesAsync` / `GetDirectRechargesPageAsync` | `GET /v1/wallets/{walletId}/direct-recharges` | `direct_recharges:read` | read | none | unsigned | `requests` |
| `DirectRecharges.CreateAsync` / `ResumeAsync` | `POST /v1/wallets/{walletId}/direct-recharges` | `direct_recharges:create` | order (your operation id as `Idempotency-Key`) | the recharge JSON | **signed, verified** | `requests`, `orders` |
| `DirectRecharges.GetAsync` | `GET /v1/direct-recharges/{operationId}` | `direct_recharges:read` | read | none | **signed, verified** | `requests` |
| `DirectRecharges.UpdateRecipientAsync` | `PATCH /v1/direct-recharges/{operationId}/recipient` | `direct_recharges:create` | nonce mutation | the recipient JSON | **signed 202 + Location, verified** | `requests` |
| `WebhookEndpoint.GetAsync` | `GET /v1/webhook-endpoint` | none | read | none | unsigned | `requests` |
| `WebhookEndpoint.SetAsync` | `PUT /v1/webhook-endpoint` | none | nonce mutation | `{ "url": "..." }` | **signed, verified** | `requests` |
| `WebhookEndpoint.TestAsync` | `POST /v1/webhook-endpoint/test` | none | nonce mutation | exactly `{}` | **signed, verified** | `requests` |
| `WebhookEvents.GetWebhookEventsAsync` / `GetWebhookEventsPageAsync` | `GET /v1/webhook-events` | none | read | none | unsigned | `requests` |
| `WebhookEvents.RedeliverAsync` | `POST /v1/webhook-events/{eventId}/redeliver` | none | nonce mutation | exactly `{}` | **signed, verified** | `requests` |
| `AnisEnrollmentClient.GetAsync` | `GET /v1/enrollments/{invitationId}` | enrollment token | — | none | **signed, verified** | — |
| `AnisEnrollmentClient.SubmitKeyAsync` | `POST /v1/enrollments/{invitationId}/keys` | enrollment token | — | the public key | **signed, verified** | — |
| `AnisEnrollmentClient.SubmitProofAsync` / `ProveAsync` | `POST /v1/enrollments/{invitationId}/proof` | enrollment token | — | the proof | **signed, verified** | — |
| `AnisEnrollmentClient.GetStatusAsync` | `GET /v1/enrollments/{invitationId}/status` | enrollment token | — | none | **signed, verified** | — |
| (response verification) | `GET /.well-known/partner-signing-keys.json` | public | — | none | unsigned | — |

**Signed as.** *read*: method, authority, path, query and date. *nonce mutation*: adds a `Content-Digest`
and a one-time nonce. *order*: adds the `Idempotency-Key` (your operation id). Every request you send is signed,
on every route but enrolment and the key document.

**Answer.** Anis signs the answers that move money, deliver card codes or establish or stop access — orders, order reads,
both reveals, access stops and enrolment — and the signature self-check: every one of them, the success and each refusal. The SDK
verifies those before you see them and discards one it cannot verify, including one that arrives with no signature
(`SignatureMissing`). The information reads are answered unsigned — `Content-Digest` and `X-Request-Id`, no
`Signature` — and the SDK returns them as they arrive, refusals included, with no verification and no call for
Anis's published keys. Which is which is fixed per route in the SDK, never decided from what an answer carries.

**Body.** The two reveals send no body at all — the gateway refuses any byte there — and still sign the
digest of the empty body. The self-check sends exactly `{}`. Access stops send a JSON reason (required, trimmed, 1–500 characters)
and sign its digest under the same nonce profile. They carry no idempotency key and are not wallet-bound.
A retired key cannot be revoked (`key_not_revocable`, 409); an unknown key or one belonging to another application
returns the same `resource_not_found`, 404. An already-revoked key returns its current state. Application suspension
returns its current suspended state; subsequent requests are refused by admission. Resume is staff-only.

## Meters, Direct recharge and notifications

- **Scopes.** `meter_charges:create` also grants the meter reads and packages; `meter_charges:read` grants only the
  reads. `direct_recharges:create` also grants the product reads, quotes and the direct recharge reads;
  `direct_recharges:read` grants only the reads. A create scope never has to be added to read, and a read scope never
  allows a purchase. The recipient change needs `direct_recharges:create`. The webhook endpoint and event routes need
  no scope: every application manages its own endpoint, though admission and the allowed networks still apply.
- **One operation id.** The `Idempotency-Key` you persist is the meter charge id or the Direct recharge id. The
  operation reads and the recipient change are scoped to your application: another application's id answers like a
  missing one. They take no wallet in the path.
- **Limits.** Meter charges and Direct recharges count under the existing `orders` limit and against the same
  daily spending cap as card orders. There is no limit type of their own.
- **Recipient correction acknowledgement.** `UpdateRecipientAsync` returns `DirectRechargeProcessing` (the same result type as a create 202):
  `Accepted` is the existing `DirectRechargeAccepted`, exactly `{operationId, operationType: "directRecharge", status: "processing"}`, and `Location`
  is the required verified header pointing to the direct recharge read. A successful PATCH is always 202;
  a missing Location, another success status or a direct recharge body is rejected. The read side may still show
  `suspendedDataError` immediately after acceptance. Read later with `GetAsync` or wait for a `directRecharge.*`
  webhook before deciding on another correction; the SDK never follows Location or retries the PATCH.
- **Signed changes without an idempotency key.** The recipient change, the endpoint update, the test and the
  redelivery are signed with a one-time nonce and a digest over their body (`{}` for the test and the redelivery),
  and carry no `Idempotency-Key`. After an interrupted one, read the state again before deciding to send it again.
- **Lists.** The meter, Direct recharge and delivery lists page with an opaque cursor, keep their filters on every
  page and detect a cursor that does not advance. The meter and Direct recharge lists take `from`/`to` (at most 31 days;
  the last 31 days when both are absent) and an optional page size (default 50, at most 100). The delivery list takes
  `status`, `since` and `cursor`; its page size is fixed at 50.
- **Not covered.** The wallet statement and wallet-to-wallet transfer are not routes of this version.

The route table above lists the 39 routes of this version. The gateway also publishes three staging-only test operations for the allowed-network rules (`GET` and `POST /v1/application/networks`, `POST /v1/application/networks/remove`); they are never part of this SDK, and the drift test skips exactly those three. Their two error codes (`network_not_removable`, `partner_network_limit_reached`) are known to `PartnerErrorCode`.

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
