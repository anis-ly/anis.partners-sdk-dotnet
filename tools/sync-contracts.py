"""Vendor complete owner files, record provenance, and regenerate errors in one command.

python3 tools/sync-contracts.py
python3 tools/sync-contracts.py --check

Sources default to sibling worktrees. ANIS_GATEWAY_CONTRACTS and ANIS_CONFORMANCE_ROOT
override them. --check compares bytes with live owners; normal SDK tests use the pinned
SHA-256 manifest without needing sibling checkouts. No routes are removed from OpenAPI.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "contracts/sources.json"


def git(directory, *args):
    return subprocess.check_output(["git", "-C", str(directory), *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    gateway = Path(os.environ.get("ANIS_GATEWAY_CONTRACTS", ROOT.parent / "anis.partners-consumers-gateway/contracts")).resolve()
    conformance = Path(os.environ.get("ANIS_CONFORMANCE_ROOT", ROOT.parent / "anis.partners-sdk-conformance")).resolve()
    for source in [gateway / "openapi/partner-public-v1.json", gateway / "error-catalogue.json", conformance / "vectors/webhook/manifest.json"]:
        if not source.is_file():
            parser.error(f"missing {source}; configure the owner source paths")

    sources = [
        ("anis.partners-consumers-gateway", gateway, Path("contracts"), gateway),
        ("anis.partners-sdk-conformance", conformance / "vectors", Path("tests/Anis.Partners.Sdk.Tests/vectors"), conformance),
    ]
    records = []
    stale = []
    for repository, source_folder, target_folder, git_folder in sources:
        revision = git(git_folder, "rev-parse", "HEAD")
        repository_root = Path(git(git_folder, "rev-parse", "--show-toplevel"))
        for source in sorted(source_folder.rglob("*")):
            if not source.is_file():
                continue
            relative = source.relative_to(source_folder)
            # Preserve the SDK's established path, with the owner's complete bytes.
            if repository == "anis.partners-consumers-gateway" and relative.as_posix() == "openapi/partner-public-v1.json":
                relative = Path("partner-public-v1.json")
            target = target_folder / relative
            payload = source.read_bytes()
            records.append({
                "repository": repository,
                "revision": revision,
                "source": source.relative_to(repository_root).as_posix(),
                "sourceModified": bool(git(repository_root, "status", "--porcelain", "--", str(source))),
                "target": target.as_posix(),
                "sha256": hashlib.sha256(payload).hexdigest(),
            })
            destination = ROOT / target
            current = destination.read_bytes() if destination.exists() else None
            if current != payload:
                stale.append(target.as_posix())
                if not args.check:
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(payload)

    if MANIFEST.exists():
        previous = json.loads(MANIFEST.read_text())["files"]
        targets = {record["target"] for record in records}
        for old in previous:
            if old["target"] not in targets:
                target = Path(old["target"])
                if target.is_absolute() or ".." in target.parts or not any(target.is_relative_to(folder) for _, _, folder, _ in sources):
                    parser.error("unsafe path in the previous source manifest")
                destination = ROOT / target
                if destination.exists():
                    stale.append(target.as_posix())
                    if not args.check:
                        destination.unlink()
    manifest = (json.dumps({"files": records}, indent=2) + "\n").encode()
    current_manifest = MANIFEST.read_bytes() if MANIFEST.exists() else None
    if current_manifest != manifest:
        stale.append("contracts/sources.json")
        if not args.check:
            MANIFEST.write_bytes(manifest)
    if args.check:
        if stale:
            print("STALE: " + ", ".join(stale))
            return 1
        subprocess.run([sys.executable, str(ROOT / "tools/generate-errors.py"), "--check"], check=True)
    else:
        subprocess.run([sys.executable, str(ROOT / "tools/generate-errors.py")], check=True)
    print(f"{'checked' if args.check else 'synced'} {len(records)} complete owner files; {len(stale)} changed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
