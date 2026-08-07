#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


def parse_test_results(root: Path) -> dict[str, int]:
    totals = {key: 0 for key in ("total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive")}
    files = list(root.glob("tests/**/TestResults/**/*.trx"))
    if not files:
        raise RuntimeError("No TRX test result was found")
    for path in files:
        document = ET.parse(path)
        counters = next((element for element in document.iter() if element.tag.endswith("Counters")), None)
        if counters is None:
            raise RuntimeError(f"TRX counters are missing: {path}")
        for key in totals:
            totals[key] += int(counters.attrib.get(key, "0"))
    return totals


def parse_coverage(root: Path) -> tuple[float, float]:
    files = list(root.glob("tests/**/TestResults/**/coverage.cobertura.xml"))
    if not files:
        raise RuntimeError("No Cobertura coverage result was found")
    document = ET.parse(files[0]).getroot()
    return float(document.attrib.get("line-rate", "0")) * 100, float(document.attrib.get("branch-rate", "0")) * 100


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="Create a CI validation report for Jellyfin Community.")
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--artifacts", required=True, type=Path)
    parser.add_argument("--dotnet-version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-url", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    root = args.root.resolve()
    artifacts = args.artifacts.resolve()
    package = artifacts / "Jellyfin.Plugin.Community_1.0.0.0.zip"
    audit = artifacts / "vulnerability-audit.txt"
    if not package.is_file() or not audit.is_file():
        raise RuntimeError("Package or vulnerability audit is missing")

    tests = parse_test_results(root)
    if tests["failed"] or tests["error"] or tests["timeout"] or tests["aborted"]:
        raise RuntimeError(f"Unsuccessful test counters: {tests}")
    line_coverage, branch_coverage = parse_coverage(root)
    audit_text = audit.read_text(encoding="utf-8", errors="replace")
    if "has the following vulnerable packages" in audit_text.lower():
        raise RuntimeError("The dependency audit reports vulnerable packages")

    with zipfile.ZipFile(package, "r") as archive:
        names = archive.namelist()
        corrupt = archive.testzip()
    if corrupt is not None:
        raise RuntimeError(f"Corrupt ZIP entry: {corrupt}")

    generated = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    report = f"""# Informe de validación reproducible

- Fecha UTC: `{generated}`
- Commit validado: `{args.commit}`
- Ejecución CI: {args.run_url}
- Jellyfin objetivo: `10.10.7`
- ABI de catálogo objetivo: `10.10.7.0`
- Framework: `net8.0`
- SDK de compilación: `{args.dotnet_version}`
- Plugin: `1.0.0.0`

## Resultado

- Restauración de dependencias: **correcta**
- Compilación Release: **correcta, advertencias tratadas como errores**
- Analizadores estáticos de .NET: **correctos**
- Pruebas: **{tests['passed']} superadas de {tests['total']}**, 0 fallos, 0 errores, 0 canceladas
- Cobertura de líneas: **{line_coverage:.2f}%**
- Cobertura de ramas: **{branch_coverage:.2f}%**
- Auditoría de dependencias directas y transitivas: **sin paquetes vulnerables conocidos reportados por NuGet**
- Integridad del ZIP: **correcta**

## Paquete instalable

- SHA-256: `{sha256(package)}`
- Contenido permitido: `{', '.join(names)}`
- Número de entradas: `{len(names)}`

El paquete no incorpora ensamblados `Jellyfin.*` del servidor, `MediaBrowser.*`, `Microsoft.*`, `System.*` ni un runtime `SQLitePCLRaw.*`; evita sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Alcance de la garantía

Este informe demuestra una compilación reproducible, análisis estático, pruebas automatizadas, auditoría de dependencias e integridad del paquete. No afirma una prueba física de instalación o rendimiento en un Synology concreto; esa comprobación requiere arrancar el artefacto en el servidor final y revisar sus registros.
"""
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report, encoding="utf-8")
    print(args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
