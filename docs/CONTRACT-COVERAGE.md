# Cobertura del contrato funcional

Estado de la versión estable **Community 1.6.0.0** para Jellyfin 10.10.7.

| Área | Estado |
|---|---|
| Instalación como plugin | **Validado**: paquete publicado, manifest 10.10.7.0 y catálogo unificado ODOS3D. |
| Usuarios y sesión de Jellyfin | **Implementado y validado** con `IAuthorizationContext`; no existen cuentas ni contraseñas propias. |
| Administrador y moderadores | **Implementado y validado**. Administradores de Jellyfin son superadministradores; moderadores configurables y persistidos. |
| Categorías generales y por biblioteca | **Implementado y validado**. |
| Conversaciones por contenido | **Implementado** mediante `ItemId`, comprobación de visibilidad y filtros del servidor. |
| Entrada principal Foro | **Validada** en Jellyfin Web mediante `menuLinks` y `/Community/app`. |
| Canal nativo Foro | **Validado** mediante `IChannel` y API estándar de Channels de Jellyfin. La presentación depende del cliente. |
| Debates, reseñas, encuestas y anuncios | **Implementado**. |
| Mensajes, citas, menciones, reacciones y etiquetas | **Implementado**. |
| Markdown seguro | **Implementado** con HTML desactivado, validación y bloqueo de protocolos peligrosos. |
| Spoilers según estado visto | **Implementado** para el elemento asociado mediante `IUserDataManager`. |
| Seguimiento, lectura y notificaciones | **Implementado**. |
| Borradores | **Implementado**. |
| Búsqueda | **Implementada** por título y texto, con FTS5 cuando está disponible. |
| Denuncias, bloqueo, fijado, archivo, ocultación y auditoría | **Implementado**. |
| Suspensión y silenciamiento | **Implementado**. |
| Adjuntos seguros y cuotas | **Implementado** para JPEG, PNG y WebP con validación binaria y límites. |
| Limpieza automática | **Implementada** mediante tareas programadas. |
| SQLite independiente y migraciones | **Implementado y probado** con esquema versionado, transacciones, WAL e integridad. |
| Actualización sin pérdida de datos | **Validada** con una base de versión anterior en CI. |
| Copias, exportación y restauración | **Implementado**; la restauración se aplica al reiniciar. |
| Panel administrativo | **Implementado** como página de configuración del plugin. |
| API REST versionada | **Implementada y validada** bajo `/Community/api/v1`. |
| Telemetría y nube | **No existen**. Todo se conserva en el servidor. |
| Convivencia con JellyPremiere | **Validada**: ambos bootstraps Web se componen sin sobrescribirse. |
| Catálogo conjunto con JellyPremiere y JellyLiveNow | **Validado**: los tres paquetes se cargan juntos en Jellyfin 10.10.7 antes de publicar cambios del catálogo. |
| Android basado en WebView | **Validado** mediante flujo Web/Chromium equivalente. |
| Android TV / clientes nativos | El plugin expone objetos estándar de Jellyfin (`IChannel`), pero no puede inyectar páginas HTML nativas arbitrarias. La visibilidad exacta depende del cliente oficial. |

La release 1.6.0.0 no se publica si fallan compilación, tests, auditoría de dependencias, empaquetado, arranque real, E2E de API, E2E Web, canal nativo o revisión de logs.
