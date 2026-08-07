# Validación de la versión 1.0.0.0

Esta versión está dirigida exclusivamente a Jellyfin Server 10.10.7 y .NET 8.

## Controles automatizados obligatorios

El flujo de integración continua debe completar sin excepciones:

1. Reconstrucción reproducible del código fuente y comprobación SHA-256.
2. Búsqueda de objetivos antiguos o incompatibles en código, proyectos, scripts y documentación.
3. Restauración exacta de dependencias con el SDK .NET 8.0.423.
4. Compilación Release de toda la solución con analizadores de .NET y advertencias tratadas como errores.
5. Pruebas xUnit y generación de cobertura Cobertura.
6. Auditoría de vulnerabilidades de todas las dependencias directas y transitivas.
7. Publicación del plugin y empaquetado mediante una lista cerrada de archivos permitidos.
8. Verificación de integridad del ZIP y generación de sumas SHA-256.

El artefacto de CI incluye `VALIDATION-REPORT.md`, los resultados TRX, la cobertura, el inventario del ZIP y la auditoría de dependencias. El paquete no debe distribuirse cuando cualquiera de estos pasos falle.

## Validación operativa restante

La automatización no sustituye una prueba física sobre el servidor final. Antes de habilitarlo para usuarios:

1. Realice una copia del directorio de datos de Jellyfin.
2. Instale el plugin con Jellyfin detenido.
3. Revise el registro de arranque y confirme que no aparecen errores de carga de ensamblados o migraciones.
4. Compruebe creación de categorías, publicación, moderación, adjuntos, búsqueda, copias y restauración en una instalación de ensayo.
5. Verifique la interfaz con el tema y el navegador usados en el Synology.
6. Pruebe la retirada del plugin y la restauración de la copia.
