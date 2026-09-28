# Anis.Partners

The .NET client for the Anis Partner API, for **.NET 8** and **.NET 10**.

Every request is signed (RFC 9421 over P-256), every response is verified before you see it, and the
operations are typed. Ordering, idempotent retry, one-time card codes and recovery after a timeout are
shaped so they cannot go wrong quietly.

```bash
dotnet add package Anis.Partners
```

```csharp
builder.Services
    .AddAnisPartners(builder.Configuration.GetSection("AnisPartners"))
    .WithSigner(EcdsaP256Signer.FromPemFile("/secure/partner-key.pem", keyId));
```

```json
{
  "AnisPartners": {
    "Authority": "https://<the address Anis gave you>",
    "SignatureLifetime": "00:01:00",
    "AcceptLanguage": "English"
  }
}
```

Then inject `IAnisPartnersClient` and call it:

```csharp
var profile = await client.Profile.GetAsync(cancellationToken);
```

## Documentation

- [Getting started, enrolment and your first call](https://developers.anis.ly/sdks/dotnet)
- [Orders and recovery](https://github.com/anis-ly/anis.partners-sdk-dotnet/blob/main/docs/orders-and-recovery.md) — read this before you take money
- [Errors](https://github.com/anis-ly/anis.partners-sdk-dotnet/blob/main/docs/errors.md)
- [Security and key custody](https://github.com/anis-ly/anis.partners-sdk-dotnet/blob/main/docs/security.md)
- [Observability](https://github.com/anis-ly/anis.partners-sdk-dotnet/blob/main/docs/observability.md)
- [A working sample for every route](https://github.com/anis-ly/anis.partners-sdk-dotnet/tree/main/samples/Anis.Partners.Sample.Console)
- [Changelog](https://github.com/anis-ly/anis.partners-sdk-dotnet/blob/main/CHANGELOG.md)
