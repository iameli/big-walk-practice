<#
.SYNOPSIS
    Builds every mod under mods\ against the BepInEx refs under -Profile
    (default: the r2modman profile's generated interop) and optionally deploys
    each DLL into the profile's plugins\<Mod>\ folder.

    CI calls this with -Profile pointing at the unpacked ci-refs snapshot.
#>
[CmdletBinding()]
param(
    [string]$Profile = "$env:APPDATA\r2modmanPlus-local\BigWalk\profiles\Default",
    [switch]$Deploy,
    [string]$Configuration = 'Release'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$modsRoot = Join-Path $repo 'mods'

if (-not (Test-Path (Join-Path $Profile 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw "BepInEx interop not found under $Profile. Launch the game modded once (r2modman) so it generates."
}

$projects = Get-ChildItem $modsRoot -Recurse -Filter '*.csproj'
if (-not $projects) { throw "No mod projects under $modsRoot" }

$profilePlugins = Join-Path $Profile 'BepInEx\plugins'
foreach ($proj in $projects) {
    Write-Host "Building $($proj.BaseName)..."
    & dotnet build $proj.FullName -c $Configuration -v m "/p:GamePath=$Profile"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($proj.Name)" }

    if ($Deploy) {
        $dll = Get-ChildItem (Join-Path $proj.DirectoryName 'bin') -Recurse -Filter "$($proj.BaseName).dll" |
               Where-Object { $_.FullName -match '\\bin\\' -and $_.FullName -notmatch '\\obj\\' } |
               Select-Object -First 1
        if (-not $dll) { throw "No build output for $($proj.BaseName)" }

        $dest = Join-Path $profilePlugins $proj.BaseName
        New-Item -ItemType Directory -Force $dest | Out-Null
        Copy-Item $dll.FullName (Join-Path $dest $dll.Name) -Force
        Write-Host "  deployed -> $($dll.Name)"
    }
}

Write-Host ""
Write-Host "Done. Launch Big Walk via r2modman to test. Log: $Profile\BepInEx\LogOutput.log"