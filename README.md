# Jellyfin Community

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.7-00A4DC)](https://jellyfin.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml/badge.svg)](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**Community** añade un foro local a Jellyfin. Reutiliza las cuentas, sesiones y permisos del servidor, respeta la visibilidad de las bibliotecas y guarda sus datos en una base SQLite independiente. No crea usuarios paralelos, no almacena contraseñas y no incorpora telemetría.

> **Línea estable 1.x:** Jellyfin Server **10.10.7**, ABI de catálogo **10.10.7.0**, **.NET 8 (`net8.0`)**. La versión preparada en esta rama es **1.2.0.0**.

## Instalación desde Jellyfin

En **Panel de control → Plugins → Repositorios → +** añada:

- **Nombre:** `Jellyfin Community`
- **URL:** `https://raw.githubusercontent.com/odoslf/Jellyfin-Community/main/manifest.json`

Guarde el repositorio, abra **Plugins → Catálogo**, seleccione **Community**, instálelo y reinicie Jellyfin cuando se le solicite. Las actualizaciones estables de la línea 1.x se ofrecen desde esa misma URL.

## Qué cambia en 1.2

La versión 1.0 podía instalarse y cargar el HTML de Comunidad, pero Jellyfin Web 10.10.7 no ejecutaba el JavaScript inline de esa página. La integración reconstruida en 1.1 solucionó ese problema y la 1.2 añade la última corrección visual detectada en la validación real: en pantallas móviles la cabecera de Jellyfin podía tapar el título y el botón Volver del foro.

La versión 1.2:

- mantiene el bootstrap versionado del propio plugin que añade **Comunidad** al menú de Jellyfin Web para usuarios autenticados;
- no modifica físicamente archivos del directorio web de Jellyfin;
- no requiere Harmony ni File Transformation;
- separa las funciones de usuario normal, moderador y administrador;
- ofrece al administrador las pestañas de Moderación y Administración dentro de Comunidad;
- reserva correctamente el espacio de la cabecera móvil de Jellyfin Web;
- bloquea CI si Volver, Comunidad, búsqueda o Nuevo tema quedan fuera del viewport o tapados por otro elemento;
- ejecuta el paquete final dentro de Jellyfin 10.10.7 real y prueba la interfaz con Chromium antes de publicarlo.

## Funciones

- Identidad y sesión de Jellyfin, sin contraseñas propias.
- Administradores de Jellyfin como superadministradores y moderadores configurables.
- Categorías generales o vinculadas a bibliotecas.
- Conversaciones, reseñas, anuncios y encuestas.
- Markdown seguro, menciones, citas, reacciones y seguimiento.
- Debates vinculados a películas, series, temporadas o episodios mediante GUID.
- Spoilers controlados según el estado visto del elemento asociado.
- Notificaciones, borradores, lectura/no leído y búsqueda.
- Denuncias, fijado, cierre, archivo, ocultación, suspensión, silenciamiento y auditoría.
- Adjuntos JPEG, PNG y WebP verificados por firma binaria, con límites y cuota global.
- SQLite separado con WAL, migraciones, integridad, optimización, limpieza y copias.
- Administración de categorías, moderadores, usuarios conocidos y mantenimiento.
- Sin telemetría ni servicios externos obligatorios.

## Compatibilidad

| Componente | Versión |
|---|---|
| Jellyfin Server | **10.10.7** |
| ABI de catálogo | **10.10.7.0** |
| Framework | **.NET 8 / net8.0** |
| Plugin | **1.2.0.0** |
| SDK de compilación CI | **8.0.423** |

La línea 1.x permanece deliberadamente fijada a Jellyfin 10.10.7/.NET 8. El soporte para versiones posteriores de Jellyfin que usan otro runtime se publicará como una línea separada; no se reemplazará silenciosamente el artefacto de 10.10.7.

## Validación de publicación

Una compilación verde por sí sola **no** es suficiente. El workflow de publicación bloquea el artefacto si falla cualquiera de estas etapas:

- sintaxis de los recursos web y prohibición de JavaScript inline en la página del foro;
- compilación Release con advertencias tratadas como errores y analizadores de .NET;
- pruebas .NET con cobertura;
- auditoría de dependencias directas y transitivas;
- ZIP reproducible con una lista cerrada de dos archivos;
- arranque del ZIP final dentro de la imagen oficial `jellyfin/jellyfin:10.10.7`;
- E2E real de API con administrador y usuario normal;
- E2E real de Jellyfin Web en Chromium, incluyendo inicio de sesión, menú Comunidad, carga del foro, creación de un tema, controles móviles visibles y panel administrativo;
- comprobación del registro de runtime para errores emitidos por Community;
- generación de un informe de validación a partir de las evidencias de esa misma ejecución.

Consulte [VALIDATION.md](docs/VALIDATION.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md) y el informe de la versión publicada para los detalles.

## Compilar en Windows 10

Requisitos: SDK **.NET 8.0.423**, Git y Python 3.

```powershell
git clone https://github.com/odoslf/Jellyfin-Community.git
cd Jellyfin-Community
.\scripts\build.ps1
```

El paquete se genera en:

```text
artifacts/Jellyfin.Plugin.Community_1.2.0.0.zip
```

Para usuarios finales no es necesario compilar: la versión estable se instala desde el repositorio del catálogo indicado arriba.

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

Antes de contribuir, consulte [CONTRIBUTING.md](CONTRIBUTING.md). Los problemas de seguridad deben seguir [SECURITY.md](SECURITY.md). No publique un ZIP generado desde una ejecución que no haya superado todas las puertas E2E de la versión objetivo.

## Licencia

Este proyecto se distribuye bajo **GNU GPL v3**. Consulte [LICENSE](LICENSE).