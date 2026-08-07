# Informe de validación reproducible

- Fecha UTC: `2026-08-04T23:39:46.887888Z`
- Commit validado: `5dfa17ea44fecdaaaead8357ac2d5a49e4f0d347`
- Ejecución CI: https://github.com/odoslf/X4/actions/runs/30960675838
- Jellyfin objetivo: `10.10.7`
- ABI objetivo: `10.10.0.0`
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

- SHA-256: `890e474e463b96d73915b90e391ae607e6aa5f4d08210f65170e869a4a5b2dcc`
- Contenido permitido: `Jellyfin.Plugin.Community.dll, Markdig.dll`
- Número de entradas: `2`

El paquete no incorpora ensamblados `Jellyfin.*` del servidor, `MediaBrowser.*`, `Microsoft.*`, `System.*` ni un runtime `SQLitePCLRaw.*`; evita sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Alcance de la garantía

Este informe demuestra una compilación reproducible, análisis estático, pruebas automatizadas, auditoría de dependencias e integridad del paquete. No afirma una prueba física de instalación o rendimiento en un Synology concreto; esa comprobación requiere arrancar el artefacto en el servidor final y revisar sus registros.
