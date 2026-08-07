# Contribuir a Jellyfin Community

Gracias por mejorar Community. El objetivo del proyecto es mantener una base pequeña, auditable y compatible con la versión de Jellyfin declarada en cada artefacto.

## Flujo de trabajo

1. Cree una rama desde `main`.
2. Mantenga los cambios acotados y documente cualquier cambio de esquema o API.
3. Ejecute `./scripts/build.ps1` en Windows o `./scripts/build.sh` en Linux.
4. No reduzca las comprobaciones de seguridad, cuotas, permisos ni validación de entradas para hacer pasar una prueba.
5. Añada o actualice pruebas para toda corrección de seguridad, migración o comportamiento nuevo.
6. Abra un pull request describiendo impacto, compatibilidad y pruebas realizadas.

## Compatibilidad

La línea 1.x está dirigida a Jellyfin 10.10.7 / .NET 8. Un cambio que requiera otra ABI o runtime debe publicarse como versión separada; no debe romper el paquete estable existente.

## Estilo

- C# con nullable habilitado.
- Advertencias tratadas como errores.
- Analizadores de .NET habilitados.
- SQL parametrizado.
- Sin secretos, tokens, cookies ni datos reales de servidores en pruebas o incidencias.

## Seguridad

No publique una vulnerabilidad explotable en una incidencia pública. Siga `SECURITY.md` y elimine de los registros cualquier token, IP privada, ruta sensible, usuario real o contenido de base de datos.
