# Jellyfin Community

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.7-00A4DC)](https://jellyfin.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml/badge.svg)](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**Community** es un plugin de foro local para Jellyfin. Usa las cuentas existentes del servidor, respeta la visibilidad de las bibliotecas y mantiene sus datos en una base SQLite independiente. No crea un sistema de usuarios paralelo y no incorpora telemetría.

> **Rama estable actual:** Jellyfin **10.10.7**, ABI de catálogo **10.10.7.0**, **.NET 8 (`net8.0`)**, plugin **1.0.0.0**.

## Instalación desde el catálogo de Jellyfin

En **Panel de control → Plugins → Repositorios → +**, añada:

- **Nombre:** `Jellyfin Community`
- **URL:** `https://raw.githubusercontent.com/odoslf/Jellyfin-Community/main/manifest.json`

Guarde el repositorio, abra **Plugins → Catálogo**, busque **Community**, instálelo y reinicie Jellyfin cuando se le solicite.

La URL anterior es la única que necesita el usuario final. El `manifest.json` apunta a un artefacto versionado e incluye el checksum MD5 que Jellyfin utiliza para validar la descarga.

## Compatibilidad

| Componente | Versión |
|---|---|
| Jellyfin Server | **10.10.7** |
| ABI de catálogo | **10.10.7.0** |
| Framework | **.NET 8 / net8.0** |
| Plugin | **1.0.0.0** |
| SDK de compilación validado | **8.0.423** |

Esta línea `1.x` se mantiene deliberadamente enfocada en Jellyfin 10.10.7. Las versiones futuras de Jellyfin con otro runtime se publicarán como artefactos separados dentro de este mismo repositorio y con su propio `targetAbi`; no se sustituirá silenciosamente el paquete de la línea 10.10.7.

## Funciones

- Identidad y sesión de Jellyfin, sin contraseñas propias.
- Administradores de Jellyfin como superadministradores y moderadores configurables.
- Categorías generales o vinculadas a bibliotecas.
- Conversaciones, reseñas, anuncios y encuestas.
- Markdown seguro, menciones, citas, reacciones y seguimiento.
- Debates vinculados a películas, series, temporadas o episodios mediante GUID.
- Spoilers controlados según el estado visto del elemento asociado.
- Notificaciones, borradores, lectura/no leído y búsqueda.
- Denuncias, bloqueo, fijado, archivo, ocultación, suspensión, silenciamiento y auditoría.
- Adjuntos JPEG, PNG y WebP verificados por firma binaria, con límites y cuota global.
- SQLite separado con WAL, migraciones, integridad, optimización, limpieza y copias.
- Panel administrativo y tareas programadas de Jellyfin.
- Sin telemetría ni servicios externos obligatorios.

## Validación de 1.0.0.0

El artefacto publicado fue construido y validado para Jellyfin 10.10.7 con .NET 8. La ejecución reproducible registrada completó:

- compilación Release con advertencias tratadas como errores;
- analizadores estáticos de .NET;
- **20/20 pruebas** superadas;
- auditoría de dependencias directas y transitivas sin vulnerabilidades conocidas reportadas por NuGet en esa ejecución;
- verificación de integridad del ZIP;
- paquete mínimo con solo `Jellyfin.Plugin.Community.dll` y `Markdig.dll`.

SHA-256 del paquete 1.0.0.0:

```text
60608e0e88ef69acbbf1c65ae273ff18a38c6218989f4504dabf2e51f59e5845
```

Consulte [el informe de validación](docs/VALIDATION-REPORT.md) y [la arquitectura](docs/ARCHITECTURE.md) para más detalles. La validación automatizada no sustituye una prueba final en el hardware y la instalación concreta del servidor.

## Compilar en Windows 10

Requisitos: SDK **.NET 8.0.423**, Git y Python 3.

```powershell
git clone https://github.com/odoslf/Jellyfin-Community.git
cd Jellyfin-Community
.\scripts\build.ps1
```

El paquete resultante se genera en:

```text
artifacts/Jellyfin.Plugin.Community_1.0.0.0.zip
```

El proceso restaura dependencias, compila, ejecuta pruebas, audita paquetes, publica y empaqueta el plugin.

## Estructura del repositorio

```text
src/        código del plugin
tests/      pruebas automatizadas
docs/       arquitectura, contrato y validación
scripts/    compilación, empaquetado y manifiestos
dist/       artefacto público validado de la versión estable
manifest.json  catálogo consumido por Jellyfin
```

## Desarrollo y contribuciones

Antes de enviar cambios, lea [CONTRIBUTING.md](CONTRIBUTING.md). Los cambios de seguridad deben seguir [SECURITY.md](SECURITY.md). La compatibilidad con nuevas ramas de Jellyfin se incorporará mediante artefactos separados y CI específico por runtime.

## Licencia

Este proyecto se distribuye bajo **GNU GPL v3**. Consulte [LICENSE](LICENSE).
