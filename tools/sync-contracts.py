"""Re-makes the two reduced contract copies in contracts/ from the Consumer Gateway, which owns them.

Run:  python3 tools/sync-contracts.py            write the copies, then run tools/generate-errors.py
      python3 tools/sync-contracts.py --check    fail if a copy differs from what the gateway publishes

The gateway folder defaults to ../../Ecom-LTD/anis.partners-consumers-gateway/contracts; set
ANIS_GATEWAY_CONTRACTS to point elsewhere. The copies are reduced on purpose: the catalogue keeps only the
public rows, without their documentation URL; the OpenAPI keeps only each route's signing kind.
"""
import json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GATEWAY = os.environ.get("ANIS_GATEWAY_CONTRACTS") or os.path.join(
    ROOT, "..", "..", "Ecom-LTD", "anis.partners-consumers-gateway", "contracts")
HERE = os.path.join(ROOT, "contracts")


def load(path):
    if not os.path.exists(path):
        sys.exit(f"missing {path} -- set ANIS_GATEWAY_CONTRACTS to the gateway's contracts folder")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def catalogue(gateway, current):
    rows = [{k: v for k, v in r.items() if k != "type"}
            for r in gateway["representations"] if r.get("publicDocumentation")]
    return {"description": current["description"], "representations": rows}


def openapi(gateway, current):
    paths = {}
    for path, operations in gateway["paths"].items():
        kept = {method: {"x-anis-route": {"requestKind": operation["x-anis-route"]["requestKind"]}}
                for method, operation in operations.items()
                if isinstance(operation, dict) and "requestKind" in operation.get("x-anis-route", {})}
        if kept:
            paths[path] = kept
    return {"openapi": current["openapi"], "info": current["info"], "paths": paths}


check = "--check" in sys.argv
stale = 0
for source, target, reduce in [("error-catalogue.json", "error-catalogue.json", catalogue),
                               (os.path.join("openapi", "partner-public-v1.json"), "partner-public-v1.json", openapi)]:
    target_path = os.path.join(HERE, target)
    text = json.dumps(reduce(load(os.path.join(GATEWAY, source)), load(target_path)), indent=2, ensure_ascii=False) + "\n"
    with open(target_path, encoding="utf-8") as f:
        current = f.read()
    if text == current:
        print(f"up to date  {target}")
    elif check:
        print(f"STALE       {target}")
        stale += 1
    else:
        with open(target_path, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"written     {target}")
sys.exit(1 if stale else 0)
