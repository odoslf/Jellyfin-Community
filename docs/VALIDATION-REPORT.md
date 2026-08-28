# Informe de validación final — Community 1.6.0.0

- Commit funcional validado: `178ece10ab0f5f7013c046301a0c46015476e226`
- Ejecución CI final: `33120488855`
- Resultado CI: **success**
- Jellyfin objetivo: **10.10.7**
- ABI de catálogo: **10.10.7.0**
- Framework: **net8.0 / .NET 8**
- Plugin: **1.6.0.0**
- Release: `v1.6.0.0`

## Puertas superadas

La ejecución final completó correctamente:

- validación de sintaxis y contrato Web;
- restauración de dependencias;
- compilación Release con análisis estático y warnings como errores;
- pruebas .NET con cobertura;
- auditoría de dependencias;
- publicación y empaquetado;
- arranque del ZIP final en la imagen oficial `jellyfin/jellyfin:10.10.7`;
- E2E real de API con autenticación;
- E2E real del canal nativo **Foro**;
- verificación de menú, aplicación independiente Foro y recursos sin caché obsoleta;
- E2E real de Jellyfin Web mediante Chromium móvil;
- captura y revisión del log de runtime;
- generación y subida de evidencias.

## Paquete publicado

- Archivo: `Jellyfin.Plugin.Community_1.6.0.0.zip`
- MD5 del catálogo: `14B601B1893D3DEB27FE5EEA1E1AD9A2`
- SHA-256 del asset publicado: `35661989bc425b93c680d227b8846c23c185b1a1fc9a2f110c9b5d3c1dc078d1`
- Contenido esperado del plugin: `Jellyfin.Plugin.Community.dll`, `Markdig.dll`

El paquete no incorpora DLL del host Jellyfin ni un runtime .NET alternativo que pueda sustituir dependencias proporcionadas por Jellyfin 10.10.7.

## Validación conjunta del catálogo ODOS3D

Después de publicar Community 1.6.0.0, el repositorio unificado ODOS3D descargó los paquetes finales de Community, JellyPremiere y JellyLiveNow, verificó sus checksums, los instaló juntos en un Jellyfin 10.10.7 oficial y completó con éxito el E2E combinado y la revisión de logs antes de actualizar el catálogo público.

## Alcance

Este informe acredita el comportamiento automatizado validado del paquete final y su convivencia con los otros plugins del catálogo. No supone que un plugin de servidor pueda añadir páginas HTML nativas arbitrarias a clientes que no ejecuten Jellyfin Web. La presentación exacta en cada cliente físico sigue dependiendo de las capacidades del cliente oficial.
