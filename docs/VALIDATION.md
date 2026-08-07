# Validación de Community 1.1.0.0

Esta línea está dirigida específicamente a **Jellyfin Server 10.10.7 / .NET 8**.

La versión 1.1 cambia la validación de forma deliberada: una compilación correcta ya no es suficiente. El paquete que se vaya a publicar debe arrancar dentro de una instancia oficial de Jellyfin 10.10.7 y superar pruebas reales de API y navegador.

## Puertas obligatorias de CI

La publicación se bloquea si falla cualquiera de estos controles:

1. Comprobación de sintaxis del bootstrap y del controlador JavaScript del frontend.
2. Prohibición de JavaScript inline en `community.html`; la página debe usar el ciclo de controladores de Jellyfin Web.
3. Restauración exacta con SDK .NET 8.0.423 y referencias Jellyfin 10.10.7.
4. Compilación Release con analizadores de .NET y advertencias tratadas como errores.
5. Pruebas xUnit y cobertura Cobertura.
6. Auditoría NuGet de dependencias directas y transitivas.
7. Empaquetado reproducible con lista cerrada: `Jellyfin.Plugin.Community.dll` y `Markdig.dll`.
8. Integridad del ZIP y SHA-256.
9. Arranque del **ZIP final** en `jellyfin/jellyfin:10.10.7`.
10. E2E de API con un administrador y un usuario normal.
11. Verificación de inyección del bootstrap, recursos web y controlador de página.
12. E2E de Jellyfin Web en Chromium: inicio de sesión, menú `Comunidad`, carga del foro, creación de tema y separación de permisos.
13. Revisión automática del registro de runtime para errores emitidos por `Jellyfin.Plugin.Community`.
14. Generación de `VALIDATION-REPORT.md` a partir de las evidencias de esa misma ejecución.

Las pruebas E2E se ejecutan con una configuración Jellyfin nueva para evitar que una instalación previa o una base de datos residual oculte errores de inicialización.

## Qué no significa “validado”

Una ejecución verde demuestra que el artefacto final compila, carga y funciona en la versión oficial de servidor y cliente web probada. No demuestra que sea imposible encontrar un defecto en otra combinación de navegador, proxy inverso, permisos de filesystem, arquitectura de CPU o configuración de Synology.

Por eso, antes de habilitar una actualización en producción, se recomienda conservar una copia del directorio de datos de Jellyfin y revisar el registro del primer arranque en el servidor final.
