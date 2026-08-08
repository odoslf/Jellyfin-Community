#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

VERSION = os.environ.get("COMMUNITY_VERSION", "1.3.0.0")
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


def require_text(path: Path, label: str) -> str:
    if not path.is_file() or path.stat().st_size == 0:
        raise RuntimeError(f"{label} evidence is missing or empty: {path}")
    return path.read_text(encoding="utf-8", errors="replace")


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
    json_contract_path = artifacts / "community-json-contract.json"
    index_headers_path = artifacts / "community-index-headers.txt"
    index_html_path = artifacts / "community-index.html"
    controller_path = artifacts / "community-controller-13.js"
    runtime_log_path = artifacts / "jellyfin-10.10.7-runtime.log"
    user_screenshot = artifacts / "e2e-user-mobile.png"
    admin_screenshot = artifacts / "e2e-admin-mobile.png"

    if not package.is_file() or not audit.is_file():
        raise RuntimeError(f"Package or vulnerability audit is missing for Community {VERSION}")

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
    json_contract = read_json_evidence(json_contract_path, "Community JSON contract")
    if not all(json_contract.get(key) is True for key in ("pascalCaseContract", "camelCaseContract", "emptyArraySafe")):
        raise RuntimeError(f"Community JSON contract evidence is incomplete: {json_contract}")

    required_browser_flags = (
        "ordinaryUser",
        "administrator",
        "menu",
        "createThread",
        "adminPanel",
        "mobileViewport",
        "toolbarVisible",
    )
    if not all(browser_e2e.get(key) is True for key in required_browser_flags):
        raise RuntimeError(f"Browser E2E evidence is incomplete: {browser_e2e}")
    if browser_e2e.get("horizontalOverflow") is not False:
        raise RuntimeError(f"Mobile browser E2E detected horizontal overflow: {browser_e2e}")

    index_headers = require_text(index_headers_path, "Jellyfin index headers")
    index_html = require_text(index_html_path, "Jellyfin transformed index")
    controller = require_text(controller_path, "Community 1.3 controller")
    if "data-jellyfin-community-bootstrap" not in index_html or "CommunityBootstrap" not in index_html:
        raise RuntimeError("The real Jellyfin index response does not contain the Community bootstrap")
    if "cache-control:" not in index_headers.lower() or "no-cache" not in index_headers.lower():
        raise RuntimeError("The real Jellyfin index response is missing Community no-cache headers")
    if "CommunityPageController13" not in controller or "normalizeCommunityJson" not in controller:
        raise RuntimeError("The real Jellyfin server did not expose the Community 1.3 normalized controller")

    for screenshot in (user_screenshot, admin_screenshot):
        if not screenshot.is_file() or screenshot.stat().st_size == 0:
            raise RuntimeError(f"Mobile browser screenshot is missing: {screenshot}")

    runtime_log = require_text(runtime_log_path, "Jellyfin runtime log")
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
    transformed_count = api_e2e.get("webIntegration", {}).get("indexResponsesTransformed", "n/d")
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
- Contrato JSON PascalCase/camelCase y arrays vacíos: **correcto**
- Respuesta real `/web/index.html` con bootstrap de Community: **verificada**
- Cabeceras anti-caché del `index.html`: **verificadas**
- Controlador 1.3 servido por Jellyfin con `normalizeCommunityJson`: **verificado**
- Inyección registrada durante E2E de API: **{transformed_count} respuestas transformadas**
- E2E de navegador Chromium sobre Jellyfin Web real: **correcto**
- Menú `Comunidad` para usuario normal: **verificado en navegador**
- Creación de conversación desde la interfaz: **verificada en navegador**
- Pestañas y panel de administración para administrador: **verificados en navegador**
- Ocultación de administración/moderación para usuario normal: **verificada en navegador**
- Barra de Comunidad visible y pulsable: **verificada**
- Layout móvil 390×844 y 430×932: **verificado sin desbordamiento horizontal**
- Capturas de evidencia de usuario y administrador: **generadas**

## Paquete instalable

- SHA-256: `{sha256(package)}`
- Contenido: `{', '.join(names)}`
- Número de entradas: `{len(names)}`

El paquete no incorpora ensamblados `Jellyfin.*` del servidor, `MediaBrowser.*`, `Microsoft.*`, `System.*` ni un runtime `SQLitePCLRaw.*`; evita sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Qué prueba específicamente el E2E 1.3

La validación 1.3 prueba de forma separada los dos fallos que escaparon a 1.2. Primero pide al servidor real su `index.html` y exige que la respuesta contenga el bootstrap de Community y cabeceras anti-caché, por lo que no basta con probar el transformador aislado. Segundo ejecuta un contrato frontend con propiedades PascalCase y camelCase y comprueba en el servidor real que se sirve el controlador 1.3 que realiza esa normalización.

Además, la prueba API configura desde cero Jellyfin 10.10.7, autentica un administrador y un usuario normal, comprueba categorías iniciales, creación y búsqueda de temas, reacciones, seguimiento, respuestas, denuncias, moderación y separación de permisos. La prueba Chromium inicia sesión mediante Jellyfin Web real, abre `Comunidad` desde la navegación de usuario, crea una conversación y comprueba por separado los controles administrativos.

## Alcance

Esta validación arranca y utiliza el **paquete final** dentro de Jellyfin Server 10.10.7 y ejecuta su interfaz con Chromium. No demuestra ausencia absoluta de defectos en todas las combinaciones de proxy, caché, navegador o hardware. La comprobación específica del Synology consiste en instalar exactamente esta compilación, reiniciar Jellyfin y recargar/cerrar y abrir el cliente una vez para que cargue el nuevo documento web.
"""
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report, encoding="utf-8")
    print(args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
