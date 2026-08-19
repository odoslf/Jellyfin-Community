# Arquitectura

## Capas

- `Plugin.cs`: metadatos y registro de recursos web embebidos.
- `PluginServiceRegistrator.cs`: registro de servicios, estado de integración web, `IChannel` y `IStartupFilter`.
- `CommunityChannel.cs`: implementación nativa de `IChannel` para exponer el Foro como una sección/canal en cualquier cliente de Jellyfin (Android, Android TV, Web, etc.).
- `WebIntegration`: integración no destructiva con Jellyfin Web 10.10.7.
- `Controllers`: API REST autenticada.
- `Services`: reglas de negocio, autorización, Markdown, adjuntos, avisos, moderación y copias.
- `Infrastructure`: rutas y SQLite.
- `Tasks`: limpieza, optimización e integridad.
- `Web`: aplicación independiente del Foro, estilos y bootstrap de navegación Android.
- `Configuration`: configuración del plugin en el panel de Jellyfin.

## Persistencia

La base usa SQLite con claves foráneas, WAL, espera por bloqueos, transacciones y esquema versionado. No escribe en la base de datos interna de Jellyfin. Los ficheros adjuntos usan nombres aleatorios validados y el nombre original solo se conserva como metadato.

## Autorización

El controlador obtiene el usuario desde `IAuthorizationContext`; nunca acepta un identificador de usuario enviado por el cliente como identidad efectiva. La visibilidad de elementos asociados se valida en el servidor con las APIs de biblioteca de Jellyfin. Las decisiones de administrador y moderador también se calculan exclusivamente en el servidor.

## Integración con Jellyfin Web 10.10.7

Las páginas declaradas por un plugin mediante `IHasWebPages` pertenecen al sistema de configuración. Jellyfin 10.10.7 protege la enumeración de esas páginas con elevación, por lo que no se usan como punto de entrada del usuario normal.

La integración 1.6 funciona así:

1. `CommunityStartupFilter` registra `CommunityWebInjectionMiddleware` antes del middleware de archivos estáticos.
2. Para `/web/config.json`, el middleware conserva la configuración existente y añade exactamente un enlace relativo `{ name: "Foro", icon: "forum", url: "../Community/app?..." }` a `menuLinks`, el mecanismo público documentado de Jellyfin Web.
3. Para `/web/index.html`, inserta un bootstrap pequeño. Este cambia el enlace oficial a `target=_self` para que la app Android no intente abrir otra ventana y crea una entrada de reserva solo si Jellyfin todavía no ha renderizado la oficial.
4. `CommunityAppController` sirve `/Community/app` y sus recursos con `no-cache/no-store`, CSP restrictiva. El HTML puede cargarse sin autenticar, pero todos los datos continúan protegidos por `/Community/api/v1`.
5. La app selecciona en `jellyfin_credentials` la sesión que coincide con el origen/subruta actuales y envía su token mediante cabeceras Jellyfin. Nunca introduce el token en la URL ni solicita una dirección al usuario.

El middleware no modifica archivos en disco. Los enlaces son relativos y conservan automáticamente una base URL de proxy como `/jellyfin`. Si la transformación falla, se sirve el recurso original y el diagnóstico queda en `CommunityWebIntegrationState`.

## Frontend

La interfaz es un documento aislado y adaptable, sin depender del ciclo de vistas ni de custom elements de Jellyfin Web. Los formularios dinámicos usan `input`, `select`, `textarea` y `button` nativos. Las respuestas Markdown llegan renderizadas por el servidor tras desactivar HTML y protocolos peligrosos. Los textos generados en el cliente se escapan antes de insertarse en el DOM.

El usuario normal dispone de Actividad, Siguiendo, Notificaciones y creación/respuesta de temas. Las pestañas Moderación y Administración solo se muestran cuando `/Community/api/v1/me` confirma el rol correspondiente; además, todos los endpoints sensibles vuelven a comprobar permisos en el servidor.

## Validación de integración

El workflow crea el ZIP final, lo instala en una configuración vacía de la imagen oficial `jellyfin/jellyfin:10.10.7` y comprueba API, `menuLinks`, bootstrap, aplicación independiente y Jellyfin Web en Chromium móvil. La prueba verifica el menú Foro, navegación en la misma WebView, detección de sesión, categorías con opciones nativas, creación por usuario normal, separación administrativa y conservación de una base existente de 1.4.
