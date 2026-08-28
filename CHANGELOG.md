# Changelog

## 1.6.0.0 — 2026-08-27

- Consolidada la versión final para Jellyfin Server 10.10.7 / .NET 8 y ABI 10.10.7.0.
- Añadido y validado el canal nativo **Foro** mediante `IChannel`, visible para clientes que exponen Channels de Jellyfin.
- Validado el acceso de administrador y usuario normal al Foro mediante API real de Jellyfin y navegador Chromium móvil.
- Reforzada la convivencia con **JellyPremiere**: ambos plugins componen su integración Web sin sobrescribirse.
- Mantenida la aplicación independiente `/Community/app`, el enlace oficial **Foro** y el flujo Android basado en WebView.
- Publicación bloqueada por compilación con warnings como errores, tests con cobertura, auditoría de dependencias, paquete final, arranque en `jellyfin/jellyfin:10.10.7`, E2E de API, canal nativo, navegador y revisión de logs.
- La release final se valida además junto a JellyPremiere y JellyLiveNow en el catálogo unificado ODOS3D antes de actualizar el manifest público.
- Catálogo recomendado único: `https://raw.githubusercontent.com/odoslf/Repositorio-plugin-Jelly-fin-odos3d.lab/main/manifest.json`.

## 1.5.0.0 — 2026-08-12

- Sustituida la interfaz heredada por una aplicación de Foro independiente en `/Community/app`; no reutiliza controladores 1.3/1.4 ni páginas de configuración como interfaz de usuario.
- Añadida una entrada **Foro** al `menuLinks` oficial de Jellyfin Web, disponible para usuarios normales junto a la navegación de bibliotecas.
- Añadido bootstrap 1.5 aislado de caché que mantiene el enlace dentro del WebView de Android (`target=_self`) y aporta una entrada de reserva sin duplicarla.
- Eliminada toda configuración manual de IP, dominio o puerto: la app selecciona automáticamente la sesión guardada por Jellyfin para el origen y la subruta actuales.
- Sustituidos `emby-select` y otros elementos personalizados en formularios dinámicos por controles HTML nativos, corrigiendo las categorías vacías observadas en Android.
- Separadas las vistas de usuario, moderación, administración y ajustes del plugin; un usuario normal no depende del panel de control.
- Los errores del API incluyen código, estado y referencia de solicitud; los errores 500 se correlacionan con el registro sin exponer detalles internos.
- Un fallo al enviar menciones después de confirmar una transacción ya no devuelve error sobre un tema o respuesta que sí se guardó.
- Añadidas pruebas del `config.json` transformado, `/Community/app`, sesión automática, selector nativo, mismo WebView, creación de temas, panel administrativo y conservación de datos desde 1.4.
- Se mantienen Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.4.0.0 — 2026-08-12

- Confirmado y reforzado el uso automático del servidor Jellyfin activo: Community no configura, pide ni persiste IP, dominio o puerto propios.
- Corregido el versionado de recursos para no asumir que la URL devuelta por Jellyfin ya contiene parámetros de consulta.
- Añadida validación de URLs absolutas y relativas cuando Jellyfin está publicado por proxy inverso bajo una subruta como `/jellyfin`.
- Conservadas las sesiones y cabeceras de autenticación de Jellyfin tanto desde el dominio público como desde el acceso LAN.
- Se mantienen Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.3.0.0 — 2026-08-08

- Corregida la ausencia de **Comunidad** en la navegación normal observada en instalaciones reales de Jellyfin 10.10.7: la respuesta de `index.html` se transforma y sirve antes del middleware de archivos estáticos, evitando que rutas `SendFile` o cachés de cliente dejen fuera el bootstrap.
- Añadidas cabeceras `no-cache/no-store` al documento inicial y versionado 1.3 de los recursos web para evitar reutilizar JavaScript antiguo después de actualizar el plugin.
- Confirmado el punto de integración de Jellyfin Web 10.10.7 en `.customMenuOptions`, disponible para usuarios autenticados normales y administradores.
- `EnableInMainMenu` queda activado como acceso alternativo para administradores; no se usa como solución para usuarios normales porque Jellyfin 10.10.7 protege `ConfigurationPages` con permisos elevados.
- Añadido un controlador 1.3 que normaliza recursivamente respuestas JSON PascalCase y camelCase antes de ejecutar la interfaz existente. Corrige categorías sin nombre, `Cannot read properties of undefined (reading 'length')`, permisos mal interpretados y creación de temas con identificadores de categoría vacíos al entrar desde el panel.
- Mejorado el tratamiento de errores HTTP y de red y eliminada la interpolación de mensajes de error sin escapar en el banner de arranque.
- Añadida prueba de contrato frontend para `Items/items`, `Page/page`, categorías anidadas y arrays vacíos.
- Añadida comprobación CI directa contra el `index.html` servido por un contenedor oficial `jellyfin/jellyfin:10.10.7`, exigiendo el marcador de bootstrap y cabeceras anti-caché.
- Añadida comprobación CI de que el servidor real entrega el controlador 1.3 con el normalizador JSON.
- Se mantienen E2E de API y navegador Chromium móvil para usuario normal y administrador, incluida creación de conversaciones, navegación normal, permisos y administración.
- El plugin sigue fijado a Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.2.0.0 — 2026-08-08

