#!/usr/bin/env python3
"""Add or replace a version entry in the Jellyfin plugin repository manifest (newest first).

usage: update_manifest.py <manifest.json> <tag-or-version> <zip> <source-url> [changelog] [target-abi]
  tag-or-version: "v0.1.1", "0.1.1" or "0.1.1.0" (always stored as four parts)
"""
import hashlib, json, sys
from datetime import datetime, timezone

def four_part(v: str) -> str:
    parts = v.lstrip("v").split(".")
    if not 1 <= len(parts) <= 4 or not all(p.isdigit() for p in parts):
        sys.exit(f"invalid version: {v}")
    return ".".join(parts + ["0"] * (4 - len(parts)))

def main() -> None:
    if len(sys.argv) < 5:
        sys.exit(__doc__)
    manifest_path, tag, zip_path, url = sys.argv[1:5]
    changelog = sys.argv[5] if len(sys.argv) > 5 else "See the GitHub release notes."
    abi = sys.argv[6] if len(sys.argv) > 6 else "12.4.0.0"
    version = four_part(tag)
    md5 = hashlib.md5(open(zip_path, "rb").read()).hexdigest().upper()
    entry = {
        "version": version,
        "changelog": changelog,
        "targetAbi": abi,
        "sourceUrl": url,
        "checksum": md5,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    data = json.load(open(manifest_path, encoding="utf-8"))
    plugin = data[0]
    plugin["versions"] = [entry] + [v for v in plugin["versions"] if v["version"] != version]
    plugin["versions"].sort(key=lambda v: tuple(int(x) for x in v["version"].split(".")), reverse=True)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"manifest: {version} {md5}")

main()
