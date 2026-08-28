# Arquitectura

## Capas

- `Plugin.cs`: metadatos y registro de recursos web embebidos.
- `PluginServiceRegistrator.cs`: registro de servicios, `CommunityChannel`, estado de integración Web y `IStartupFilter`.
- `CommunityChannel.cs`: canal estándar **Foro** mediante `IChannel` para clientes que exponen Channels de Jellyfin.
- `WebIntegration`: integración no destructiva con Jellyfin Web 10.10.7 y convivencia con JellyPremiere.
- `Controllers`: API REST autenticada y aplicación independiente `/Community/app`.
- `Services`: reglas de negocio, autorización, Markdown, adjuntos, avisos, moderación y copias.
- `Infrastructure`: rutas y SQLite.
- `Tasks`: limpieza, optimización e integridad.
- `Web`: aplicación independiente del Foro, estilos y bootstrap de navegación Android/WebView.
- `Configuration`: configuración del plugin en el panel de Jellyfin.

## Persistencia

La base usa SQLite con claves foráneas, WAL, espera por bloqueos, transacciones y esquema versionado. No escribe en la base de datos interna de Jellyfin. Los ficheros adjuntos usan nombres aleatorios validados y el nombre original solo se conserva como metadato.

## Autorización

El controlador obtiene el usuario desde `IAuthorizationContext`; nunca acepta un identificador enviado por el cliente como identidad efectiva. La visibilidad de elementos asociados se valida en el servidor con las APIs de biblioteca de Jellyfin. Las decisiones de administrador y moderador también se calculan exclusivamente en el servidor.

## Integración con Jellyfin Web 10.10.7

Las páginas declaradas mediante `IHasWebPages` pertenecen al sistema de configuración y no se usan como entrada del usuario normal.

La integración 1.6 funciona así:

1. `CommunityStartupFilter` registra `CommunityWebInjectionMiddleware` antes del middleware de archivos estáticos.
2. Para `/web/config.json`, conserva la configuración existente y añade exactamente un enlace relativo **Foro** a `menuLinks`.
3. Para `/web/index.html`, inserta un bootstrap pequeño que mantiene la navegación dentro de la misma WebView cuando corresponde.
4. `CommunityAppController` sirve `/Community/app` y sus recursos con `no-cache/no-store` y CSP restrictiva. El HTML puede cargarse sin autenticar, pero todos los datos continúan protegidos por `/Community/api/v1`.
5. La app selecciona en `jellyfin_credentials` la sesión que coincide con el origen/subruta actuales y envía su token mediante cabeceras Jellyfin. Nunca introduce el token en la URL ni solicita una dirección al usuario.
6. Si JellyPremiere está instalado, ambos mecanismos de arranque Web se componen sin reemplazarse entre sí.

El middleware no modifica archivos en disco. Los enlaces son relativos y conservan automáticamente una base URL de proxy como `/jellyfin`. Si una transformación falla, se sirve el recurso original y el diagnóstico queda en `CommunityWebIntegrationState`.

## Canal nativo Foro

`CommunityChannel` se registra como `IChannel`. Esto proporciona una representación estándar del Foro en la API de Channels de Jellyfin para clientes que la consuman. No intenta convertir `/Community/app` en una vista Android TV nativa: un plugin de servidor no puede inyectar arbitrariamente páginas HTML en clientes nativos que no ejecuten Jellyfin Web.

## Frontend

La interfaz es un documento aislado y adaptable, sin depender del ciclo de vistas ni de custom elements internos de Jellyfin Web. Los formularios dinámicos usan controles HTML nativos. Las respuestas Markdown llegan renderizadas por el servidor tras desactivar HTML y protocolos peligrosos. Los textos generados en el cliente se escapan antes de insertarse en el DOM.

El usuario normal dispone de Actividad, Siguiendo, Notificaciones y creación/respuesta de temas. Las pestañas Moderación y Administración solo se muestran cuando `/Community/api/v1/me` confirma el rol correspondiente; todos los endpoints sensibles vuelven a comprobar permisos en el servidor.

## Validación de integración

El workflow crea el ZIP final, lo instala en una configuración vacía de la imagen oficial `jellyfin/jellyfin:10.10.7` y comprueba API, canal nativo Foro, `menuLinks`, bootstrap, aplicación independiente y Jellyfin Web en Chromium móvil. También verifica actualización de datos, permisos y ausencia de errores Community en runtime.

Después de publicar la release, el repositorio unificado ODOS3D descarga los ZIP finales de Community, JellyPremiere y JellyLiveNow, valida checksums y los arranca juntos en Jellyfin 10.10.7 antes de aceptar un cambio del catálogo público.