- Consolidada como versión publicable la integración web reconstruida y validada contra `jellyfin/jellyfin:10.10.7` real.
- Corregido el solapamiento móvil que podía dejar el título **Comunidad** y el botón **Volver** debajo de la cabecera de Jellyfin Web.
- Añadida una comprobación E2E específica que exige que Volver, Comunidad, búsqueda y Nuevo tema estén dentro del viewport y no estén tapados por ningún elemento de Jellyfin.
- La prueba de navegador sigue validando por separado un usuario normal y un administrador, incluida la visibilidad exclusiva de Moderación y Administración.
- El artefacto de publicación continúa fijado a Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.1.0.0 — 2026-08-07

- Corregida la integración web de la versión 1.0: `community.html` deja de depender de JavaScript inline y utiliza el ciclo `data-controller` de Jellyfin Web para páginas de plugins.
- Añadido un bootstrap versionado que se sirve como recurso embebido del plugin y añade **Comunidad** al menú de Jellyfin Web para usuarios autenticados normales y administradores.
- La integración del menú no modifica físicamente archivos del directorio web de Jellyfin y no requiere Harmony ni File Transformation.
- Añadido diagnóstico administrativo de la integración web: índices detectados, respuestas transformadas, última inyección y último error.
- Mejorado el panel de administración con usuarios conocidos, alta de categorías, moderadores, silenciamiento, limpieza, integridad y descarga de copias.
- Corregida la acción de ocultar publicaciones para que actúe sobre el mensaje y no sobre toda la conversación.
- Corregida la edición de publicaciones para utilizar el Markdown original y conservar metadatos de spoiler.
- Corregida la descarga de copias desde la interfaz administrativa mediante una petición autenticada.
- Endurecido el frontend para separar visualmente y por API las funciones de usuario, moderador y administrador.
- Añadidas pruebas unitarias del transformador de `index.html` y comprobaciones estáticas de los recursos JavaScript.
- Añadido E2E real sobre el paquete final dentro de `jellyfin/jellyfin:10.10.7`: inicialización limpia, autenticación de administrador y usuario normal, categorías, temas, búsqueda, seguimiento, reacciones, respuestas, denuncias, resolución por moderación y permisos.
- Añadido E2E de navegador Chromium sobre Jellyfin Web real: inicio de sesión, menú **Comunidad**, carga del foro, creación de tema, ocultación de controles administrativos al usuario normal y panel administrativo para el administrador.
- Las fallas E2E pasan a ser fatales para CI.
- El paquete sigue dirigido exclusivamente a Jellyfin Server 10.10.7, ABI 10.10.7.0 y .NET 8.

## 1.0.0.0 — 2026-08-05

- Primera versión para Jellyfin Server 10.10.7 y .NET 8.
- Referencias oficiales Jellyfin 10.10.7 y ABI objetivo 10.10.7.0.
- Backend REST autenticado, categorías, conversaciones, mensajes, encuestas, notificaciones, moderación y SQLite independiente.
- Adjuntos JPEG, PNG y WebP con validación binaria, límites y cuota global.
- Copias consistentes y restauración validada.
- Compilación Release con analizadores, pruebas y auditoría de dependencias.

> Nota histórica: 1.0 falló por JavaScript inline; 1.2 descubrió dos diferencias que la validación previa no aislaba: la respuesta real de `index.html` podía no pasar por el transformador posterior a archivos estáticos y el acceso directo desde el panel podía ejecutar el controlador sin el adaptador JSON del bootstrap. La 1.3 añade pruebas específicas para ambas rutas antes de permitir su publicación.
