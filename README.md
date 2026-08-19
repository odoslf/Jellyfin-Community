# Jellyfin Community

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.10.7-00A4DC)](https://jellyfin.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml/badge.svg)](https://github.com/odoslf/Jellyfin-Community/actions/workflows/build.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**Community** añade un foro local a Jellyfin. Reutiliza las cuentas, sesiones y permisos del servidor y guarda sus datos en una base SQLite independiente. No crea usuarios paralelos, no almacena contraseñas y no incorpora telemetría.

> **Línea estable 1.x:** Jellyfin Server **10.10.7**, ABI de catálogo **10.10.7.0**, **.NET 8 (`net8.0`)**. La versión de esta rama es **1.6.0.0**.

## Instalación desde Jellyfin

En **Panel de control → Plugins → Repositorios → +** añada:

- **Nombre:** `Jellyfin Community`
- **URL:** `https://raw.githubusercontent.com/odoslf/Jellyfin-Community/main/manifest.json`

Guarde el repositorio, abra **Plugins → Catálogo**, seleccione **Community**, instálelo y reinicie Jellyfin cuando se le solicite. Después de actualizar el plugin, cierre y vuelva a abrir una vez la aplicación Jellyfin o recargue completamente Jellyfin Web para que el cliente cargue el nuevo `index.html` y el bootstrap de Community.

## Qué cambia en 1.5

La interfaz se ha reconstruido para corregir el problema de diseño de las versiones anteriores: una página declarada mediante `IHasWebPages` es una página de configuración de Jellyfin y su enumeración requiere privilegios elevados; no es una sección fiable para usuarios normales.

Community 1.5 separa por completo ambos usos:

- Jellyfin Web recibe una entrada **Foro** mediante su opción oficial `menuLinks`, visible en la navegación normal junto a las bibliotecas.
- **Foro** abre `/Community/app`, una aplicación independiente apta para usuarios normales. No carga el controlador heredado ni componentes personalizados `emby-select`.
- La app Android conserva la navegación dentro de su WebView mediante un bootstrap pequeño que cambia el enlace oficial a `target=_self`.
- Los formularios usan controles HTML nativos; las categorías se renderizan con texto y valor explícitos.
- La sesión se obtiene automáticamente de `jellyfin_credentials`, igual que hace la app Android oficial. Community no pide, guarda ni compara una IP, dominio o puerto.
- Todas las rutas se calculan a partir del origen y la subruta actuales. Funciona detrás de proxy inverso, tanto en `/` como en una base URL como `/jellyfin`.
- Administración y moderación aparecen en pestañas separadas solo para los roles correspondientes. Los ajustes del plugin permanecen en el panel de control.
- Los errores muestran estado HTTP, código y referencia correlacionable con el registro de Jellyfin; ya no se reducen al aviso `Error de Community`.
- Un fallo de notificación posterior al guardado ya no convierte una creación correcta en un error ni provoca duplicados al reintentar.

El menú se integra específicamente con **Jellyfin Web 10.10.7** y con la aplicación Android basada en su WebView. Otros clientes nativos que no cargan Jellyfin Web no admiten páginas de plugin arbitrarias y no se declaran compatibles con esta pantalla.

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

La línea 1.x permanece deliberadamente fijada a Jellyfin 10.10.7/.NET 8. El soporte para versiones posteriores de Jellyfin que usen otro runtime se publicará como una línea separada y no sustituirá silenciosamente este artefacto.

## Validación de publicación

Una compilación verde por sí sola no se considera suficiente. La publicación 1.5 queda bloqueada si falla cualquiera de estas etapas:

- sintaxis de los recursos web y política CSP sin JavaScript inline;
- contrato frontend que prueba respuestas JSON PascalCase/camelCase, subrutas, sesión automática y controles HTML nativos;
- compilación Release con advertencias tratadas como errores y analizadores de .NET;
- pruebas .NET con cobertura;
- auditoría de dependencias directas y transitivas;
- ZIP cerrado a `Jellyfin.Plugin.Community.dll` y `Markdig.dll`;
- arranque del ZIP final dentro de la imagen oficial `jellyfin/jellyfin:10.10.7`;
- E2E autenticado de API con administrador y usuario normal;
- petición directa a `config.json` que exige una única entrada oficial **Foro** y petición a `index.html` que exige el bootstrap Android 1.5;
- comprobación de `/Community/app` y sus recursos aislados de versiones anteriores;
- E2E de Jellyfin Web real en Chromium móvil: inicio de sesión, entrada **Foro**, misma WebView, categorías nativas, creación de un tema, permisos y administración separada;
- prueba de actualización que conserva temas y mensajes de una base existente de 1.4;
- comprobación del registro de runtime para errores emitidos por Community;
- informe de validación generado a partir de las evidencias de esa misma ejecución.

Estas pruebas reducen el riesgo y reproducen los fallos concretos comunicados en las versiones anteriores; no equivalen a prometer que un software pueda estar libre de cualquier fallo en todas las configuraciones externas.

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
artifacts/Jellyfin.Plugin.Community_1.6.0.0.zip
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
