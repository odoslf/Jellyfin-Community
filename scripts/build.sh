#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="1.5.0.0"
PUBLISH="$ROOT/artifacts/publish"
PACKAGE="$ROOT/artifacts/Jellyfin.Plugin.Community_${VERSION}.zip"
rm -rf "$ROOT/artifacts"
mkdir -p "$PUBLISH"
dotnet restore "$ROOT/Jellyfin.Plugin.Community.sln" --force-evaluate
dotnet build "$ROOT/Jellyfin.Plugin.Community.sln" -c Release --no-restore --warnaserror
dotnet test "$ROOT/Jellyfin.Plugin.Community.sln" -c Release --no-build --collect:"XPlat Code Coverage"
dotnet list "$ROOT/Jellyfin.Plugin.Community.sln" package --vulnerable --include-transitive | tee "$ROOT/artifacts/vulnerability-audit.txt"
if grep -Eiq 'has the following vulnerable packages|\b(Critical|High|Moderate|Low)\b' "$ROOT/artifacts/vulnerability-audit.txt"; then
  echo 'Vulnerable dependency detected.' >&2
  exit 1
fi
dotnet publish "$ROOT/src/Jellyfin.Plugin.Community/Jellyfin.Plugin.Community.csproj" -c Release --no-restore -o "$PUBLISH"
python3 "$ROOT/scripts/package.py" --publish "$PUBLISH" --output "$PACKAGE"
