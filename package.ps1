<#
.SYNOPSIS
    Builds NeuroMita.DLSS5 and produces the release archive (dist\NeuroMita.DLSS5-<version>.zip).

.DESCRIPTION
    Steps:
      1. read the version from src\NeuroMita.DLSS5\NeuroMita.DLSS5.csproj
      2. dotnet build -c Release (needs -GameDir / NEUROMITA_DIR or a game.dir file)
      3. stage the release layout:
             install.bat                  one click installer (double click)
             plugin\NeuroMita.DLSS5.dll   the BepInEx plugin
             tools\*.ps1                  install / verify / splash patch scripts
             README.md, README.ru.md, LICENSE, CHANGELOG.md, CONTRIBUTING.md
             docs\**
             SHA256SUMS.txt
      4. zip it and render the release notes template into dist\RELEASE-NOTES.md

.PARAMETER GameDir
    Game folder used to resolve the BepInEx interop references while building.

.PARAMETER Version
    Override the version taken from the csproj.

.PARAMETER NoBuild
    Reuse the existing bin\Release output instead of rebuilding.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File package.ps1 -GameDir "D:\Games\NeuroMita"
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$Version,
    [switch]$NoBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\NeuroMita.DLSS5\NeuroMita.DLSS5.csproj'

if (-not (Test-Path -LiteralPath $project)) { throw "project not found: $project" }

if (-not $Version) {
    $csproj = [IO.File]::ReadAllText($project)
    $m = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "could not read <Version> from $project" }
    $Version = $m.Groups[1].Value.Trim()
}
Write-Host "packaging NeuroMita.DLSS5 $Version" -ForegroundColor Cyan

# ---------------------------------------------------------------- build
if (-not $NoBuild) {
    Write-Host '[1/4] building ...' -ForegroundColor Cyan
    $buildArgs = @($project, '-c', $Configuration, '-v', 'm')
    if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
    & dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
} else {
    Write-Host '[1/4] build skipped (-NoBuild)' -ForegroundColor DarkGray
}

$dll = Join-Path $root "src\NeuroMita.DLSS5\bin\$Configuration\NeuroMita.DLSS5.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "built plugin not found: $dll" }

# ---------------------------------------------------------------- stage
Write-Host '[2/4] staging ...' -ForegroundColor Cyan
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "NeuroMita.DLSS5-$Version"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'plugin') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'tools') | Out-Null

Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'plugin\NeuroMita.DLSS5.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'installer\install.bat') -Destination (Join-Path $stage 'install.bat') -Force
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $root 'tools') -Filter *.ps1 -File) {
    Copy-Item -LiteralPath $script.FullName -Destination (Join-Path $stage 'tools') -Force
}
foreach ($file in @('README.md', 'README.ru.md', 'LICENSE', 'CHANGELOG.md', 'CONTRIBUTING.md')) {
    $p = Join-Path $root $file
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $stage -Force }
    else { Write-Warning "missing $file (skipped)" }
}
$docs = Join-Path $root 'docs'
if (Test-Path -LiteralPath $docs) {
    Copy-Item -LiteralPath $docs -Destination $stage -Recurse -Force
    # screenshots stay in the repository only: they are 1+ MB each and the README renders them
    # straight from GitHub, so the release archive stays small
    $shot = Join-Path $stage 'docs\screenshots'
    if (Test-Path -LiteralPath $shot) {
        Get-ChildItem -LiteralPath $shot -Filter *.png -File | Remove-Item -Force
    }
}

# ---------------------------------------------------------------- checksums
Write-Host '[3/4] checksums ...' -ForegroundColor Cyan
$lines = New-Object System.Collections.Generic.List[string]
foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName) {
    $rel = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines.Add("$hash  $rel")
}
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $lines.ToArray(), (New-Object Text.UTF8Encoding($false)))
Write-Host ("    {0} files hashed" -f $lines.Count)

# ---------------------------------------------------------------- zip + notes
Write-Host '[4/4] archiving ...' -ForegroundColor Cyan
$zip = Join-Path $dist "NeuroMita.DLSS5-$Version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("    {0} ({1:N0} bytes)" -f $zip, (Get-Item -LiteralPath $zip).Length)

$notesTemplate = Join-Path $root 'docs\RELEASE-NOTES.md'
if (Test-Path -LiteralPath $notesTemplate) {
    $notes = [IO.File]::ReadAllText($notesTemplate)
    $notes = $notes.Replace('<VERSION>', $Version).Replace('<DATE>', (Get-Date -Format 'yyyy-MM-dd'))
    $notesPath = Join-Path $dist 'RELEASE-NOTES.md'
    [IO.File]::WriteAllText($notesPath, $notes, (New-Object Text.UTF8Encoding($false)))
    Write-Host "    release notes: $notesPath"
    Write-Host ("    zip sha256: {0}" -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())
}

Write-Host ''
Write-Host "done: $zip" -ForegroundColor Green
