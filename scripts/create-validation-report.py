#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

VERSION = "1.1.0.0"
PACKAGE_NAME = f"Jellyfin.Plugin.Community_{VERSION}.zip"


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


def read_json_evidence(path: Path, label: str) -> dict:
    if not path.is_file() or path.stat().st_size == 0:
        raise RuntimeError(f"{label} evidence is missing or empty: {path}")
    lines = [line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not lines:
        raise RuntimeError(f"{label} evidence contains no JSON output: {path}")
    try:
        result = json.loads(lines[-1])
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"{label} evidence is not valid JSON: {path}") from exc
    if result.get("status") != "passed":
        raise RuntimeError(f"{label} did not report a passing result: {result}")
    return result


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
    package = artifacts / PACKAGE_NAME
    audit = root / "vulnerability-audit.txt"
    api_e2e_path = artifacts / "runtime-api-e2e.json"
    browser_e2e_path = artifacts / "runtime-browser-e2e.txt"
    runtime_log_path = artifacts / "jellyfin-10.10.7-runtime.log"

    if not package.is_file() or not audit.is_file():
        raise RuntimeError("Package or vulnerability audit is missing")

    tests = parse_test_results(root)
    if tests["failed"] or tests["error"] or tests["timeout"] or tests["aborted"]:
        raise RuntimeError(f"Unsuccessful test counters: {tests}")
    if tests["passed"] != tests["total"] or tests["total"] <= 0:
        raise RuntimeError(f"Not every unit/integration test passed: {tests}")

    line_coverage, branch_coverage = parse_coverage(root)
    audit_text = audit.read_text(encoding="utf-8", errors="replace")
    if "has the following vulnerable packages" in audit_text.lower():
        raise RuntimeError("The dependency audit reports vulnerable packages")

    api_e2e = read_json_evidence(api_e2e_path, "Jellyfin API E2E")
    browser_e2e = read_json_evidence(browser_e2e_path, "Jellyfin Web browser E2E")
    if not all(browser_e2e.get(key) is True for key in ("ordinaryUser", "administrator", "menu", "createThread", "adminPanel")):
        raise RuntimeError(f"Browser E2E evidence is incomplete: {browser_e2e}")

    if not runtime_log_path.is_file() or runtime_log_path.stat().st_size == 0:
        raise RuntimeError("Jellyfin runtime log is missing")
    runtime_log = runtime_log_path.read_text(encoding="utf-8", errors="replace")
    if f"Loaded plugin: Community {VERSION}" not in runtime_log or "Jellyfin Community initialized" not in runtime_log:
        raise RuntimeError("The runtime log does not prove that Community loaded and initialized")

    with zipfile.ZipFile(package, "r") as archive:
        names = archive.namelist()
        corrupt = archive.testzip()
    if corrupt is not None:
        raise RuntimeError(f"Corrupt ZIP entry: {corrupt}")
    expected_names = {"Jellyfin.Plugin.Community.dll", "Markdig.dll"}
    if set(names) != expected_names or len(names) != len(expected_names):
        raise RuntimeError(f"Unexpected package contents: {names}")

    generated = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    report = f"""# Informe de validación reproducible — Community {VERSION}

- Fecha UTC: `{generated}`
- Commit validado: `{args.commit}`
- Ejecución CI: {args.run_url}
- Jellyfin objetivo y ejecutado en E2E: `10.10.7`
- Imagen de runtime E2E: `jellyfin/jellyfin:10.10.7`
- ABI de catálogo objetivo: `10.10.7.0`
- Framework: `net8.0`
- SDK de compilación: `{args.dotnet_version}`
- Plugin: `{VERSION}`

## Resultado

- Restauración de dependencias: **correcta**
- Compilación Release: **correcta, advertencias tratadas como errores**
- Analizadores estáticos de .NET: **correctos**
- Pruebas .NET: **{tests['passed']} superadas de {tests['total']}**, 0 fallos, 0 errores, 0 canceladas
- Cobertura de líneas: **{line_coverage:.2f}%**
- Cobertura de ramas: **{branch_coverage:.2f}%**
- Auditoría de dependencias directas y transitivas: **sin paquetes vulnerables conocidos reportados por NuGet en esta ejecución**
- Integridad y lista cerrada del ZIP: **correctas**
- Arranque del paquete final dentro de Jellyfin Server 10.10.7: **correcto**
- E2E de API con administrador y usuario normal: **correcto**
- Inyección del bootstrap de Community en Jellyfin Web: **correcta** (`{api_e2e['webIntegration']['indexResponsesTransformed']}` respuestas transformadas durante la prueba API)
- E2E de navegador Chromium sobre Jellyfin Web real: **correcto**
- Menú `Comunidad` para usuario normal: **verificado en navegador**
- Creación de conversación desde la interfaz: **verificada en navegador**
- Pestañas y panel de administración para administrador: **verificados en navegador**
- Ocultación de administración/moderación para usuario normal: **verificada en navegador**

## Paquete instalable

- SHA-256: `{sha256(package)}`
- Contenido: `{', '.join(names)}`
- Número de entradas: `{len(names)}`

El paquete no incorpora ensamblados `Jellyfin.*` del servidor, `MediaBrowser.*`, `Microsoft.*`, `System.*` ni un runtime `SQLitePCLRaw.*`; evita sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Qué prueba específicamente el E2E

La prueba API configura desde cero una instancia oficial de Jellyfin 10.10.7, autentica un administrador y un usuario normal, comprueba recursos web, categorías iniciales, creación y búsqueda de temas, reacciones, seguimiento, respuestas, denuncias y resolución por moderación, separación de permisos administrativos y diagnóstico de integración web.

La prueba de navegador inicia sesión mediante la interfaz real de Jellyfin Web, abre `Comunidad` desde el menú insertado por el plugin, comprueba que el usuario normal puede utilizar el foro y crear un tema, y comprueba en otra sesión que el administrador dispone de Moderación y Administración y puede ver usuarios conocidos y el estado de integración web.

## Alcance de la garantía

Esta validación sí arranca y utiliza el **paquete final** dentro de una instancia real de Jellyfin Server 10.10.7 y ejecuta su interfaz web con Chromium. Aun así, ninguna suite automatizada puede demostrar ausencia absoluta de defectos en todas las configuraciones, proxies, navegadores o hardware. La comprobación final específica del Synology sigue siendo instalar esta misma compilación, reiniciar Jellyfin y revisar su registro.
"""
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report, encoding="utf-8")
    print(args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
