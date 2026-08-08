# Jellyfin Community

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.7-00A4DC)](https://jellyfin.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml/badge.svg)](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**Community** añade un foro local a Jellyfin. Reutiliza las cuentas, sesiones y permisos del servidor y guarda sus datos en una base SQLite independiente. No crea usuarios paralelos, no almacena contraseñas y no incorpora telemetría.

> **Línea estable 1.x:** Jellyfin Server **10.10.7**, ABI de catálogo **10.10.7.0**, **.NET 8 (`net8.0`)**. La versión de esta rama es **1.3.0.0**.

## Instalación desde Jellyfin

En **Panel de control → Plugins → Repositorios → +** añada:

- **Nombre:** `Jellyfin Community`
- **URL:** `https://raw.githubusercontent.com/odoslf/Jellyfin-Community/main/manifest.json`

Guarde el repositorio, abra **Plugins → Catálogo**, seleccione **Community**, instálelo y reinicie Jellyfin cuando se le solicite. Después de actualizar el plugin, cierre y vuelva a abrir una vez la aplicación Jellyfin o recargue completamente Jellyfin Web para que el cliente cargue el nuevo `index.html` y el bootstrap de Community.

## Qué corrige 1.3

La 1.3 está orientada específicamente a los fallos observados en una instalación real de Jellyfin 10.10.7 con la 1.2:

- Community no aparecía en la navegación normal de algunos clientes y quedaba accesible solo desde el panel de administración.
- Al abrirla desde la página del plugin podían aparecer categorías sin nombre.
- La pantalla podía detenerse con `Cannot read properties of undefined (reading 'length')`.
- Crear una conversación podía terminar en el aviso genérico `Error de Community`.

Las causas eran dos rutas de ejecución distintas que las pruebas anteriores no aislaban correctamente. La inyección posterior a los archivos estáticos podía no transformar la respuesta real de `index.html` cuando el servidor usaba `SendFile`, y el acceso directo desde el panel no instalaba el adaptador que convertía las propiedades JSON de ASP.NET/Jellyfin de PascalCase a camelCase.

La versión 1.3 cambia esas dos piezas:

- sirve una respuesta transformada del `index.html` físico de Jellyfin **antes** del middleware de archivos estáticos, sin modificar el archivo en disco;
- añade `Cache-Control: no-cache, no-store` al documento inicial para evitar conservar un bootstrap antiguo;
- inyecta **Comunidad** en `.customMenuOptions`, el espacio que Jellyfin Web 10.10.7 reserva en el menú lateral de todos los usuarios autenticados;
- mantiene `EnableInMainMenu` solo como acceso alternativo para administradores, porque Jellyfin 10.10.7 protege la enumeración de `ConfigurationPages` con permisos elevados;
- instala la normalización JSON dentro del propio controlador 1.3, por lo que categorías, resultados paginados, permisos, temas y administración se interpretan igual tanto al entrar desde el menú normal como desde el panel;
- conserva el backend y las funciones de foro existentes: temas, respuestas, reacciones, encuestas, notificaciones, moderación y administración.

Community no contiene ni necesita conocer su dominio público. Las llamadas se construyen con `ApiClient.getUrl(...)` sobre el mismo servidor Jellyfin al que el usuario ya está conectado. Un fallo al conectar manualmente a `https://servidor:10000` es una cuestión de acceso/reverse proxy de Jellyfin, no una dirección codificada por Community.

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
| Plugin | **1.3.0.0** |
| SDK de compilación CI | **8.0.423** |

La línea 1.x permanece deliberadamente fijada a Jellyfin 10.10.7/.NET 8. El soporte para versiones posteriores de Jellyfin que usen otro runtime se publicará como una línea separada y no sustituirá silenciosamente este artefacto.

## Validación de publicación

Una compilación verde por sí sola no se considera suficiente. La publicación 1.3 queda bloqueada si falla cualquiera de estas etapas:

- sintaxis de los recursos web y prohibición de JavaScript inline en la página del foro;
- contrato frontend que prueba explícitamente respuestas JSON PascalCase y camelCase, incluido `Items`/`items` y arrays vacíos;
- compilación Release con advertencias tratadas como errores y analizadores de .NET;
- pruebas .NET con cobertura;
- auditoría de dependencias directas y transitivas;
- ZIP cerrado a `Jellyfin.Plugin.Community.dll` y `Markdig.dll`;
- arranque del ZIP final dentro de la imagen oficial `jellyfin/jellyfin:10.10.7`;
- E2E autenticado de API con administrador y usuario normal;
- petición directa al `index.html` servido por Jellyfin que exige encontrar el bootstrap de Community y sus cabeceras anti-caché;
- comprobación de que el servidor entrega el controlador 1.3 con la normalización JSON;
- E2E de Jellyfin Web real en Chromium móvil: inicio de sesión, entrada **Comunidad** en la navegación, carga del foro, creación de un tema, permisos de usuario normal y controles de administrador;
- comprobación del registro de runtime para errores emitidos por Community;
- informe de validación generado a partir de las evidencias de esa misma ejecución.

Estas pruebas reducen el riesgo y reproducen los fallos concretos detectados en 1.2; no equivalen a prometer que un software pueda estar libre de cualquier fallo en todas las configuraciones externas.

Consulte [VALIDATION.md](docs/VALIDATION.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md) y el informe adjunto a cada Release.

## Compilar en Windows 10

Los usuarios finales no necesitan compilar. Para desarrollo se requiere SDK **.NET 8.0.423**, Git y Python 3:

```powershell
git clone https://github.com/odoslf/Jellyfin-Community.git
cd Jellyfin-Community
.\scripts\build.ps1
```

El paquete de esta versión se genera en:

```text
artifacts/Jellyfin.Plugin.Community_1.3.0.0.zip
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

Antes de contribuir, consulte [CONTRIBUTING.md](CONTRIBUTING.md). Los problemas de seguridad deben seguir [SECURITY.md](SECURITY.md). No publique un ZIP generado desde una ejecución que no haya superado las puertas de validación de la versión objetivo.

## Licencia

Este proyecto se distribuye bajo **GNU GPL v3**. Consulte [LICENSE](LICENSE).
