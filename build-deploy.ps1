<#
.SYNOPSIS
    Builds the Big Walk plugins against the r2modman profile interop and
    deploys the DLLs into the profile's plugins folder.
    Run this, then hit Play in r2modman to test.
#>
[CmdletBinding()]
param(
    [string]$Profile = "$env:APPDATA\r2modmanPlus-local\BigWalk\profiles\Default",
    [string]$PluginsRoot = "$PSScriptRoot\bigwalk-mods\plugins",
    [string]$Configuration = 'Release'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Profile 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw "Interop not found under $Profile. Launch the game modded once (r2modman) so BepInEx generates it."
}

$projects = Get-ChildItem $PluginsRoot -Recurse -Filter '*.csproj'
if (-not $projects) { throw "No plugin projects under $PluginsRoot" }

$profilePlugins = Join-Path $Profile 'BepInEx\plugins'
$deployed = @()

foreach ($proj in $projects) {
    Write-Host "Building $($proj.BaseName)..."
    & dotnet build $proj.FullName -c $Configuration -v m /p:GamePath="$Profile"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($proj.Name)" }

    $dll = Get-ChildItem (Join-Path $proj.DirectoryName 'bin') -Recurse -Filter "$($proj.BaseName).dll" |
           Where-Object { $_.FullName -match '\\bin\\' -and $_.FullName -notmatch '\\obj\\' } |
           Select-Object -First 1
    if (-not $dll) { throw "No build output for $($proj.BaseName)" }

    $destDir = Join-Path $profilePlugins $proj.BaseName
    New-Item -ItemType Directory -Force $destDir | Out-Null
    Copy-Item $dll.FullName (Join-Path $destDir $dll.Name) -Force
    $deployed += $dll.Name
}

Write-Host ""
Write-Host "Deployed to $profilePlugins :" -ForegroundColor Green
$deployed | ForEach-Object { Write-Host "  $_" }
Write-Host "Launch Big Walk via r2modman to test. Log: $Profile\BepInEx\LogOutput.log"