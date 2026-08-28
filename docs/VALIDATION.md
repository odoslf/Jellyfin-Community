# Validación de Community 1.6.0.0

Esta línea está dirigida a **Jellyfin Server 10.10.7 / .NET 8**. La publicación solo puede usar el mismo ZIP que haya superado todos los controles siguientes.

## Puertas obligatorias de CI

1. Sintaxis de los recursos Web y de las pruebas de navegador.
2. Contrato frontend: normalización JSON, base URL bajo `/jellyfin`, selección automática de sesión y controles HTML nativos.
3. Restauración con SDK .NET 8 y referencias Jellyfin 10.10.7.
4. Compilación Release con analizadores y advertencias tratadas como errores.
5. Pruebas xUnit con cobertura, incluida actualización de una base anterior sin perder temas ni mensajes.
6. Auditoría NuGet de dependencias directas y transitivas.
7. ZIP limitado a `Jellyfin.Plugin.Community.dll` y `Markdig.dll`, con comprobación de integridad y hashes.
8. Arranque del ZIP final en la imagen oficial `jellyfin/jellyfin:10.10.7`.
9. E2E de API con administrador y usuario normal: categorías, temas, respuestas, búsqueda, seguimiento, reacciones, denuncias, moderación y separación de permisos.
10. E2E del canal nativo **Foro** mediante la API estándar de Channels de Jellyfin.
11. Verificación real de `/web/config.json`, una única entrada **Foro**, `index.html`, `/Community/app` y recursos sin caché obsoleta.
12. E2E Chromium móvil que inicia sesión, abre Foro desde el menú normal en la misma vista, comprueba las categorías, crea un tema y valida el panel administrativo separado.
13. Apertura de `CommunityConfiguration` desde el panel de Jellyfin.
14. Revisión automática del registro para errores emitidos por `Jellyfin.Plugin.Community`.
15. Generación de evidencias y de un informe de validación ligado a esa ejecución.
16. Antes de entrar en el catálogo unificado ODOS3D, validación conjunta con JellyPremiere y JellyLiveNow dentro de un único Jellyfin 10.10.7.

## Resultado de la versión estable

El commit funcional final `178ece10ab0f5f7013c046301a0c46015476e226` superó el run de CI `33120488855`. Después se publicó Community 1.6.0.0 y el catálogo unificado volvió a descargar el ZIP publicado, comprobar su checksum y arrancarlo junto a JellyPremiere y JellyLiveNow en Jellyfin 10.10.7.

## Alcance

Una ejecución verde demuestra el paquete en Jellyfin Server/Web 10.10.7 y reproduce el flujo de la aplicación Android basada en WebView. También valida la representación estándar `IChannel` que puede consumir un cliente compatible con Channels.

No equivale a prometer que una página HTML arbitraria aparecerá como interfaz nativa en Android TV ni cubre cada proxy, arquitectura o versión de WebView existente. Antes de actualizar una instalación real se recomienda conservar una copia del directorio de datos de Jellyfin y reiniciar el servidor tras instalar el plugin.
