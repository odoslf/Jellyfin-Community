# Soporte

Antes de abrir una incidencia, confirma la versión de Jellyfin y la versión del plugin.

Para **Community 1.6.0.0**, la combinación soportada es Jellyfin Server **10.10.7** con **.NET 8** y ABI de catálogo **10.10.7.0**. Adjunta únicamente registros previamente censurados y los pasos mínimos para reproducir el problema. No publiques tokens, cookies, contraseñas, nombres de usuario reales ni bases de datos del servidor.

Para problemas de instalación desde el catálogo, comprueba primero que Jellyfin puede acceder al repositorio unificado ODOS3D:

`https://raw.githubusercontent.com/odoslf/Repositorio-plugin-Jelly-fin-odos3d.lab/main/manifest.json`

El catálogo unificado valida los ZIP y checksums de Community, JellyPremiere y JellyLiveNow y prueba los tres juntos en Jellyfin 10.10.7 antes de publicar cambios.
