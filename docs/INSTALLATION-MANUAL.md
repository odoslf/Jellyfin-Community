# Instalación de Jellyfin Community 1.0.0.0

## Compatibilidad exacta

- Jellyfin Server: 10.10.7
- Framework: .NET 8 (`net8.0`)
- ABI: 10.10.0.0
- Paquete: `Jellyfin.Plugin.Community_1.0.0.0_Jellyfin-10.10.7_net8.zip`

## Antes de instalar

1. Haga una copia del directorio de configuración y datos de Jellyfin.
2. Compruebe que el servidor sigue mostrando la versión 10.10.7.
3. Detenga completamente Jellyfin desde Synology antes de copiar archivos.

## Instalación manual

1. Localice el directorio de plugins usado por su instalación de Jellyfin.
2. Cree dentro una carpeta nueva llamada `Community_1.0.0.0`.
3. Extraiga directamente en esa carpeta los dos archivos del ZIP instalable:
   - `Jellyfin.Plugin.Community.dll`
   - `Markdig.dll`
4. Inicie Jellyfin.
5. Abra el registro del servidor y confirme que no aparecen errores de carga, dependencias o migraciones relacionados con `Community`.
6. Entre en **Panel de control → Plugins → Community** y revise los límites, adjuntos, moderadores, copias y retención.
7. Habilite Community, cierre y vuelva a abrir una vez el cliente y abra **Foro** desde el menú principal. No configure ninguna IP para Community.

## Prueba inicial recomendada

Realice primero estas operaciones con una cuenta de prueba:

1. Crear una categoría.
2. Publicar un tema y una respuesta.
3. Probar Markdown, menciones, reacciones y una encuesta.
4. Subir una imagen JPEG, PNG o WebP dentro de los límites configurados.
5. Probar una acción de moderación.
6. Crear una copia de seguridad y verificar que aparece en el panel.
7. Reiniciar Jellyfin y comprobar que los datos continúan disponibles.

## Retirada y recuperación

1. Detenga Jellyfin.
2. Elimine únicamente la carpeta `Community_1.0.0.0` del directorio de plugins.
3. Inicie Jellyfin y revise el registro.
4. Los datos de Community se conservan en el directorio de datos de Jellyfin, dentro de `community`, hasta que usted decida eliminarlos.

## Verificación del archivo

Compare el SHA-256 con el archivo `.sha256` entregado. Una diferencia significa que no debe instalar el paquete.

La validación automatizada demuestra compilación, análisis estático, pruebas, auditoría de dependencias e integridad del ZIP. La comprobación física final requiere arrancarlo en su Synology y revisar el registro del servidor.
