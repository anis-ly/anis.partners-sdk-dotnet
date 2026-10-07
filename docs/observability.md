# Observability

Logs, metrics and traces, all on by default, all under one name.

```
Anis.Partners.Sdk
```

Subscribe to that name for traces and for metrics; logging flows through whatever `ILoggerFactory` your
host already has. Nothing is emitted unless something is listening — an unlistened `ActivitySource` and an
unsubscribed `Meter` cost effectively nothing, which is why none of this sits behind a flag you would have
to remember to switch on at the exact moment you need it.

## Turning it on

With `AddAnisPartners`, logging is wired automatically from the container. For traces and metrics:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing  => tracing.AddSource("Anis.Partners.Sdk"))
    .WithMetrics(metrics  => metrics.AddMeter("Anis.Partners.Sdk"));
```

Building the client by hand? Pass an `ILoggerFactory`:

```csharp
var client = AnisPartnersClient.Create(options, signer, loggerFactory: loggerFactory);
```

Leave it out and every log becomes a no-op. The client still works.

## Traces

One span per API call — enrollment calls included — named `anis.partners {route}`, kind `Client`.

| Tag | Value |
|---|---|
| `anis.client` | the name the application was registered under — `default` unless you registered several |
| `anis.route` | the route **template**, e.g. `/v1/wallets/{walletId}/orders` |
| `http.request.method` | `GET`, `POST` |
| `http.response.status_code` | the status |
| `anis.request_id` | the `X-Request-Id` — **quote this when you ask Anis about a call** |
| `anis.operation_id` | on orders: your own operation id |
| `anis.error.code` | on a refusal; the span status is also set to `Error` |
| `error.type` | when the call got no usable answer: `signing` (your signer failed; nothing was sent), `timeout`, `canceled`, `connection`, `unverifiable` or `other`; the span status is also set to `Error` |

The **template**, never the concrete path. Tagging `/v1/wallets/2f1c…/orders` would give your metrics
backend one time series per wallet, which is how an observability bill becomes a surprise.

`anis.operation_id` is the single most useful tag here: it is the same id you persisted before the call and
the same id Anis knows the purchase by, so one purchase is traceable across both systems.

## Metrics

| Instrument | Type | Tags |
|---|---|---|
| `anis.partners.request.duration` | histogram, ms | client, route, method, status, and on a refusal the public error code — or, when no usable answer came, `error.type` instead of a status |
| `anis.partners.signature.duration` | histogram, ms | signature profile |
| `anis.partners.response.verification.failures` | counter | the rule that refused it |
| `anis.partners.order.outcomes` | counter | client, and `completed` / `processing` / `replayed` / `unknown` (with `error.type` saying why) |
| `anis.partners.signing_keys.fetches` | counter | `first-use` / `expired` / `refresh` |

### What each one tells you

**`signature.duration` is separate from `request.duration` on purpose.** It is the one part you control. A
vault-backed signer adds a network round trip there, and inside a total that cost is invisible — you would
see "Anis is slow" and be wrong.

**`verification.failures` moving is an incident, with one known exception.** It means something between
you and Anis altered a response, or a clock is badly out, or a key rotation went unnoticed. Only the routes
whose answers Anis signs are verified — orders, order reads, the reveals, enrolment and the signature self-check —
so only they can move it; the information reads (profile, wallets, catalogue, owned cards) are answered unsigned,
are never verified and never count here. The exception: when Anis's own response signer is unavailable, the
gateway answers a signed route with a deliberately unsigned `503` — the one answer it may send unsigned there —
which counts here as `SignatureMissing`. A short burst of those is
an Anis outage, not tampering; treat the call as "dependency unavailable" (retry a read, resume an order).
Alert on anything else at any rate above zero.

**`order.outcomes` is the business signal.** A rising `processing` share means the owner side is slow; a
rising `replayed` share means your own code is repeating orders that had already completed. **`unknown` is
the one to alert on:** each is an order that ended with no outcome you can treat as final — a timeout, a
lost connection, an answer that could not be verified, or a refusal that reached no decision, or a refusal of a resume that is not the order's recorded answer — and each must
be resumed with the same operation id. `error.type` says which: the no-answer reasons above, `empty_body`
(a verified success that carried no order), or the public code of the refusal (`dependency_unavailable`,
`request_timeout`, `internal_error`, `replay_detected`) — or, on a resume, any other code that was not a recorded answer. A failure of your own signer is **not** among them:
nothing was sent, so nothing needs resuming. Log event 1008 names the operation id to resume.

Order calls return those outcomes as `OrderOutcomeUnknown`. If your own token cancels the call, the SDK counts `unknown` and throws `OperationCanceledException`; resume that id when you start again.

**`signing_keys.fetches` with reason `refresh`** means a response named a key version this client had not
seen — a rotation in progress, or a client pointed at the wrong authority. The first fetch happens at the first
signed answer, not the first call: a client that has only read information has fetched nothing.

## Logs

Every log the SDK writes lives in one file, source-generated, so the templates are fixed at compile time
and nothing can be interpolated into one by accident.

| Event | Level | When |
|---|---|---|
| 1000 `RequestSigned` | Debug | a request was signed — method, path, profile, key id |
| 1001 `RequestCompleted` | Debug | a response arrived — status, duration, request id |
| 1002 `RequestRefused` | **Warning** | Anis refused — code, status, request id, retryable, replayed |
| 1003 `ResponseDiscarded` | **Error** | a response on a route Anis signs could not be verified |
| 1004 `OrderOutcome` | Information | an order completed, is processing, or was replayed |
| 1005 `SigningKeysFetched` | Information | the key document was fetched |
| 1006 `UnknownSigningKey` | Warning | a response named an unseen key version |
| 1007 `RequestUnanswered` | **Warning** | a call ended with no usable answer — method, route, duration, reason |
| 1008 `OrderOutcomeUnknown` | **Warning** | an order's outcome is unknown — the operation id to resume, and why |

Levels are chosen for your on-call, not for our debugging. A **Warning** is Anis telling you something
about your request. An **Error** means the channel itself is suspect — that is a different kind of problem
and deserves a different page.

## What is never emitted

On any signal — log, span tag or metric tag:

- the private key, and anything derived from it
- the `Signature` value
- the **signature base**, and `@signature-params`
- the `Signature-Input` value
- the `Nonce`
- the enrollment token
- a voucher or a serial number

The signature base matters most: it is exactly the input an attacker needs to test candidate signatures
offline, and a telemetry pipeline is a place logs are shipped, indexed and retained.

This is not a convention. `No_secret_reaches_any_telemetry_signal` runs a reveal and an order that both
release credentials, captures **every** log line, span tag and metric tag, and asserts that none of the
above appears — then asserts the telemetry is non-empty, so the check cannot pass vacuously. The
enrollment walk-through test does the same for the enrollment token.

The concrete path appears in one place only: the Debug-level `RequestSigned` log, so a signature problem
can be matched to the exact URL. It holds identifiers (wallet, card, invoice), never a secret.

`RevealedCredential.ToString()` also redacts, so a careless interpolation elsewhere in your code cannot
leak one either. That is a guard rail, not a licence to log the object.

## Correlating with Anis

When something is wrong, the useful line is:

```
anis insufficient_balance (409) request 01J9R2K8T4V6XQ0M3B7C5D9E1F
```

`RequestId` is on `AnisApiException`, on the span as `anis.request_id`, and in the refusal log. It is what
Anis needs to find your call.
