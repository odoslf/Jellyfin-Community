# Estrategia de compatibilidad

## Línea 1.x

La línea estable actual está fijada a:

- Jellyfin Server **10.10.7**
- ABI de catálogo **10.10.7.0**
- .NET 8 / `net8.0`
- Community **1.6.0.0**

La interfaz rica **Foro** está validada en Jellyfin Web 10.10.7 y en el flujo de la app Android que carga ese cliente dentro de un WebView. Community no declara una página HTML nativa en clientes que no ejecutan Jellyfin Web, porque un plugin de servidor no dispone de un punto de extensión que permita inyectar vistas arbitrarias en Android TV u otros clientes nativos.

Para esos clientes, Community expone además el canal estándar **Foro** mediante `IChannel`. Su visibilidad y presentación dependen de que el cliente oficial muestre Channels de Jellyfin.

La versión 1.6.0.0 convive con JellyPremiere: ambos pueden integrarse en Jellyfin Web sin sobrescribirse mutuamente. El catálogo ODOS3D instala esta versión desde una release inmutable identificada por versión y checksum; no depende de ramas históricas de artefactos.

## Repositorio recomendado

Para instalaciones normales debe usarse únicamente el catálogo unificado:

`https://raw.githubusercontent.com/odoslf/Repositorio-plugin-Jelly-fin-odos3d.lab/main/manifest.json`

Ese catálogo comprueba el ABI, descarga el ZIP publicado, valida su checksum y, cuando cambia algún plugin, arranca los paquetes juntos en Jellyfin 10.10.7 antes de actualizar el manifest público.

## Versiones futuras

Cuando se prepare soporte para una generación de Jellyfin basada en otro runtime, se publicará como una versión distinta del plugin y con un `targetAbi` distinto. No se declarará compatibilidad con una nueva versión de Jellyfin hasta que compile, pase las pruebas y se valide contra las referencias oficiales de esa versión.
