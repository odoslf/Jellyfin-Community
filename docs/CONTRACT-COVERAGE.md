# Cobertura del contrato funcional

Esta tabla evita presentar como terminado aquello que todavía requiere validación específica de Jellyfin Web.

| Área | Estado en el código |
|---|---|
| Instalación como un único plugin | Implementada mediante proyecto, empaquetador, `build.yaml` y generador de manifiesto. Requiere publicar el ZIP en una URL HTTPS. |
| Usuarios y sesión de Jellyfin | Implementada con `IAuthorizationContext`; no existen cuentas ni contraseñas propias. |
| Administrador y moderadores | Implementado. Administradores de Jellyfin son superadministradores; moderadores configurables y persistidos. |
| Categorías generales y por biblioteca | Implementado. La creación automática por cada biblioteca queda bajo control del administrador para no generar categorías no deseadas. |
| Conversaciones por contenido | Implementado mediante `ItemId`, comprobación de visibilidad y filtro de URL. |
| Pestaña inyectada en todas las fichas | No se parchea Jellyfin Web. La página principal funciona y acepta `itemId`; la inyección debe desarrollarse por ABI una vez validada la versión concreta del cliente web. |
| Debates, reseñas, encuestas y anuncios | Implementado. |
| Mensajes, citas, menciones, reacciones y etiquetas | Implementado en backend; la interfaz principal expone publicación, respuesta, reacción y navegación. |
| Markdown seguro | Implementado con HTML desactivado, validación y bloqueo de protocolos peligrosos. |
| Spoilers según estado visto | Implementado para el elemento asociado mediante `IUserDataManager`. La semántica avanzada «hasta episodio» requerirá pruebas con la estructura real de series. |
| Seguimiento, lectura y notificaciones | Implementado. |
| Borradores | Implementado. |
| Búsqueda | Implementada por título y texto con fallback `LIKE`; se crea FTS5 cuando está disponible. |
| Denuncias, bloqueo, fijado, archivo, ocultación y auditoría | Implementado. |
| Suspensión y silenciamiento | Implementado. |
| Adjuntos seguros y cuotas | Implementado para JPEG, PNG y WebP; desactivado por defecto. |
| Limpieza automática | Implementada mediante tareas programadas. |
| SQLite independiente y migraciones | Implementado con esquema versionado, transacciones, WAL e integridad. |
| Copias, exportación y restauración | Implementado; la restauración se aplica al reiniciar. |
| Panel administrativo | Implementado como página de configuración del plugin. |
| Apariencia clara/oscura | La interfaz usa clases y variables de Jellyfin; requiere validación visual con el tema concreto. |
| API REST versionada | Implementada bajo `/Community/api/v1`. |
| Telemetría y nube | No existen. Todo se conserva en el servidor. |
| Clientes no basados en Jellyfin Web | La API queda disponible, pero las interfaces nativas de televisión o móviles requieren integración propia del cliente. |
