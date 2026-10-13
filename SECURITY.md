# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Email **support@anis.ly** with a description, the
version of `Anis.Partners` you used, and the steps to reproduce it. You will get an answer within five
working days.

## Supported versions

Security fixes target **2.0.1**. **1.x is deprecated — upgrade.** Signed answers require the `anis-response-v2`
profile, request signature binding and the published key lifecycle checks described in the security guide.

## Your private key

This package never sends, stores or logs your private key. It only asks your `IRequestSigner` for a
signature. Keep the key where your organisation keeps its secrets; see
[Security and key custody](docs/security.md).
