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

## The enrolment ends with a phone call

A key Anis holds is only as good as the proof that it is yours. Two checks cover the two ways it could go wrong:

- **Before the proof:** `SubmitKeyAsync` computes the thumbprint of the key you sent and compares it, in fixed
  time, with the one Anis answers with. A different thumbprint throws `EnrollmentKeyMismatchException` and no
  proof is built.
- **Before the key goes live:** Anis staff phone your technical contact and ask for the **safety code** your
  software printed (`EnrollmentKeyResult.SafetyCode`, 16 characters, `XXXX-XXXX-XXXX-XXXX`). The code is the first
  80 bits of the key's thumbprint, so only the key Anis holds produces it. If it does not match, the key stays
  pending. Staff do not see the fingerprint or the public key until the code has matched.

Read the code from your own software, not from an email or a chat message, and give it only to Anis staff who called
the number you registered.

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
staff verify its safety code by phone and confirm it. During the overlap they set, **both keys sign successfully** —
move your signer to the new key id inside that window.
When the overlap ends, the old key is refused with `invalid_credentials` by itself; nobody needs to do anything. Your
key also has an end date (`EnrollmentStatus.KeyExpiresAt`): ask for its replacement weeks before it.

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

Anis refuses a signature created more than about 30 seconds ahead of its clock, and a signature from this SDK lives 60 seconds, so a host clock more than about 30 seconds fast or 60 seconds slow fails in both directions. Run NTP.
