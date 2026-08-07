#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import zipfile

PLUGIN_DLL = "Jellyfin.Plugin.Community.dll"
ALLOWED_FILES = {PLUGIN_DLL, "Markdig.dll"}
FORBIDDEN_PACKAGE_PREFIXES = (
    "MediaBrowser.",
    "Microsoft.",
    "System.",
    "SQLitePCLRaw.",
)


def main() -> int:
    parser = argparse.ArgumentParser(description="Package Jellyfin Community publish output.")
    parser.add_argument("--publish", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    publish = args.publish.resolve()
    output = args.output.resolve()
    if not publish.is_dir():
        parser.error(f"Publish directory does not exist: {publish}")

    publish_files = [path for path in publish.rglob("*") if path.is_file()]
    by_name: dict[str, Path] = {}
    duplicate_names: set[str] = set()
    for path in publish_files:
        if path.name in by_name:
            duplicate_names.add(path.name)
        by_name[path.name] = path
    if duplicate_names:
        parser.error("Duplicate publish filenames are ambiguous: " + ", ".join(sorted(duplicate_names)))

    missing = sorted(name for name in ALLOWED_FILES if name not in by_name)
    if missing:
        parser.error("Required runtime files are missing: " + ", ".join(missing))

    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        output.unlink()

    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(ALLOWED_FILES):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, by_name[name].read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

    with zipfile.ZipFile(output, "r") as archive:
        names = archive.namelist()
        packaged = set(names)
        if packaged != ALLOWED_FILES or len(names) != len(ALLOWED_FILES):
            parser.error(f"Unexpected package contents: {sorted(names)}")
        bad_paths = [name for name in names if Path(name).name != name]
        if bad_paths:
            parser.error("Package contains nested or unsafe paths: " + ", ".join(bad_paths))
        forbidden = sorted(
            name
            for name in names
            if name.startswith(FORBIDDEN_PACKAGE_PREFIXES)
            or (name.startswith("Jellyfin.") and name != PLUGIN_DLL)
        )
        if forbidden:
            parser.error(
                "Package contains host/runtime assemblies that could shadow Jellyfin 10.10.7: "
                + ", ".join(forbidden)
            )
        test_result = archive.testzip()
        if test_result is not None:
            parser.error(f"Corrupt ZIP entry: {test_result}")

    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(
        f"{digest}  {output.name}\n", encoding="utf-8"
    )
    print(f"Packaged files: {', '.join(sorted(ALLOWED_FILES))}")
    print(output)
    print(digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
