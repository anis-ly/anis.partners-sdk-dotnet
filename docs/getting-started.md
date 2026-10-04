# Getting started

## 1. Enrollment — once per key, before anything else

You cannot call the API until a key of yours is active. Anis staff create your application and send you an
**invitation id** and a single-use **enrollment token**. You then:

1. generate a P-256 key pair — the private half never leaves your side;
2. submit the **public** half, and receive a challenge;
3. prove you hold the private half;
4. read your key's **safety code** to Anis staff when they phone your technical contact. They check it against
   the key they hold and confirm the key, and from that moment it signs requests.

```csharp
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

// Protect the PRIVATE half FIRST, before Anis hears about the key. An invitation takes exactly one key: if
// saving failed after the public half was accepted, the invitation would be spent on a key nobody holds.
await File.WriteAllTextAsync("/secure/partner-key.pem", key.ExportPkcs8PrivateKeyPem(), ct);

using var enrollment = AnisEnrollmentClient.Create(authority, invitationId, enrollmentToken);

var submitted = await enrollment.SubmitKeyAsync(new EnrollmentKeyRequest
{
    PublicJwk = AnisEnrollmentClient.PublicJwkOf(key),   // public members only; a `d` is refused
    NotBefore = DateTimeOffset.UtcNow,
    ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
}, ct);

var status = await enrollment.ProveAsync(submitted, key, ct);
if (status.ProofState != "accepted")
    throw new InvalidOperationException("The proof failed — check the key, or ask Anis staff to restart if the challenge expired.");   // state stays pendingProof

Console.WriteLine($"key id {submitted.KeyId}");
Console.WriteLine($"Your safety code: {submitted.SafetyCode} — Anis staff will call you and ask you to read it.");
```

`submitted.KeyId` is the key id you sign with from then on. `submitted.SafetyCode` is the 16-character code
(`XXXX-XXXX-XXXX-XXXX`) derived from your key's fingerprint; keep it where the person who answers the call can
read it. The call goes to the technical contact you gave Anis, so make sure that person knows to expect it. You
send nobody a fingerprint: the call is the check.

`SubmitKeyAsync` also protects you from a swapped key. It computes the thumbprint of the key you sent
(`KeyThumbprint.Compute`) and compares it, in fixed time, with the one Anis answers with. If they differ it
throws `EnrollmentKeyMismatchException` and gives you no result, so no proof can be built on it: ask Anis staff
to restart the enrollment, and look at anything between you and Anis that rewrites request bodies before you
enrol again. The safety code is `SafetyCode.FromThumbprint(thumbprint)`; the SDK uses the one Anis answers with
and derives it itself when the answer has none.

Once `ProofState` is `accepted`, wait for staff to confirm. `GetStatusAsync` reports where the key stands:

| `state` | Meaning |
|---|---|
| `pendingProof` | submitted; possession not yet proved |
| `pendingApproval` | proved; waiting for Anis staff to verify your safety code by phone and confirm the key |
| `active` | confirmed — the key signs requests |
| `unavailable` | the key is being replaced by a newer one; once a key has ended (revoked, retired or past its end date) this read is refused with `resource_not_found` |

Once the key is `active`, `KeyExpiresAt` is the date it stops working. Ask Anis staff for a replacement weeks before
it: past it, every call is refused with `invalid_credentials`, including reveals of cards you already bought.

**The proof is not a signature over the challenge.** Anis keeps only a hash of the challenge, so the proof
signs a message built from values both sides hold. `ProveAsync` does it for you; if your key lives in a vault
or HSM, sign `AnisEnrollmentClient.ProofMessage(submitted)` there (ECDSA P-256/SHA-256, IEEE P1363, 64 bytes)
and send `AnisEnrollmentClient.CreateProof(submitted, signature)` with `SubmitProofAsync`.

What Anis may refuse, as `EnrollmentRefusedException`:

