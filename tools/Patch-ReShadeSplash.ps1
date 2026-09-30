<#
.SYNOPSIS
    Applies (or restores) the ReShade startup-banner patch in a local dxgi.dll.

.DESCRIPTION
    ReShade 6.8 draws a splash window ("ReShade 6.8.0 / Visit https://reshade.me ...") while the
    effects compile and for ~5 seconds afterwards. It has no configuration switch: the overlay
    forces its own alpha to 1.0 and the condition is hard-coded in runtime_gui.cpp, so the only way
    to remove it is to skip the drawing block in the binary.

    The guarded block starts with the ImGui call that uses the literal "Splash Window". Its guard
    in the shipped ReShade 6.8.0 build is

        0x1800D14E4   je  0x1800D1ED1        (file offset 0xD08E4: 0F 84 E7 09 00 00)

    This script rewrites it to an unconditional jump

        E9 E8 09 00 00 90                    (rel32 must be E8: with E7 the jump lands one byte
                                              early and the game crashes on startup)

    A backup (dxgi.dll.dlss5bak) is written next to the file the first time. Use -Restore to put the
    original bytes back.

.PARAMETER GameDir
    Folder containing NeuroMita.exe and the local dxgi.dll.

.PARAMETER Restore
    Restore dxgi.dll from the backup instead of patching.

.PARAMETER Force
    Patch even when the target bytes are not the expected ones (not recommended).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [switch]$Restore,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$Offset = 0xD08E4
$Expected = '0F 84 E7 09 00 00'
$Patched = 'E9 E8 09 00 00 90'

$dxgi = Join-Path (Resolve-Path -LiteralPath $GameDir).Path 'dxgi.dll'
$backup = "$dxgi.dlss5bak"

if (-not (Test-Path -LiteralPath $dxgi)) { throw "dxgi.dll not found at $dxgi" }

if ($Restore) {
    if (-not (Test-Path -LiteralPath $backup)) { throw "no backup found at $backup" }
    Copy-Item -LiteralPath $backup -Destination $dxgi -Force
    Write-Host "restored dxgi.dll from $backup" -ForegroundColor Green
    exit 0
}

$bytes = [IO.File]::ReadAllBytes($dxgi)
if ($bytes.Length -le $Offset + 5) { throw "dxgi.dll is too small to be a ReShade 6.8 build" }

$current = ($bytes[$Offset..($Offset + 5)] | ForEach-Object { $_.ToString('X2') }) -join ' '

if ($current -eq $Patched) {
    Write-Host 'already patched (startup banner stays hidden)' -ForegroundColor Green
    exit 0
}
if ($current -ne $Expected -and -not $Force) {
    throw "unexpected bytes at 0x$('{0:X}' -f $Offset): $current (expected $Expected). This is a different ReShade build; patch not applied."
}

if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $dxgi -Destination $backup -Force
    Write-Host "backup written to $backup"
}

$bytes[$Offset] = 0xE9; $bytes[$Offset + 1] = 0xE8; $bytes[$Offset + 2] = 0x09
$bytes[$Offset + 3] = 0x00; $bytes[$Offset + 4] = 0x00; $bytes[$Offset + 5] = 0x90
[IO.File]::WriteAllBytes($dxgi, $bytes)

$verify = ([IO.File]::ReadAllBytes($dxgi)[$Offset..($Offset + 5)] | ForEach-Object { $_.ToString('X2') }) -join ' '
if ($verify -ne $Patched) { throw "patch verification failed: $verify" }

Write-Host "patched dxgi.dll at 0x$('{0:X}' -f $Offset): $Expected -> $Patched" -ForegroundColor Green
Write-Host 'The ReShade startup banner will no longer be drawn. Restore with -Restore.' -ForegroundColor DarkGray
