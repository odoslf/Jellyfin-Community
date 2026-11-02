# Odos3D Community 2.0 — Jellyfin 12.4 candidate

## Scope

This is a **migration candidate**, NOT a verified production release. It ports the existing Community 1.6 sources to `net10.0` and Jellyfin 12.4 APIs, retaining the plugin GUID and SQLite schema. The 1.6.0.0 release and historical validation reports are not overwritten. Validation must run before publishing 2.0.

## Required runtime checks

- Build and unit tests (.NET 10, Jellyfin 12.4)
- Real Jellyfin 12.4 API: anonymous rejected, admin, ordinary users, forum categories, create/edit/reply, moderation, polls, notifications, attachments, linked libraries
- Web frontend login in current Jellyfin Web modern/legacy, without `EnableLegacyAuthorization`
- Native `IChannel` functionality (where exposed by the native client)
- Existing Community SQLite data and attachments backup/restore across upgrade; test with populated database
- Coexistence with Odos3D JellyPremiere and JellyLiveNow after those have been ported too

Do not attempt release or production deployment unless CI is green and restore test passes. No promise of identical HTML forum views in native Smart TV applications; those clients require independent support.

## Files changed

- Production + test projects `net10.0`, Jellyfin 12.4 references and SQLite dependencies.
- New Jellyfin `User` entity and permission namespaces.
- Browser API authentication using standard `Authorization: MediaBrowser ...` instead of legacy `X-Emby-*` headers.
- Version constants 2.0.0.0 across code and E2E tests.
- `build.yaml` version/framework/ABI, plus separate GitHub Actions workflow for 12.4.

## Release safety

Original `.github/workflows/publish-1.6.0.0.yml` is unchanged. The migration branch **must not** match `release/**` or `release-*`; use `migration/jellyfin-12.4`. No production manifest is updated. Do not remove the old GitHub release or change the GUID.

New release automation should be built only after green CI, using the SHA-256 of the exact tested ZIP and adding an entry to the ODOS3D unified plugin catalog. Review packaging dependencies and test rollback with independent backups.
