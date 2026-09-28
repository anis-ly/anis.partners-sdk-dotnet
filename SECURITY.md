# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Email **support@anis.ly** with a description, the
version of `Anis.Partners` you used, and the steps to reproduce it. You will get an answer within five
working days.

## Supported versions

Security fixes are made to the latest released version.

## Your private key

This package never sends, stores or logs your private key. It only asks your `IRequestSigner` for a
signature. Keep the key where your organisation keeps its secrets; see
[Security and key custody](docs/security.md).
