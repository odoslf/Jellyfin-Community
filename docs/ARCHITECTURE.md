# Arquitectura

## Capas

- `Plugin.cs`: metadatos y páginas embebidas.
- `PluginServiceRegistrator.cs`: registro de servicios.
- `Controllers`: API REST autenticada.
- `Services`: reglas de negocio, autorización, Markdown, adjuntos, avisos y copias.
- `Infrastructure`: rutas y SQLite.
- `Tasks`: limpieza, optimización e integridad.
- `Web`: interfaz de usuario integrada.
- `Configuration`: panel administrativo.

## Persistencia

La base usa SQLite con claves foráneas, WAL, espera por bloqueos, transacciones y esquema versionado. No escribe en la base de datos interna de Jellyfin. Los ficheros adjuntos usan nombres aleatorios y el nombre original solo se conserva como metadato.

## Autorización

El controlador obtiene el usuario desde `IAuthorizationContext`; nunca acepta un identificador de usuario enviado por el cliente como identidad efectiva. La visibilidad de elementos asociados se valida con `BaseItem.IsVisible(User)`. Las decisiones administrativas se calculan en el servidor.

## Frontend

La interfaz usa clases y variables visuales de Jellyfin y no sobrescribe estilos globales. Las respuestas Markdown llegan renderizadas por el servidor tras desactivar HTML. Los textos generados en el cliente se escapan antes de insertarse en el DOM.
