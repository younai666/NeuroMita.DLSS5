<#
.SYNOPSIS
    Installs the DLSS5 stack (ReShade 6.8 + DLSS5-Feeder + RenoDX + NGX runtimes) into a game folder.

.DESCRIPTION
    This repository does not redistribute NVIDIA/ReShade/Feeder binaries. This script simply drives
    the upstream one-command installer and passes the options that are correct for NeuroMita:

      - Render API is forced to D3D: NeuroMita presents with Direct3D 11, while a string scan of
        UnityPlayer.dll makes the upstream installer guess Vulkan.
      - Neural consumer is RenoDX (the only consumer the upstream installer can fetch unattended).

    Requires network access to GitHub (and reshade.me).

.PARAMETER GameDir
    Folder containing NeuroMita.exe.

.PARAMETER FeederTag
    DLSS5-Feeder release tag to use. Default v1.18.0-beta.1 (the version this project was verified
    against).

.PARAMETER Consumer
    RenoDX (default) or DFC. Deep Fried Chicken is not published by its author, so the upstream
    installer asks for a local zip when DFC is selected.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$FeederTag = 'v1.18.0-beta.1',
    [ValidateSet('RenoDX', 'DFC')][string]$Consumer = 'RenoDX',
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$game = (Resolve-Path -LiteralPath $GameDir).Path
$exe = Join-Path $game 'NeuroMita.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "NeuroMita.exe not found in $game" }

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installer = Join-Path $toolsDir 'Install-DLSS5Feeder.ps1'

if (-not (Test-Path -LiteralPath $installer)) {
    $url = "https://raw.githubusercontent.com/jlrouzies-fr/DLSS5-Feeder/$FeederTag/tools/Install-DLSS5Feeder.ps1"
    Write-Host "Downloading the upstream installer ($FeederTag) ..." -ForegroundColor Cyan
    $ok = $false
    foreach ($attempt in 1..4) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing -TimeoutSec 60
            $ok = $true; break
        } catch {
            Write-Warning "attempt $attempt failed: $($_.Exception.Message)"
            Start-Sleep -Seconds 3
        }
    }
    if (-not $ok) { throw "could not download $url" }
    Write-Host ("saved to {0} ({1:N0} bytes)" -f $installer, (Get-Item -LiteralPath $installer).Length)
}

$arguments = @(
    '-ExecutionPolicy', 'Bypass', '-File', $installer, $exe,
    '-Api', 'D3D',
    '-Consumer', $Consumer,
    '-HelperMode', 'No',
    '-NoPause'
)
if ($Yes) { $arguments += '-Yes' }

Write-Host "Running: powershell $($arguments -join ' ')" -ForegroundColor DarkGray
& powershell @arguments
$code = $LASTEXITCODE
if ($code -ne 0) { throw "upstream installer exited with $code" }

Write-Host ''
Write-Host 'DLSS5 stack installed. Re-run tools\Install-DLSS5.ps1 to apply the recommended ReShade settings.' -ForegroundColor Green
