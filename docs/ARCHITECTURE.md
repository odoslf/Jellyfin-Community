# Arquitectura

## Capas

- `Plugin.cs`: metadatos y registro de recursos web embebidos.
- `PluginServiceRegistrator.cs`: registro de servicios, estado de integración web y `IStartupFilter`.
- `WebIntegration`: integración no destructiva con Jellyfin Web 10.10.7.
- `Controllers`: API REST autenticada.
- `Services`: reglas de negocio, autorización, Markdown, adjuntos, avisos, moderación y copias.
- `Infrastructure`: rutas y SQLite.
- `Tasks`: limpieza, optimización e integridad.
- `Web`: página del foro, controlador ES module y bootstrap global.
- `Configuration`: configuración del plugin en el panel de Jellyfin.

## Persistencia

La base usa SQLite con claves foráneas, WAL, espera por bloqueos, transacciones y esquema versionado. No escribe en la base de datos interna de Jellyfin. Los ficheros adjuntos usan nombres aleatorios validados y el nombre original solo se conserva como metadato.

## Autorización

El controlador obtiene el usuario desde `IAuthorizationContext`; nunca acepta un identificador de usuario enviado por el cliente como identidad efectiva. La visibilidad de elementos asociados se valida en el servidor con las APIs de biblioteca de Jellyfin. Las decisiones de administrador y moderador también se calculan exclusivamente en el servidor.

## Integración con Jellyfin Web 10.10.7

La versión 1.1 evita el fallo de la versión 1.0, donde Jellyfin mostraba el HTML de la página pero no ejecutaba su JavaScript inline.

La integración funciona en dos niveles:

1. `CommunityStartupFilter` registra `CommunityWebInjectionMiddleware` antes de completar el pipeline ASP.NET de Jellyfin.
2. El middleware solo inspecciona respuestas del documento raíz de Jellyfin Web (`/web`, `/web/`, `/web/index.html` o `/web/index.htm`). Cuando recibe el HTML correcto, inserta una etiqueta `script` versionada que apunta a `ConfigurationPage?name=CommunityBootstrap`. No altera archivos en disco.
3. `communityBootstrap.js` observa los contenedores `.customMenuOptions` de Jellyfin Web y añade una entrada **Comunidad** para usuarios autenticados. La navegación utiliza `Dashboard.getPluginUrl` y `Dashboard.navigate` cuando están disponibles.
4. `community.html` es solo markup y estilos. Declara `data-controller="CommunityPageController"`.
5. Jellyfin Web carga `communityPageController.js` como módulo mediante su propio ciclo de vistas. Toda la inicialización, llamadas API, creación de temas y paneles de moderación/administración viven en ese controlador.

Esta solución no requiere Harmony, no depende de File Transformation y no escribe ni sustituye `index.html`, bundles JavaScript ni otros archivos de Jellyfin Web. Si la inyección falla, el middleware conserva la respuesta original de Jellyfin y registra el diagnóstico en `CommunityWebIntegrationState`.

## Frontend

La interfaz usa clases y variables visuales de Jellyfin y no sobrescribe estilos globales. Las respuestas Markdown llegan renderizadas por el servidor tras desactivar HTML y protocolos peligrosos. Los textos generados en el cliente se escapan antes de insertarse en el DOM.

El usuario normal dispone de Actividad, Siguiendo, Notificaciones y creación/respuesta de temas. Las pestañas Moderación y Administración solo se muestran cuando `/Community/api/v1/me` confirma el rol correspondiente; además, todos los endpoints sensibles vuelven a comprobar permisos en el servidor.

## Validación de integración

El workflow crea el ZIP final, lo instala en una configuración vacía de la imagen oficial `jellyfin/jellyfin:10.10.7` y comprueba tanto la API como Jellyfin Web en Chromium. La prueba del navegador verifica expresamente el menú Comunidad, la ejecución del controlador, la creación de un tema por un usuario normal y las funciones administrativas en otra sesión.
