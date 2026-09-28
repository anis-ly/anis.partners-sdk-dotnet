# Security and key custody

## Your private key never reaches this SDK as a key

`IRequestSigner` has exactly one method: it takes bytes and returns 64 bytes. There is **no member** that
hands over, exports or describes a private key — which is what lets a vault or HSM satisfy it without the
key entering your process at all.

```csharp
// Development, or a deployment whose custody is a protected file.
.WithSigner(EcdsaP256Signer.FromPemFile("/secure/partner-key.pem", keyId))

// Any custody you like.
public sealed class VaultSigner(IVault vault, Guid keyId) : IRequestSigner
{
    public Guid KeyId => keyId;

    public async ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> signatureBase, CancellationToken ct)
        => await vault.SignP1363Async(signatureBase, ct);   // must return r‖s, 64 bytes
}
```

Never put the key in `appsettings.json`, an environment variable, or anywhere a log dump or a crash
reporter can reach.

`KeyId` is a `Guid`, not a string, because the request `keyid` is a canonical UUID and the gateway
validates it as one. A typo in a string would have surfaced as an unexplained `401`.

## There is no environment setting

No `Environment` enum, no `.Staging` / `.Production`, no flag — anywhere in this SDK. Every Anis deployment
is a fully isolated copy with its own authority, keys and data. Which one you talk to is decided by the
`Authority` you were given and by nothing else; a key enrolled on one deployment means nothing on another.

An SDK-level environment switch would be one line that repoints production traffic, which is exactly the
line somebody edits by accident.

## Responses are always verified

That includes **enrollment**. Enrollment *requests* are not signed — they carry a bearer-style token — but
enrollment *responses* are, and they simply omit the `;req` binding because there is no request signature
to bind to. `AnisEnrollmentClient` verifies them. The key-submission response carries the challenge and the
`keyId` a partner then trusts for a year, which makes it the last response anyone should take on faith.

The one genuinely unsigned route is `GET /.well-known/partner-signing-keys.json`, fetched by a client with
no verifying handler — verifying it would need the keys it is being fetched to supply. The one unsigned
*answer* is the `503` the gateway sends when its own signer is unavailable; the SDK discards it like any
other unverifiable response (`SignatureMissing`), and the right move is the one for any unknown outcome —
retry a read, resume an order with the same operation id.


Every response is checked before you see it: the `Content-Digest` against the body, the advertised
components against the frozen profile, the `;req` binding against the request *you* sent, the `created`
freshness, the key against the published document, and finally the signature.

The digest is compared **before** the signature — the signature covers the digest *header*, not the body,
so checking the signature first would accept a swapped body whenever the attacker also rewrote the digest.

There is no option to turn this off.

## Anis's response keys rotate automatically

The published document carries the active, next and retiring versions at once, so a response received just
before a rotation still verifies just after one. The SDK caches it for `SigningKeyCacheDuration` and
refreshes **once immediately** on a `keyid` it does not know, which is the ordinary signal that a rotation
happened. A document carrying a private member is rejected whole — not just that key.

You never need to act on a rotation. For reference, this schematic published document shows several versions at
once, each named by `kid` (`x` and `y` are abbreviated here):

```json
{
  "keys": [
    { "kty": "EC", "crv": "P-256", "kid": "partner-response-signing/4d1a2c9e8b7f4e6a9c0d1b2e3f4af3e2", "use": "sig", "alg": "ES256", "x": "…", "y": "…" },
    { "kty": "EC", "crv": "P-256", "kid": "partner-response-signing/a07e5b3c1d2f4c8e9b6a0f1e2d3c9b41", "use": "sig", "alg": "ES256", "x": "…", "y": "…" }
  ]
}
```

An answer names its key in `Signature-Input` (`keyid="partner-response-signing/4d1a2c9e8b7f4e6a9c0d1b2e3f4af3e2"`), and the SDK matches it
to a `kid` exactly — the order of the entries means nothing to it. If you supply your own `ISigningKeySource`, keep
every entry of the document, not only the first: an answer signed moments before or after a rotation names one of
the others.

## Your request keys: rotation and revocation

Your own signing key is rotated by enrolling its replacement. Anis staff start the rotation and send you a
new invitation; you enrol a new key exactly like the first (see [Getting started](getting-started.md)), and
staff record and confirm its fingerprint. During the overlap they set, **both keys sign successfully** —
move your signer to the new key id inside that window.

A revoked key is refused on every route with `401 invalid_credentials`, exactly like an unknown key, from
the moment staff revoke it. Revoking a key you no longer use does not affect your current one. To sign again after your
current key is revoked, ask for a new invitation and enrol a new key.

## Do not log credentials

`RevealedCredential.ToString()` redacts the serial and the voucher, so a careless interpolation cannot leak
one. That is a guard rail, not a guarantee: do not serialize the object into a log either.

Credential plaintext exists in exactly two places on this surface — the first completion of an order, and a
reveal. Persist it where you keep secrets.

## Clock

Requests carry `created` and `expires`. The contract allows them up to 300 seconds apart; this SDK signs for
at most 60 (`SignatureLifetime`, 60 by default). Responses carry `created` and no `expires`, so the SDK bounds
their freshness itself at ±60 seconds. Without a bound, a captured signed response would stay replayable to a
client forever.

The two limits are kept equal on purpose. Anis admits a request until its `expires`, and the SDK accepts
the answer only within 60 seconds of its own clock. If the signature could live longer than the answer
window, a clock a few minutes slow would get an order admitted and completed — and its answer, carrying the
card codes, discarded as too old. With both at 60 seconds, such a clock is refused before anything is
bought.

A host clock more than a minute out will fail in both directions. Run NTP.
