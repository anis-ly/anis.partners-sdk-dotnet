# Maintainer tools

These scripts are for people who maintain this SDK. They are not part of the package.

## Refresh the contract snapshot

Run from the SDK root after the gateway announces **gateway contract final**:

```bash
python3 tools/sync-contracts.py
python3 tools/sync-contracts.py --check
```

The first command vendors complete gateway contract files and the full conformance corpus byte for byte,
records per-file source revisions, modified-source flags and SHA-256 in `contracts/sources.json`, and regenerates
errors. Sources default to sibling worktrees; override `ANIS_GATEWAY_CONTRACTS` and `ANIS_CONFORMANCE_ROOT` if
needed. The snapshot can include staged, uncommitted owner changes; its hashes identify those exact bytes.
`--check` compares live owner bytes and generated errors without changing anything. The normal test suite checks
vendored hashes, route metadata, public error codes and product schemas without sibling checkouts.

The complete OpenAPI retains the three staging network operations. The single named test constant
`StagingTestOnlyRoutes` excludes exactly those operations from SDK route comparisons. They are staging-only
testing endpoints, unavailable in production; they are never SDK operations. No other route is excluded.
