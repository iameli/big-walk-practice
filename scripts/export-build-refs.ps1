<#
.SYNOPSIS
    Zips the BepInEx core + interop assemblies the plugin compiles against,
    for the GitHub Actions build (CI cannot reach this machine).
    Re-run whenever the game updates - interop regenerates per game version.
    Upload the result as the `build-refs.zip` asset on practice-canary:
        gh release upload practice-canary --clobber dist\ci-refs\build-refs.zip
#>
[CmdletBinding()]
param(
    [string]$Profile = "$env:APPDATA\r2modmanPlus-local\BigWalk\profiles\Default",
    [string]$Out = "$PSScriptRoot\..\dist\ci-refs"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$bep = Join-Path $Profile 'BepInEx'
foreach ($sub in @('core', 'interop')) {
    if (-not (Test-Path (Join-Path $bep $sub))) {
        throw "Missing $bep\$sub - launch the game modded once so interop generates."
    }
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("brefs_" + [System.IO.Path]::GetRandomFileName())
try {
    New-Item -ItemType Directory -Force (Join-Path $staging 'BepInEx\core') | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $staging 'BepInEx\interop') | Out-Null
    Copy-Item (Join-Path $bep 'core\*') (Join-Path $staging 'BepInEx\core\') -Recurse -Force
    Copy-Item (Join-Path $bep 'interop\*') (Join-Path $staging 'BepInEx\interop\') -Recurse -Force

    New-Item -ItemType Directory -Force $Out | Out-Null
    $zip = Join-Path $Out 'build-refs.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)

    $count = (Get-ChildItem (Join-Path $bep 'interop') -File).Count
    Write-Host "Wrote $zip ($count interop assemblies)"
} finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}