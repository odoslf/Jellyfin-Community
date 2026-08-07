# Informe de validación reproducible

- Fecha UTC: `2026-08-07T19:08:51.657274Z`
- Commit validado: `9f552fc7ab82388498bc077347bc5c4ebddfcd11`
- Ejecución CI: https://github.com/odoslf/Jellyfin-Community/actions/runs/31210122703
- Jellyfin objetivo: `10.10.7`
- ABI de catálogo objetivo: `10.10.7.0`
- Framework: `net8.0`
- SDK de compilación: `8.0.423`
- Plugin: `1.0.0.0`

## Resultado

- Restauración de dependencias: **correcta**
- Compilación Release: **correcta, advertencias tratadas como errores**
- Analizadores estáticos de .NET: **correctos**
- Pruebas: **20 superadas de 20**, 0 fallos, 0 errores, 0 canceladas
- Cobertura de líneas: **21.58%**
- Cobertura de ramas: **16.80%**
- Auditoría de dependencias directas y transitivas: **sin paquetes vulnerables conocidos reportados por NuGet**
- Integridad del ZIP: **correcta**

## Paquete instalable

- SHA-256: `56044556a6481130a888f5ef8d9f19d5ac7d16071787e94d7ced6afff9be2ad3`
- Contenido permitido: `Jellyfin.Plugin.Community.dll, Markdig.dll`
- Número de entradas: `2`

El paquete no incorpora ensamblados `Jellyfin.*` del servidor, `MediaBrowser.*`, `Microsoft.*`, `System.*` ni un runtime `SQLitePCLRaw.*`; evita sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Alcance de la garantía

Este informe demuestra una compilación reproducible, análisis estático, pruebas automatizadas, auditoría de dependencias e integridad del paquete. No afirma una prueba física de instalación o rendimiento en un Synology concreto; esa comprobación requiere arrancar el artefacto en el servidor final y revisar sus registros.
