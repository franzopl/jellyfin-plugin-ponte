#!/usr/bin/env python3
"""Packages a built release and records it in the plugin repository manifest.

    scripts/package.py 0.1.0 "What changed" [--base-url URL]

Reads the Release build of both target frameworks, writes dist/ponte_<version>.<abi>.zip for
each Jellyfin ABI and adds those versions to manifest.json (newest first). Run after
`dotnet build -c Release -p:PonteVersion=<version>`.
"""

import argparse
import datetime
import hashlib
import json
import pathlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src" / "Jellyfin.Plugin.Ponte"
DIST = ROOT / "dist"
MANIFEST = ROOT / "manifest.json"
REPO = "https://github.com/franzopl/jellyfin-plugin-ponte"

PLUGIN = {
    "guid": "1bbb9efc-a177-428b-acdc-8d9370cc3a62",
    "name": "Ponte",
    "description": (
        "Ponte serves your Jellyfin movies and shows as a Stremio addon: catalogs, metadata, "
        "streams straight from the files and external subtitles, for Stremio, Nuvio, AIOStreams "
        "and any other addon client. Each user gets a private addon link that plays as them."
    ),
    "overview": "Your Jellyfin library as a Stremio addon",
    "owner": "franzopl",
    "category": "General",
    "imageUrl": "https://raw.githubusercontent.com/franzopl/jellyfin-plugin-ponte/main/assets/banner.png",
}

# Target framework -> (version suffix, minimum Jellyfin ABI)
ABIS = {
    "net9.0": ("10", "10.11.0.0"),
    "net10.0": ("12", "12.0.0.0"),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("version", help="plugin version without the ABI suffix, e.g. 0.1.0")
    parser.add_argument("changelog")
    parser.add_argument("--base-url", default=f"{REPO}/releases/download")
    args = parser.parse_args()

    DIST.mkdir(exist_ok=True)
    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    versions = []

    for tfm, (suffix, abi) in ABIS.items():
        version = f"{args.version}.{suffix}"
        dll = PROJECT / "bin" / "Release" / tfm / "Jellyfin.Plugin.Ponte.dll"
        if not dll.exists():
            raise SystemExit(f"missing {dll}: build with -c Release first")

        meta = dict(PLUGIN, version=version, targetAbi=abi, changelog=args.changelog, timestamp=timestamp)
        meta.pop("imageUrl")
        zip_path = DIST / f"ponte_{version}.zip"
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
            z.write(dll, dll.name)
            z.writestr("meta.json", json.dumps(meta, indent=2))

        checksum = hashlib.md5(zip_path.read_bytes()).hexdigest()
        versions.append({
            "version": version,
            "changelog": args.changelog,
            "targetAbi": abi,
            "sourceUrl": f"{args.base_url}/v{args.version}/{zip_path.name}",
            "checksum": checksum,
            "timestamp": timestamp,
        })
        print(f"{zip_path.relative_to(ROOT)}  abi {abi}  md5 {checksum}")

    manifest = json.loads(MANIFEST.read_text()) if MANIFEST.exists() else [dict(PLUGIN, versions=[])]
    entry = manifest[0]
    entry.update(PLUGIN)
    new = {v["version"] for v in versions}
    entry["versions"] = versions + [v for v in entry.get("versions", []) if v["version"] not in new]
    entry["versions"].sort(key=lambda v: [int(p) for p in v["version"].split(".")], reverse=True)
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"manifest.json: {len(entry['versions'])} version(s)")


if __name__ == "__main__":
    main()