| Code | Meaning |
|---|---|
| `invitation_invalid` | unknown, used or expired invitation or token, or five failed proofs; ask Anis for a new invitation |
| `challenge_expired` | the proof was built on a challenge generation that is no longer current: Anis staff restarted this enrollment after you received the challenge. Enrol again with the new invitation token staff send you |
| `key_proof_invalid` | the public key you submitted is not a usable P-256 public key — malformed, of another type or curve, or carrying a private member. Submit the public half exactly as `AnisEnrollmentClient.PublicJwkOf` builds it; a JWK the SDK cannot fingerprint is refused on your side before anything is sent, with an `ArgumentException` |
| `key_duplicate` | the key is not waiting for this step — usually this invitation has already taken a key (a submission sent again after its answer was lost), or a proof arrived for a key that no longer waits for one. Ask Anis staff to restart the enrollment; on the proof, it means the proof was already accepted and was sent again — read the status (`GetStatusAsync`) first |

**A proof that fails is not a refusal.** When the signature does not verify, or the proof arrives after the
challenge expired, `ProveAsync` answers normally with `ProofState` `"failed"` and the key keeps waiting for a proof.
Check `ProofState` before you contact staff. Fix the proof (the right key, `ProofMessage` unchanged, a 64-byte
P1363 signature) and send it again — but more than about 30 minutes after the key submission a proof can no longer succeed: do not prove again, ask Anis staff to restart the enrollment. After five failed proofs the next one is refused with `invitation_invalid`.

The enrollment token carries at least 256 bits of entropy and is stored by Anis only as a SHA-256 and never
logged — the SDK does not log it either. Treat it the same way. Two clocks run during enrollment:

- **the invitation** is valid for about a day by default — `GetAsync` reports its exact `expiresAt`;
- **the proof** must follow the key submission within about 30 minutes by default; a later proof comes back with
  `ProofState` `"failed"` and staff must restart the enrollment. If your key lives in a vault or HSM whose signing
  needs an approval, arrange that approval **before** you submit the key.

If the key submission gets no answer (a timeout, a dropped connection), you cannot tell whether Anis took
the key, and the challenge you need for the proof travelled in the answer you lost. Submit the **same** key
once more. If it is accepted, carry on. If it is refused with `key_duplicate`, the first submission was
taken: ask Anis staff to restart the enrollment, which gives you a new invitation token, and enrol again.
(Do not rely on `GetAsync` here: it reads a copy of the enrollment that can trail the live state by a few
moments.)

The validity window you ask for is used for its **length** only: Anis keeps the shorter of it and the validity Anis
staff set (never more than two years), and starts it when the key is committed. Tell Anis staff the networks you will
call from when you ask for access. A request from any other address is refused with `403 insufficient_scope` — the
same answer as a missing permission, on purpose, so the refusal never tells a caller whether its address or its grant
is the thing to work around. If a call that should be permitted is refused that way, check both with Anis.

## 2. Register the client

From a settings file:

```json
{
  "AnisPartners": {
    "Authority": "https://<the authority Anis gave you>",
    "SignatureLifetime": "00:01:00",
    "AcceptLanguage": "Arabic",
    "SigningKeyCacheDuration": "00:10:00",
    "Timeout": "00:00:30"
  }
}
```

```csharp
builder.Services
    .AddAnisPartners(builder.Configuration.GetSection(AnisPartnersClientOptions.SectionName))
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/partner-key.pem", keyId));
```

Or in code:

```csharp
builder.Services
    .AddAnisPartners(options =>
    {
        options.Authority         = new Uri("https://<the authority Anis gave you>");
        options.SignatureLifetime = TimeSpan.FromSeconds(60);   // 1 to 60 seconds
        options.AcceptLanguage    = AnisLanguage.Arabic;        // presentation only
    })
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/partner-key.pem", keyId));
```

Either way the settings are validated when the host starts: a missing authority or a signature lifetime
above 60 seconds stops it there, not at the first call. (Anis would admit up to 300 seconds, but the SDK
accepts Anis's answers only within 60 seconds of your clock; with a longer signature and a slow clock an
order could complete and its answer — with the card codes — be discarded as too old.) A registration that never names a signer fails when the
client is first resolved, saying no `IRequestSigner` is registered.

