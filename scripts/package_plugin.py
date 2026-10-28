#!/usr/bin/env python3
"""Package the plugin the way Jellyfin expects it.

Produces ``dist/JellyFix_<version>.zip`` containing the plugin assemblies from
build.yaml plus a ``meta.json`` whose version is injected from the release tag, so
the version is maintained in exactly one place (the git tag).

Usage:
    python3 scripts/package_plugin.py --version 1.0.0.1
    python3 scripts/package_plugin.py --version 1.0.0.1 --build-dir path/to/bin/Release/net9.0
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import zipfile

try:
    import yaml
except ImportError:  # pragma: no cover - CI installs PyYAML
    sys.exit("PyYAML is required: python3 -m pip install pyyaml")

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent


def normalize(text: str) -> str:
    return " ".join((text or "").split())


def build_meta(build: dict, version: str) -> dict:
    return {
        "category": build["category"],
        "guid": build["guid"],
        "name": build["name"],
        "description": normalize(build.get("description", "")),
        "overview": normalize(build.get("overview", "")),
        "owner": build["owner"],
        "version": version,
        "targetAbi": build["targetAbi"],
        "changelog": normalize(build.get("changelog", "")),
        "status": "Active",
        "autoUpdate": False,
        "assemblies": build.get("artifacts", []),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="Plugin version, for example 1.0.0.1")
    parser.add_argument("--meta", default="build.yaml", help="Path to the plugin metadata file")
    parser.add_argument(
        "--build-dir",
        default="Jellyfin.Plugin.JellyFix/bin/Release/net9.0",
        help="Directory containing the built plugin assemblies",
    )
    parser.add_argument("--out-dir", default="dist", help="Directory for the produced zip")
    parser.add_argument("--name", default=None, help="Zip base name (defaults to the plugin name)")
    args = parser.parse_args()

    meta_path = REPO_ROOT / args.meta
    build = yaml.safe_load(meta_path.read_text(encoding="utf-8"))
    build_dir = REPO_ROOT / args.build_dir
    out_dir = REPO_ROOT / args.out_dir

    artifacts = build.get("artifacts") or []
    if not artifacts:
        sys.exit(f"{meta_path} does not declare any artifacts")

    missing = [name for name in artifacts if not (build_dir / name).is_file()]
    if missing:
        sys.exit(f"missing build output(s) in {build_dir}: {', '.join(missing)}")

    out_dir.mkdir(parents=True, exist_ok=True)
    zip_name = f"{args.name or build['name']}_{args.version}.zip"
    zip_path = out_dir / zip_name
    meta = build_meta(build, args.version)

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in artifacts:
            archive.write(build_dir / name, arcname=name)
        archive.writestr("meta.json", json.dumps(meta, indent=2) + "\n")

    print(f"created {zip_path.relative_to(REPO_ROOT)} (version {args.version})")
    with zipfile.ZipFile(zip_path) as archive:
        for info in archive.infolist():
            print(f"  {info.file_size:>8}  {info.filename}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
