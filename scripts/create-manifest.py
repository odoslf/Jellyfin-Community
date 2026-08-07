#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from urllib.parse import urlparse

PLUGIN_GUID = "c24c5b8e-2fa8-47f6-a671-a7eb9d60c114"
PLUGIN_VERSION = "1.1.0.0"
TARGET_ABI = "10.10.7.0"


def main() -> int:
    parser = argparse.ArgumentParser(description="Create a Jellyfin plugin repository manifest entry.")
    parser.add_argument("--zip", required=True, type=Path, dest="zip_path")
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    archive = args.zip_path.resolve()
    if not archive.is_file():
        parser.error(f"Package does not exist: {archive}")

    parsed = urlparse(args.source_url)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
        parser.error("--source-url must be an absolute HTTPS URL without embedded credentials")

    checksum = hashlib.md5(archive.read_bytes(), usedforsecurity=False).hexdigest().upper()
    manifest = [
        {
            "guid": PLUGIN_GUID,
            "name": "Community",
            "description": "Foro comunitario local integrado con usuarios y bibliotecas de Jellyfin.",
            "overview": "Debates, mensajes, encuestas, notificaciones y moderación dentro de Jellyfin.",
            "owner": "odoslf",
            "category": "General",
            "versions": [
                {
                    "version": PLUGIN_VERSION,
                    "changelog": "Community 1.1 corrige la integración con Jellyfin Web 10.10.7, añade acceso desde el menú para usuarios, panel administrativo funcional y validación E2E real.",
                    "targetAbi": TARGET_ABI,
                    "sourceUrl": args.source_url,
                    "checksum": checksum,
                    "timestamp": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
                    "dependencies": [],
                }
            ],
        }
    ]

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
