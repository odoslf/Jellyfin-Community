# Validación de Community 1.6.0.0

Esta línea está dirigida a **Jellyfin Server 10.10.7 / .NET 8**. La publicación
solo puede usar el mismo ZIP que haya superado todos los controles siguientes.

## Puertas obligatorias de CI

1. Sintaxis de `communityForum15.js`, `communityBootstrap.js` y las pruebas de navegador.
2. Contrato de normalización JSON, base URL bajo `/jellyfin`, selección automática de sesión y controles HTML nativos.
3. Restauración con SDK .NET 8.0.423 y referencias Jellyfin 10.10.7.
4. Compilación Release con analizadores y advertencias tratadas como errores.
5. Pruebas xUnit, incluida actualización de una base 1.4 sin perder temas ni mensajes, y cobertura Cobertura.
6. Auditoría NuGet de dependencias directas y transitivas.
7. ZIP reproducible limitado a `Jellyfin.Plugin.Community.dll` y `Markdig.dll`, integridad y SHA-256.
8. Arranque del ZIP final en la imagen oficial `jellyfin/jellyfin:10.10.7`.
9. E2E de API con administrador y usuario normal: categorías, temas, respuestas, búsqueda, seguimiento, reacciones, denuncias, moderación y separación de permisos.
10. Verificación real de `/web/config.json` con una única entrada **Foro**, `index.html` con bootstrap 1.5 y cabeceras anti-caché.
11. Verificación de `/Community/app`, su CSP y los recursos `communityForum15` servidos por el plugin.
12. E2E Chromium móvil que inicia sesión, abre Foro desde el menú normal en la misma vista, comprueba las categorías nativas, crea un tema y valida el panel administrativo separado.
13. Apertura de `CommunityConfiguration` desde el panel de Jellyfin.
14. Revisión automática del registro para errores emitidos por `Jellyfin.Plugin.Community`.
15. Informe `VALIDATION-REPORT.md` generado a partir de esas mismas evidencias.

## Alcance

Una ejecución verde demuestra el paquete en Jellyfin Server/Web 10.10.7 y reproduce
el flujo de la aplicación Android basada en WebView. No extiende automáticamente la
compatibilidad a clientes nativos que no ejecutan Jellyfin Web, ni cubre cada proxy,
arquitectura o versión de WebView existente.

Antes de actualizar una instalación real se recomienda conservar una copia del
directorio de datos de Jellyfin, reiniciar el servidor tras instalar el DLL y cerrar
y abrir una vez el cliente para descargar el nuevo documento web.
