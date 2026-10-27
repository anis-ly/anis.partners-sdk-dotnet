# Webhooks

Anis tells your application when a meter charge or a Direct recharge reaches an outcome: a signed `POST` to the
HTTPS URL you registered. This page covers what to register, how to verify what arrives, and what to do with it.
Setting and testing the endpoint, and asking for a missed notification again, are in the
[routes](routes-and-permissions.md#meters-direct-recharge-and-notifications).

## Register one endpoint

```csharp
var endpoint = await anis.WebhookEndpoint.SetAsync(new SetWebhookEndpointRequest { Url = "https://pos.example.ly/hooks/anis" }, ct);
// endpoint.Status is PendingVerification until the challenge below is answered
```

The URL must be HTTPS on port 443 with a host name that contains a dot (`https://hooks.example.com` is fine, `https://hooks` is refused) and is not an IP address, no user-info or fragment, at most 2048
characters (`webhook_endpoint_invalid` otherwise). Sending the same URL again keeps its registration; a changed
URL needs verification again. `Status` is `NotConfigured`, `PendingVerification`, `Verified` or `Disabled`;
staff can disable an endpoint, and `Disabled` wins. `Generation` identifies the registration the challenge
belongs to. `TestAsync` and `RedeliverAsync` need a verified, enabled endpoint (`webhook_endpoint_unverified`
otherwise).

Anis calls your endpoint from one fixed address per deployment. If your firewall only accepts known sources, allow the
address listed in [Where webhooks come from](https://developers.anis.ly/docs/webhooks#where-webhooks-come-from); the signature, not the address, proves a request came from Anis.

## Receive: capture the raw bytes first

The signature covers the exact bytes Anis sent. A framework that parses the JSON first and re-serializes it
changes whitespace and escapes, and the digest no longer matches. Read the raw body before any JSON middleware
and hand those bytes to the verifier. In ASP.NET Core:

```csharp
app.MapPost("/hooks/anis", async (HttpRequest request, PartnerWebhookVerifier verifier, IWebhookInbox inbox, CancellationToken ct) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer, ct);

    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    // UseForwardedHeaders must already have validated known proxies and an explicit host allowlist.
    // The proxy must preserve the external path and query, without rewriting them.
    var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()!.RawTarget;
    var actualExternalTarget = $"{request.Scheme}://{request.Host}{rawTarget}";
    var webhook = new VerifiableWebhook(request.Method, actualExternalTarget, headers, buffer.ToArray());
    WebhookEnvelope envelope;
    try
    {
        envelope = await verifier.VerifyAndParseAsync(webhook, ct);
    }
    catch (UnverifiableWebhookException failure)
    {
        // Never use, echo or store the content of what failed. Log the reason only.
        logger.LogWarning("anis webhook refused: {Reason}", failure.Failure);
        return Results.BadRequest();
    }

    // Echo on EVERY verified challenge delivery, before operational deduplication.
    if (envelope.Data is WebhookVerificationData verification)
        return Results.Json(new { challenge = verification.Challenge });

    // Implement this in the same database that owns your business changes (see below).
    return await inbox.ProcessOnceAsync(envelope, ct)
        ? Results.Ok()
        : Results.StatusCode(503);
});
```

Create the verifier once, with values from your own trusted configuration:

```csharp
var keys = new HttpSigningKeySource(new HttpClient { BaseAddress = new Uri("https://partners.anis.ly") },
    TimeSpan.FromMinutes(5), TimeProvider.System);
var verifier = new PartnerWebhookVerifier(keys, new Uri("https://pos.example.ly/hooks/anis"), applicationId, TimeProvider.System);
```

- **Expected URL and application id come from trusted configuration.** Supply the actual external request
  target independently. A different path, query or host is refused (`TargetUriMismatch`); a different application
  is refused (`ApplicationIdMismatch`). Use `Microsoft.AspNetCore.Http.Features.IHttpRequestFeature.RawTarget`
  to retain the original escaped path and query. At a direct listener, validate the host against your allowlist.
  Behind a proxy, configure `UseForwardedHeaders` with explicit known proxies/networks and allowed hosts before
  this endpoint; the proxy must strip client-supplied forwarding headers and preserve the external path/query.
  Never substitute `ConfiguredUrl` for the actual target: that would bypass the target comparison.
- **The key source** fetches only Anis's published key document. Do not give it a URL taken from a request.
- **Do not log the body, the signature, the signature base or the challenge.** A notification carries meter tokens
  and recipient details.

## What is verified, in order

1. Both `Signature` and `Signature-Input` are present and use the label `sig1` with the algorithm
   `ecdsa-p256-sha256` and the tag `anis-partner-webhook-v1`.
2. The `Content-Digest` is the SHA-256 of the raw bytes.
3. The signature covers exactly `@method`, `@target-uri`, `content-digest`, `webhook-id`, `anis-webhook-attempt`
   and `anis-application-id`, in that order, with a positive attempt number.
4. The signature lives exactly 300 seconds, has not expired (no grace) and was created no more than 300 seconds
   from your clock. Run NTP.
5. The method is `POST`, the target is your configured URL and the application header is your application.
6. The key is published by Anis for **webhooks** (`purpose` `webhook`): a key published for answers is refused
   (`KeyPurposeMismatch`). Only `active` and `next` keys are accepted; `retired` is refused even before its
   retirement date, a revoked key is refused whenever it signed, and a signature older than the key's activation is refused.
7. The signature is 64 bytes of IEEE P1363 `r||s` (a DER-encoded signature is refused) and verifies.
8. Only then is the body parsed (full timestamps require seconds and an explicit offset; instants are normalized to UTC): an object with `id`, `type`, `occurredAt`, `applicationId`, `data`, whose `id`
   equals `webhook-id` and whose `applicationId` equals the header.

`WebhookVerificationFailure` names the first rule that failed (22 members). A failure never produces an envelope.
The corpus that pins every rule is the conformance `webhook` set (75 vectors), run on both targets.

## Trust the data, not the type

`envelope.Data` is the operation's **current** full state at the time the notification was captured, which may be
later than the event that triggered it. A `directRecharge.suspended` notification delivered after you corrected
the recipient can already carry `inProgress`. Act on `data.Status`. A type this SDK does not know is accepted
after verification: `envelope.Type` is `Unknown`, `envelope.RawType` has the spelling and `envelope.Data` is the raw
`JsonElement`; it is never treated as a completed money result.

| Type | `Data` |
|---|---|
| `meterCharge.succeeded`, `meterCharge.failed` | `MeterCharge` (receipt with every token in order, or failure and refund) |
| `directRecharge.paid`, `.paymentFailed`, `.suspended`, `.completed`, `.rejected`, `.partiallyRefunded` | `DirectRecharge` (`.partiallyRefunded` occurs only if partial refunds are enabled for the product) |
| `webhook_endpoint.verification` | `WebhookVerificationData` |
| `webhook_endpoint.test` | `WebhookTestData` |

## Deduplicate durably on the id

Anis delivers at least once: retries and redeliveries repeat the same `id`. The attempt number is not the
identity. The SDK does not keep an inbox. Implement the sample's `IWebhookInbox.ProcessOnceAsync` against your
business database:

1. Insert a durable **pending** entry keyed uniquely by `envelope.Id`, preserving verified data for recovery.
   A duplicate pending entry still needs processing. Re-open existing pending work after a restart.
2. Begin a database transaction and lock that entry so concurrent deliveries serialize. If it is already
   **completed**, return success after the transaction ends.
3. Apply the current `data.Status` to your business records and mark the inbox entry **completed in that same
   transaction**. Commit both together. Roll back both on a handler failure and leave the pending entry retryable.
4. Return true only after committed completion; a busy or pending entry returns false (503), and exceptions
   produce a non-2xx response. Never use an early “seen id” insert as permission to return 200.

The interface is a host integration boundary, not an included database implementation. If you acknowledge a
queue instead, first commit the full pending entry durably, return 202, and run a recoverable worker that commits
business changes and completion together. An in-memory queue or id set cannot provide this guarantee.

Answer within 10 seconds. Redirects count as failures. Anis retries for about three days; failed deliveries stay
available for redelivery for 30 days (`WebhookEvents.RedeliverAsync`). Verification challenges bypass the
operational inbox and must echo on every verified retry.

## The verification challenge

After you register a URL Anis sends a `webhook_endpoint.verification` notification. Verify it like any other, then
answer 2xx within 10 seconds with exactly `{"challenge":"<the same value>"}` and no other member. Never echo a
challenge from a request you have not verified.
