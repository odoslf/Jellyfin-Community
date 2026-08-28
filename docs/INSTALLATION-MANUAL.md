# Instalación de Jellyfin Community 1.6.0.0

## Compatibilidad exacta

- Jellyfin Server: **10.10.7**
- Framework: **.NET 8 (`net8.0`)**
- ABI de catálogo: **10.10.7.0**
- Paquete: `Jellyfin.Plugin.Community_1.6.0.0.zip`
- MD5 publicado: `14B601B1893D3DEB27FE5EEA1E1AD9A2`
- SHA-256 del ZIP publicado: `35661989bc425b93c680d227b8846c23c185b1a1fc9a2f110c9b5d3c1dc078d1`

## Instalación recomendada por repositorio

En **Panel de control → Plugins → Repositorios → +** añade únicamente:

- **Nombre:** `ODOS3D Jellyfin Plugins`
- **URL:** `https://raw.githubusercontent.com/odoslf/Repositorio-plugin-Jelly-fin-odos3d.lab/main/manifest.json`

Guarda, abre **Plugins → Catálogo**, instala **Community 1.6.0.0** y reinicia Jellyfin. Tras una actualización, cierra y vuelve a abrir el cliente una vez o recarga completamente Jellyfin Web.

## Instalación manual

1. Haz una copia del directorio de configuración y datos de Jellyfin.
2. Comprueba que el servidor sea Jellyfin 10.10.7.
3. Detén Jellyfin antes de sustituir archivos del plugin.
4. Crea una carpeta de plugin, por ejemplo `Community_1.6.0.0`.
5. Extrae directamente los dos archivos del ZIP instalable:
   - `Jellyfin.Plugin.Community.dll`
   - `Markdig.dll`
6. Inicia Jellyfin y confirma en el registro que aparece cargado Community 1.6.0.0 sin errores.
7. En **Panel de control → Plugins → Community**, revisa límites, adjuntos, moderadores, copias y retención.
8. Abre **Foro** con una cuenta de usuario normal y comprueba también la administración con una cuenta administradora.

No copies ensamblados `Jellyfin.*`, `MediaBrowser.*`, `Microsoft.*` o `System.*` junto al plugin.

## Prueba inicial recomendada

1. Abrir Foro con un usuario normal.
2. Crear un tema y una respuesta.
3. Probar Markdown, menciones, reacciones y una encuesta.
4. Subir una imagen JPEG, PNG o WebP dentro de los límites configurados.
5. Probar una acción de moderación con el rol adecuado.
6. Reiniciar Jellyfin y comprobar que temas y mensajes continúan disponibles.
7. Comprobar que JellyPremiere puede estar instalado simultáneamente sin perder la entrada Foro ni su propia integración Web.

## Retirada y recuperación

1. Detén Jellyfin.
2. Elimina únicamente la carpeta del plugin Community del directorio de plugins.
3. Inicia Jellyfin y revisa el registro.
4. Los datos de Community se conservan en su directorio de datos hasta que decidas eliminarlos manualmente.

## Verificación del archivo

El catálogo unificado valida automáticamente que el ZIP publicado sea real y que su MD5 coincida con el manifest. Para una instalación manual puedes comparar también el SHA-256 indicado arriba con el del archivo descargado.

La release 1.6.0.0 fue validada mediante compilación, análisis estático, pruebas, auditoría de dependencias, arranque del paquete final en Jellyfin 10.10.7, E2E de API, canal nativo Foro, navegador real y revisión de logs. La presentación final en un dispositivo físico concreto depende del cliente oficial utilizado.
