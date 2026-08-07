# Changelog

## 1.1.0.0 — 2026-08-07

- Corregida la integración web de la versión 1.0: `community.html` deja de depender de JavaScript inline y utiliza el ciclo `data-controller` que Jellyfin Web carga oficialmente para páginas de plugins.
- Añadido un bootstrap versionado que se sirve como recurso embebido del plugin y añade **Comunidad** al menú de Jellyfin Web para usuarios autenticados normales y administradores.
- La integración del menú no modifica archivos del directorio web de Jellyfin y no requiere Harmony ni File Transformation.
- Añadido diagnóstico administrativo de la integración web: índices detectados, respuestas transformadas, última inyección y último error.
- Mejorado el panel de administración con usuarios conocidos, alta de categorías, moderadores, silenciamiento, limpieza, integridad y descarga de copias.
- Corregida la acción de ocultar publicaciones para que actúe sobre el mensaje y no sobre toda la conversación.
- Corregida la edición de publicaciones para utilizar el Markdown original y conservar metadatos de spoiler.
- Corregida la descarga de copias desde la interfaz administrativa mediante una petición autenticada.
- Endurecido el frontend para separar visualmente y por API las funciones de usuario, moderador y administrador.
- Añadidas pruebas unitarias del transformador de `index.html` y comprobaciones estáticas de los recursos JavaScript.
- Añadido E2E real sobre el paquete final dentro de `jellyfin/jellyfin:10.10.7`: inicialización limpia, autenticación de administrador y usuario normal, categorías, temas, búsqueda, seguimiento, reacciones, respuestas, denuncias, resolución por moderación y permisos.
- Añadido E2E de navegador Chromium sobre Jellyfin Web real: inicio de sesión, menú **Comunidad**, carga del foro, creación de tema, ocultación de controles administrativos al usuario normal y panel administrativo para el administrador.
- Las fallas E2E pasan a ser fatales para CI; se eliminó la posibilidad de ocultar errores por tuberías `tee` sin `pipefail`.
- El paquete sigue dirigido exclusivamente a Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.0.0.0 — 2026-08-05

- Primera versión para Jellyfin Server 10.10.7 y .NET 8.
- Referencias oficiales Jellyfin 10.10.7 y ABI objetivo 10.10.7.0.
- Backend REST autenticado, categorías, conversaciones, mensajes, encuestas, notificaciones, moderación y SQLite independiente.
- Adjuntos JPEG, PNG y WebP con validación binaria, límites y cuota global.
- Copias consistentes y restauración validada.
- Compilación Release con analizadores, pruebas y auditoría de dependencias.

> Nota histórica: la integración web de 1.0 mostraba la estructura HTML de Comunidad, pero su JavaScript inline no era ejecutado por el ciclo de vistas de Jellyfin Web 10.10.7. La versión 1.1 sustituye ese diseño y añade pruebas de navegador que reproducen precisamente ese fallo antes de permitir una publicación.
