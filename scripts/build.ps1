$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Version = '1.6.0.0'
$Publish = Join-Path $Root 'artifacts/publish'
$Package = Join-Path $Root "artifacts/Jellyfin.Plugin.Community_$Version.zip"
$Audit = Join-Path $Root 'artifacts/vulnerability-audit.txt'
Remove-Item (Join-Path $Root 'artifacts') -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Publish -ItemType Directory -Force | Out-Null
dotnet restore (Join-Path $Root 'Jellyfin.Plugin.Community.sln') --force-evaluate
dotnet build (Join-Path $Root 'Jellyfin.Plugin.Community.sln') -c Release --no-restore --warnaserror
dotnet test (Join-Path $Root 'Jellyfin.Plugin.Community.sln') -c Release --no-build '--collect:XPlat Code Coverage'
dotnet list (Join-Path $Root 'Jellyfin.Plugin.Community.sln') package --vulnerable --include-transitive | Tee-Object -FilePath $Audit
if (Select-String -Path $Audit -Pattern 'has the following vulnerable packages|\b(Critical|High|Moderate|Low)\b' -Quiet) {
    throw 'Vulnerable dependency detected.'
}
dotnet publish (Join-Path $Root 'src/Jellyfin.Plugin.Community/Jellyfin.Plugin.Community.csproj') -c Release --no-restore -o $Publish
python (Join-Path $Root 'scripts/package.py') --publish $Publish --output $Package
