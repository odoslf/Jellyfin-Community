#!/usr/bin/env python3
"""Apply first-pass Jellyfin 12.4 API/runtime migration to the original Community 1.6 source.

Runs only on a checkout of odoslf/Jellyfin-Community in an isolated branch.
No network calls and no modifications to user data or existing releases.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from shutil import copy2

OLD = '1.6.0.0'
NEW = '2.0.0.0'
ABI = '12.0.0.0'


def replacement(src: str, old: str, new: str, name: str, min_count: int = 1) -> str:
    count = src.count(old)
    if count < min_count:
        raise RuntimeError(f'{name}: expected at least {min_count} occurrences of {old!r}, got {count}; source may have changed')
    return src.replace(old, new)


def modify(root: Path, rel: str, changes: list[tuple[str, str, int]], results: list[str]) -> None:
    p = root / rel
    if not p.is_file():
        raise FileNotFoundError(f'Required original file missing: {rel}')
    original = p.read_text(encoding='utf-8')
    updated = original
    for old, new, min_count in changes:
        updated = replacement(updated, old, new, rel, min_count)
    if updated != original:
        p.write_text(updated, encoding='utf-8', newline='\n')
        results.append(rel)


def apply(root: Path, kit: Path) -> list[str]:
    if not (root / 'Jellyfin.Plugin.Community.sln').is_file():
        raise RuntimeError('Run --repo against the root of a cloned Jellyfin-Community repository')
    if not (root / '.git').exists():
        raise RuntimeError('This must be a Git checkout. Create a NEW migration branch before applying.')
    project = 'src/Jellyfin.Plugin.Community/Jellyfin.Plugin.Community.csproj'
    orig = (root / project).read_text(encoding='utf-8')
    if '<TargetFramework>net10.0</TargetFramework>' in orig:
        raise RuntimeError('This checkout seems migrated already. No changes applied.')
    if '<TargetFramework>net8.0</TargetFramework>' not in orig or '<Version>1.6.0.0</Version>' not in orig:
        raise RuntimeError('Source is not the Community 1.6.0.0 baseline. Review manually; no changes applied.')

    # Phase 1: validate ALL critical source anchors before modifying files.
    anchors = {
        'src/Jellyfin.Plugin.Community/Domain/Models.cs': 'Jellyfin.Data.Entities.User',
        'src/Jellyfin.Plugin.Community/Services/CurrentUserService.cs': 'using Jellyfin.Data.Enums;',
        'src/Jellyfin.Plugin.Community/Web/communityForum15.js': "'X-Emby-Token': state.auth.token",
        'global.json': '8.0.124',
        'tests/Jellyfin.Plugin.Community.Tests/Jellyfin.Plugin.Community.Tests.csproj': '<TargetFramework>net8.0</TargetFramework>',
        'build.yaml': 'targetAbi: "10.10.7.0"',
    }
    for rel, anchor in anchors.items():
        if anchor not in (root / rel).read_text(encoding='utf-8'):
            raise RuntimeError(f'Expected baseline signature missing: {rel}: {anchor}')

    changed: list[str] = []
    pkgs = ['Jellyfin.Controller','Jellyfin.Model','Jellyfin.Common','Jellyfin.Data']
    proj_changes: list[tuple[str,str,int]] = [
        ('<TargetFramework>net8.0</TargetFramework>','<TargetFramework>net10.0</TargetFramework>',1),
        ('<Version>1.6.0.0</Version>','<Version>2.0.0.0</Version>',1),
        ('<AssemblyVersion>1.6.0.0</AssemblyVersion>','<AssemblyVersion>2.0.0.0</AssemblyVersion>',1),
        ('<FileVersion>1.6.0.0</FileVersion>','<FileVersion>2.0.0.0</FileVersion>',1),
    ]
    for pkg in pkgs:
        proj_changes.append((f'Include="{pkg}" Version="10.10.7"',f'Include="{pkg}" Version="12.4.0"',1))
    proj_changes += [('Include="Microsoft.Data.Sqlite.Core" Version="8.0.11"', 'Include="Microsoft.Data.Sqlite.Core" Version="10.0.14"',1),
        ('<PackageReference Include="Jellyfin.Data" Version="12.4.0"><ExcludeAssets>runtime</ExcludeAssets></PackageReference>',
         '<PackageReference Include="Jellyfin.Data" Version="12.4.0"><ExcludeAssets>runtime</ExcludeAssets></PackageReference>\n    <PackageReference Include="Jellyfin.Database.Implementations" Version="12.4.0"><ExcludeAssets>runtime</ExcludeAssets></PackageReference>',1)]
    modify(root, project, proj_changes, changed)

    test_proj = 'tests/Jellyfin.Plugin.Community.Tests/Jellyfin.Plugin.Community.Tests.csproj'
    test_changes: list[tuple[str,str,int]] = [('<TargetFramework>net8.0</TargetFramework>','<TargetFramework>net10.0</TargetFramework>',1)]
    for pkg in pkgs:
        test_changes.append((f'Include="{pkg}" Version="10.10.7"',f'Include="{pkg}" Version="12.4.0"',1))
    test_changes += [('Include="Microsoft.Data.Sqlite.Core" Version="8.0.11"','Include="Microsoft.Data.Sqlite.Core" Version="10.0.14"',1),
        ('Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12"','Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5"',1),
        ('<PackageReference Include="Jellyfin.Data" Version="12.4.0" PrivateAssets="all" />',
         '<PackageReference Include="Jellyfin.Data" Version="12.4.0" PrivateAssets="all" />\n    <PackageReference Include="Jellyfin.Database.Implementations" Version="12.4.0" PrivateAssets="all" />',1)]
    modify(root,test_proj,test_changes,changed)

    global_json = root / 'global.json'
    global_data = json.loads(global_json.read_text(encoding='utf-8'))
    global_data['sdk']['version'] = '10.0.100'
    global_data['sdk']['rollForward'] = 'latestFeature'
    global_json.write_text(json.dumps(global_data, indent=2) + '\n',encoding='utf-8')
    changed.append('global.json')

    modify(root,'src/Jellyfin.Plugin.Community/Domain/Models.cs', [('Jellyfin.Data.Entities.User','Jellyfin.Database.Implementations.Entities.User',1)],changed)
    modify(root,'src/Jellyfin.Plugin.Community/Services/CurrentUserService.cs',
        [('using Jellyfin.Data.Enums;', 'using Jellyfin.Data;\nusing Jellyfin.Database.Implementations.Enums;',1)], changed)

    # Jellyfin 12 defaults to disabling X-Emby legacy auth. Use modern Authorization header.
    js = 'src/Jellyfin.Plugin.Community/Web/communityForum15.js'
    js_text = (root / js).read_text(encoding='utf-8')
    js_text = replacement(js_text, "'X-Emby-Token': state.auth.token,\n            'X-Emby-Authorization':", "'Authorization':", js)
    js_text = replacement(js_text,
        "headers: { 'X-Emby-Token': state.auth.token },",
        'headers: { Authorization: `MediaBrowser Client="Odos3D Community", Device="Web", DeviceId="community-forum", Version="${VERSION}", Token="${state.auth.token}"` },',js)
    # JWT/auth tokens NEVER go in URL or logs. Keep existing escaping for all user content.
    (root / js).write_text(js_text, encoding='utf-8',newline='\n')
    changed.append(js)

    # Upgrade version strings in code and tests, but preserve old release workflow and historical reports.
    for base in ('src/Jellyfin.Plugin.Community','tests'):
        for p in (root / base).rglob('*'):
            if p.is_file() and p.suffix.lower() in {'.cs','.js','.mjs','.py','.html','.json','.css'} and p.as_posix().endswith('communityForum15.js') is False:
                c = p.read_text(encoding='utf-8')
                if OLD in c:
                    p.write_text(c.replace(OLD,NEW),encoding='utf-8',newline='\n')
                    changed.append(p.relative_to(root).as_posix())
    # The main JS file was excluded above; update its VERSION constant too.
    c = (root / js).read_text(encoding='utf-8')
    if OLD in c:
        (root / js).write_text(c.replace(OLD,NEW),encoding='utf-8',newline='\n')

    modify(root,'build.yaml', [('version: "1.6.0.0"','version: "2.0.0.0"',1),
        ('targetAbi: "10.10.7.0"',f'targetAbi: "{ABI}"',1),
        ('framework: "net8.0"','framework: "net10.0"',1),
        ('1.6.0.0 consolida','2.0.0.0 migra',1),
        ('Validado sobre Jellyfin 10.10.7 / .NET 8.', 'Pendiente de validación completa sobre Jellyfin 12.4 / .NET 10.',1)],changed)

    # New workflow; do not alter/remove previous 1.6 release or its auto-publisher.
    dest = root / '.github' / 'workflows' / 'community-12_4-validate.yml'
    dest.parent.mkdir(parents=True,exist_ok=True)
    copy2(kit / 'workflow' / 'community-12_4-validate.yml',dest)
    changed.append('.github/workflows/community-12_4-validate.yml')

    docs = root / 'docs' / 'COMMUNITY_12_4_MIGRATION.md'
    docs.parent.mkdir(parents=True, exist_ok=True)
    copy2(kit / 'docs' / 'COMMUNITY_12_4_MIGRATION.md',docs)
    changed.append('docs/COMMUNITY_12_4_MIGRATION.md')

    print('Updated files:',len(set(changed)))
    for item in sorted(set(changed)):
        print(' -',item)
    print('WARNING: no .NET compilation has been executed by this script.')
    print('Push to migration/jellyfin-12.4; GitHub Actions will run build and E2E; DO NOT publish if red.')
    return changed


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--repo',type=Path,default=Path.cwd())
    args = p.parse_args()
    apply(args.repo.resolve(),Path(__file__).parent.resolve())


if __name__=='__main__':
    main()
