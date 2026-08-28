# Jellyfin Community

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.7-00A4DC)](https://jellyfin.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml/badge.svg)](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**Community** añade un foro local a Jellyfin. Reutiliza las cuentas, sesiones y permisos del servidor y guarda sus datos en una base SQLite independiente. No crea usuarios paralelos, no almacena contraseñas y no incorpora telemetría.

> **Línea estable 1.x:** Jellyfin Server **10.10.7**, ABI de catálogo **10.10.7.0**, **.NET 8 (`net8.0`)**. La versión estable actual es **1.6.0.0**.

## Instalación desde Jellyfin

En **Panel de control → Plugins → Repositorios → +** añade únicamente el repositorio unificado ODOS3D:

- **Nombre:** `ODOS3D Jellyfin Plugins`
- **URL:** `https://raw.githubusercontent.com/odoslf/Repositorio-plugin-Jelly-fin-odos3d.lab/main/manifest.json`

Guarda el repositorio, abre **Plugins → Catálogo**, selecciona **Community**, instálalo y reinicia Jellyfin cuando se solicite. Tras actualizar el plugin, cierra y vuelve a abrir una vez la aplicación Jellyfin o recarga completamente Jellyfin Web para que el cliente cargue el documento web actualizado.

## Qué cambia en 1.6

La interfaz separa correctamente la configuración administrativa de la experiencia de usuario. Una página declarada mediante `IHasWebPages` es una página de configuración de Jellyfin y no es una sección fiable para usuarios normales, por lo que Community 1.6 utiliza mecanismos distintos para cada función.

- Jellyfin Web recibe una entrada **Foro** mediante `menuLinks`, visible en la navegación normal junto a las bibliotecas.
- **Foro** abre `/Community/app`, una aplicación independiente apta para usuarios normales.
- La app Android basada en WebView conserva la navegación dentro de la propia aplicación mediante el bootstrap de Community.
- Los formularios usan controles HTML nativos y las categorías se renderizan con texto y valor explícitos.
- La sesión se obtiene automáticamente de `jellyfin_credentials`; Community no pide, guarda ni compara IP, dominio o puerto.
- Todas las rutas se calculan a partir del origen y la subruta actuales, incluido el uso detrás de proxy inverso o base URL como `/jellyfin`.
- Administración y moderación aparecen solo para los roles correspondientes. Los ajustes del plugin permanecen en el panel de control.
- Los errores muestran estado HTTP, código y referencia correlacionable con el registro de Jellyfin.
- Un fallo de notificación posterior al guardado no convierte una creación correcta en un error ni provoca duplicados al reintentar.
- El canal nativo **Foro** se publica mediante `IChannel` para clientes que exponen Channels de Jellyfin.
- La integración Web convive con JellyPremiere sin que ambos compitan por modificar el arranque de Jellyfin Web.

La interfaz rica de Foro está validada en **Jellyfin Web 10.10.7** y en el flujo Android basado en WebView. Los clientes nativos que no ejecutan Jellyfin Web no admiten páginas HTML arbitrarias de plugins; en ellos solo puede aparecer la representación estándar que el cliente haga de los objetos nativos de Jellyfin, incluido el canal `Foro` cuando el cliente muestre Channels.

## Funciones

- Identidad y sesión de Jellyfin, sin contraseñas propias.
- Administradores de Jellyfin como superadministradores y moderadores configurables.
- Categorías generales o vinculadas a bibliotecas.
- Conversaciones, reseñas, anuncios y encuestas.
- Markdown seguro, menciones, citas, reacciones y seguimiento.
- Debates vinculados a contenido de Jellyfin mediante GUID.
- Spoilers controlados según el estado visto del elemento asociado.
- Notificaciones, lectura/no leído y búsqueda.
- Denuncias, fijado, cierre, archivo, ocultación, suspensión, silenciamiento y auditoría.
- Adjuntos JPEG, PNG y WebP con validación binaria, límites y cuota global.
- SQLite separado con WAL, migraciones, integridad, optimización, limpieza y copias.
- Administración de categorías, moderadores, usuarios conocidos y mantenimiento.
- Sin telemetría ni servicios externos obligatorios.

## Compatibilidad

| Componente | Versión |
|---|---|
| Jellyfin Server | **10.10.7** |
| ABI de catálogo | **10.10.7.0** |
| Framework | **.NET 8 / net8.0** |
| Plugin | **1.6.0.0** |
| SDK de compilación CI | **8.0.423** |

La línea 1.x permanece fijada a Jellyfin 10.10.7/.NET 8. El soporte para versiones posteriores de Jellyfin que utilicen otro runtime se publicará como una línea separada y no sustituirá silenciosamente este artefacto.

## Validación de publicación

Una compilación verde por sí sola no se considera suficiente. Community 1.6.0.0 se publica únicamente cuando pasan estas puertas:

- sintaxis de recursos web y contrato frontend;
- compilación Release con advertencias tratadas como errores y analizadores de .NET;
- pruebas .NET con cobertura;
- auditoría de dependencias directas y transitivas;
- ZIP limitado a `Jellyfin.Plugin.Community.dll` y `Markdig.dll`;
- arranque del ZIP final en `jellyfin/jellyfin:10.10.7`;
- E2E autenticado con administrador y usuario normal;
- prueba del canal nativo **Foro**;
- comprobación real de `config.json`, `index.html`, `/Community/app` y recursos sin caché obsoleta;
- E2E de Jellyfin Web real en Chromium móvil;
- conservación de datos en la prueba de actualización;
- revisión del registro de runtime para errores emitidos por Community;
- generación de evidencias y del informe de validación de esa ejecución.

El run final de publicación de 1.6.0.0 superó todas esas etapas en Jellyfin 10.10.7 antes de crear la release.

Consulta [VALIDATION.md](docs/VALIDATION.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md) y [VALIDATION-REPORT.md](docs/VALIDATION-REPORT.md).

## Compilar en Windows 10

Los usuarios finales no necesitan compilar. Para desarrollo se requiere SDK **.NET 8.0.423**, Git y Python 3:

```powershell
git clone https://github.com/odoslf/Jellyfin-Community.git
cd Jellyfin-Community
.\scripts\build.ps1
```

El paquete estable se publica como:

```text
Jellyfin.Plugin.Community_1.6.0.0.zip
```

## Estructura

```text
src/            plugin, API, almacenamiento e integración web
tests/          pruebas .NET y E2E real de Jellyfin
scripts/        compilación, empaquetado, manifiestos e informes
docs/           arquitectura, contrato y validación
dist/           artefactos públicos de versiones estables
manifest.json   catálogo consumido por Jellyfin
```

## Desarrollo y seguridad

Antes de contribuir, consulta [CONTRIBUTING.md](CONTRIBUTING.md). Los problemas de seguridad deben seguir [SECURITY.md](SECURITY.md). No publiques un ZIP generado desde una ejecución que no haya superado las puertas de validación de la versión objetivo.

## Licencia

Este proyecto se distribuye bajo **GNU GPL v3**. Consulta [LICENSE](LICENSE).