**No automatic retries on this client.** If your host adds a retry policy to every HTTP client (a
`ConfigureHttpClientDefaults(... AddStandardResilienceHandler())`, as the .NET Aspire service defaults do),
the SDK still signs every attempt correctly — but that policy's per-attempt timeout (10 seconds by
default) is shorter than the SDK's own `Timeout` (30 seconds). A slow order is abandoned and sent again: the
first attempt completes at Anis, its answer — with the card codes — is thrown away, and the second attempt
receives "already delivered", without codes. A retried reveal also spends your reveal limit twice. Retry reads
yourself if you want to; recover orders with `ResumeAsync` (see [Orders and recovery](orders-and-recovery.md)).

The SDK registers a clock and a nonce source only if your host has none, so a clock you registered for
your own code stays yours — and the SDK uses it too.

### Several Anis applications in one host

One registration is one Anis application — its authority, key and permissions. A host acting for several
(two brands, say) registers each under a name of its choosing:

```csharp
builder.Services
    .AddAnisPartners("brand-a", builder.Configuration.GetSection("AnisPartners:BrandA"))
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/brand-a.pem", brandAKeyId));

builder.Services
    .AddAnisPartners("brand-b", builder.Configuration.GetSection("AnisPartners:BrandB"))
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/brand-b.pem", brandBKeyId));
```

Then take the one you need from `IAnisPartnersClientFactory` — `factory.GetClient("brand-a")` — or inject it
as a keyed service: `[FromKeyedServices("brand-a")] IAnisPartnersClient anis`. Each has its own HTTP
pipeline and signer, and its traces and metrics carry `anis.client = brand-a`. Registering the same name
twice, or `AddAnisPartners` twice without a name, stops the host at startup: the second registration would
otherwise stack onto the first and sign with the wrong key.

### If your host replaces .NET's built-in container

`AddAnisPartners` uses .NET 8 keyed services. If your host swaps in a third-party container
(`UseServiceProviderFactory(...)`), that container must support them — Autofac 9 or later, Lamar 12.1 or
later, and SimpleInjector (which leaves the built-in container in place) all do. One that does not stops the
host at startup with *"This service descriptor is keyed. Your service provider may not support keyed
services."* In that case build the client directly instead: `AnisPartnersClient.Create(options, signer)` — requests are signed and answers verified without the container. Build it once and keep it.

**Note what is not there.** No environment. No key. See [Security](security.md).

## 3. First call

```csharp
var profile = await anis.Profile.GetAsync(ct);
```

`profile.Application.Scopes` is what your application may do **right now**. Scopes come from the live
policy and are re-evaluated on every call, so read them rather than caching them — a scope removed by an
administrator takes effect on your next request, not on your next restart. Which route needs which scope:
[Routes and permissions](routes-and-permissions.md).

## When a signature will not verify

Ask the gateway what it saw — the signature self-check has no side effects and is served by every
deployment:

```csharp
var diagnostic = await anis.Diagnostics.CheckSignatureAsync(ct);

Console.WriteLine(diagnostic.Method);            // as the gateway saw it
Console.WriteLine(diagnostic.Authority);
Console.WriteLine(diagnostic.Path);
Console.WriteLine(diagnostic.CanonicalQuery);    // WITHOUT its leading '?'
Console.WriteLine(string.Join(" ", diagnostic.CoveredComponents));
Console.WriteLine(diagnostic.KeyId);             // the key it resolved
```

Then follow what the self-check answers. It needs `diagnostics:use`.

- **It succeeds.** Your key and clock are fine. Compare each line with what the failing call signed — it is almost
  always the query or the authority (which must be the address Anis gave you), or a body changed after it was
  digested.
- **`invalid_credentials`.** The key cannot be used here: the wrong key id, a key file that does not match it, a key
  not yet active, revoked, replaced after its overlap ended or past its end date (`EnrollmentStatus.KeyExpiresAt`), a
  host clock more than about 30 seconds fast or 60 seconds slow, or the wrong address — the base URL must be the one
  Anis gave you, and a proxy must not rewrite the host. Check the address before you ask for a new key.
- **`malformed_signed_request`.** The request was rewritten on its way to Anis — usually a proxy that changes headers
  or the body. The SDK builds the signature itself.
- **`insufficient_scope`.** The call came from outside your agreed networks, or the application lacks
  `diagnostics:use`.

If nothing changed on your side and the self-check fails too, your access may be paused: write to support@anis.ly with
the request id.
