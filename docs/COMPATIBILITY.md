# Estrategia de compatibilidad

## Línea 1.x

La línea estable inicial está congelada sobre:

- Jellyfin Server 10.10.7
- ABI de catálogo 10.10.7.0
- .NET 8 / net8.0

El paquete 1.0.0.0 se conserva en una rama de artefacto (`release-1.0.0.0`) para que el `sourceUrl` del catálogo no cambie aunque `main` evolucione.

## Versiones futuras

Cuando se prepare soporte para una generación de Jellyfin basada en otro runtime, se hará como una versión distinta del plugin y con un `targetAbi` distinto dentro del mismo `manifest.json`. Esto permite que cada servidor seleccione una versión compatible sin sustituir el artefacto de 10.10.7.

No se declarará compatibilidad con una nueva versión de Jellyfin hasta que compile, pase las pruebas y se valide contra las referencias oficiales de esa versión.
