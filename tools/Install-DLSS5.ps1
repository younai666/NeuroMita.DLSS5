<#
.SYNOPSIS
    Installs NeuroMita.DLSS5 into a NeuroMita installation (BepInEx plugin + recommended settings).

.DESCRIPTION
    Steps performed (all idempotent, no administrator rights required):

      1. locate and validate the game folder (NeuroMita.exe + BepInEx)
      2. copy NeuroMita.DLSS5.dll into BepInEx\plugins (and remove the old NM.DLSS5Menu.dll)
      3. merge the recommended ReShade settings (no startup HUD/banner) into ReShade.ini
      4. optionally install the DLSS5 stack via the upstream DLSS5-Feeder installer (-InstallStack)
      5. optionally patch the ReShade startup banner out of the local dxgi.dll (-PatchSplash)
      6. optionally run tools\Verify-Install.ps1 (-Verify, on by default)

    The DLSS5 stack itself (ReShade + DLSS5-Feeder + RenoDX + the NVIDIA NGX runtimes) is NOT
    redistributed here: it is downloaded from its own publishers. See docs/TROUBLESHOOTING.md.

.PARAMETER GameDir
    Folder containing NeuroMita.exe. If omitted, the script looks in its own folder, in game.dir
    next to the package root, and finally asks.

.PARAMETER PluginDll
    Path to NeuroMita.DLSS5.dll. Default: plugin\ next to the package root, then the build output.

.PARAMETER InstallStack
    Run the upstream DLSS5-Feeder installer (needs network access) when the stack is missing.

.PARAMETER PatchSplash
    Remove the ReShade startup banner by patching the local dxgi.dll (a backup is kept).

.PARAMETER Verify
    Run tools\Verify-Install.ps1 after installing (default: on).

.PARAMETER Uninstall
    Remove the plugin and restore dxgi.dll from the backup if one exists.

.PARAMETER Yes
    Non-interactive: assume "yes" for optional prompts.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Install-DLSS5.ps1 -GameDir "D:\Games\NeuroMita" -PatchSplash
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$PluginDll,
    [switch]$InstallStack,
    [switch]$PatchSplash,
    [switch]$Verify = $true,
    [switch]$Uninstall,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pkgRoot = Split-Path -Parent $toolsDir

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    $text" -ForegroundColor Green }
function Write-Warn2([string]$text) { Write-Host "    $text" -ForegroundColor Yellow }

function Resolve-GameDir {
    param([string]$given)
    if ($given) {
        if (-not (Test-Path -LiteralPath $given)) { throw "GameDir not found: $given" }
        return (Resolve-Path -LiteralPath $given).Path
    }
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add($pkgRoot)
    $candidates.Add((Get-Location).Path)
    $gameDirFile = Join-Path $pkgRoot 'game.dir'
    if (Test-Path -LiteralPath $gameDirFile) { $candidates.Add(((Get-Content -LiteralPath $gameDirFile -Raw).Trim())) }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath (Join-Path $c 'NeuroMita.exe'))) { return (Resolve-Path -LiteralPath $c).Path }
    }
    if ($Yes) { throw 'GameDir not given and no NeuroMita.exe found nearby (pass -GameDir).' }
    Write-Host 'NeuroMita.exe was not found automatically.' -ForegroundColor Yellow
    return (Read-Host 'Path to the game folder (contains NeuroMita.exe)')
}

function Resolve-PluginDll {
    param([string]$given)
    if ($given) {
        if (-not (Test-Path -LiteralPath $given)) { throw "PluginDll not found: $given" }
        return (Resolve-Path -LiteralPath $given).Path
    }
    $candidates = @(
        (Join-Path $pkgRoot 'plugin\NeuroMita.DLSS5.dll'),
        (Join-Path $pkgRoot 'NeuroMita.DLSS5.dll'),
        (Join-Path $pkgRoot 'src\NeuroMita.DLSS5\bin\Release\NeuroMita.DLSS5.dll'),
        (Join-Path $toolsDir 'NeuroMita.DLSS5.dll')
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }
    throw 'NeuroMita.DLSS5.dll not found. Build it first (see CONTRIBUTING.md) or pass -PluginDll.'
}

function Set-IniValues {
    param([string]$Path, [string]$Section, [hashtable]$Values)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.AddRange([IO.File]::ReadAllLines($Path))
    $sectionAt = -1
    $current = ''
    $applied = 0
    foreach ($kv in $Values.GetEnumerator()) {
        $found = $false
        $current = ''
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i].Trim()
            if ($line.Length -gt 2 -and $line[0] -eq '[' -and $line[-1] -eq ']') {
                $current = $line.Substring(1, $line.Length - 2)
                if ($current -eq $Section) { $sectionAt = $i }
                continue
            }
            if ($current -ne $Section) { continue }
            if ($line -match ('^' + [regex]::Escape($kv.Key) + '\s*=')) {
                if ($lines[$i] -ne "$($kv.Key)=$($kv.Value)") { $lines[$i] = "$($kv.Key)=$($kv.Value)"; $applied++ }
                $found = $true
                break
            }
        }
        if (-not $found) {
            if ($sectionAt -ge 0) { $lines.Insert($sectionAt + 1, "$($kv.Key)=$($kv.Value)"); $sectionAt++; $applied++ }
            else { $lines.Add("[$Section]"); $lines.Add("$($kv.Key)=$($kv.Value)"); $sectionAt = $lines.Count - 2; $applied++ }
        }
    }
    if ($applied -gt 0) {
        $bak = "$Path.dlss5bak"
        if (-not (Test-Path -LiteralPath $bak)) { Copy-Item -LiteralPath $Path -Destination $bak -Force }
        [IO.File]::WriteAllLines($Path, $lines.ToArray(), (New-Object Text.UTF8Encoding($false)))
    }
    return ($applied -gt 0)
}

# --------------------------------------------------------------------------- run

Write-Host ''
Write-Host 'NeuroMita.DLSS5 installer' -ForegroundColor Magenta
Write-Host ('-' * 60)

$game = Resolve-GameDir $GameDir
if (-not (Test-Path -LiteralPath (Join-Path $game 'NeuroMita.exe'))) { throw "NeuroMita.exe not found in $game" }
$bep = Join-Path $game 'BepInEx'
Write-Ok "game folder: $game"

# --- uninstall path ---------------------------------------------------------
if ($Uninstall) {
    Write-Step 'Removing plugin'
    $installed = Join-Path $bep 'plugins\NeuroMita.DLSS5.dll'
    if (Test-Path -LiteralPath $installed) { Remove-Item -LiteralPath $installed -Force; Write-Ok 'NeuroMita.DLSS5.dll removed' }
    else { Write-Warn2 'plugin was not installed' }
    $splash = Join-Path $toolsDir 'Patch-ReShadeSplash.ps1'
    if ((Test-Path -LiteralPath (Join-Path $game 'dxgi.dll.dlss5bak')) -and (Test-Path -LiteralPath $splash)) {
        & powershell -ExecutionPolicy Bypass -File $splash -GameDir $game -Restore
    }
    Write-Host 'ReShade.ini / ReShadePreset.ini backups (*.dlss5bak) were left in place.' -ForegroundColor DarkGray
    exit 0
}

# --- BepInEx ----------------------------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $bep 'core\BepInEx.Unity.IL2CPP.dll'))) {
    throw "BepInEx 6 (IL2CPP) was not found in $bep. Install BepInEx 6.0.0-be.788 into the game folder first (https://docs.bepinex.dev), launch the game once so that BepInEx\interop is generated, then run this installer again."
}

# --- plugin -----------------------------------------------------------------
Write-Step 'Installing plugin'
$plugins = Join-Path $bep 'plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
$dll = Resolve-PluginDll $PluginDll
Copy-Item -LiteralPath $dll -Destination (Join-Path $plugins 'NeuroMita.DLSS5.dll') -Force
Write-Ok ("NeuroMita.DLSS5.dll -> {0} ({1:N0} bytes)" -f $plugins, (Get-Item -LiteralPath $dll).Length)

$legacy = Join-Path $plugins 'NM.DLSS5Menu.dll'
if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Force; Write-Ok 'removed legacy NM.DLSS5Menu.dll' }

# --- DLSS5 stack ------------------------------------------------------------
$stackOk = (Test-Path -LiteralPath (Join-Path $game 'dxgi.dll')) -and
           (Test-Path -LiteralPath (Join-Path $game 'dlss5-feed.addon64')) -and
           (Test-Path -LiteralPath (Join-Path $game 'renodx-dlss5.addon64'))

if (-not $stackOk) {
    Write-Warn2 'DLSS5 stack (ReShade + Feeder + RenoDX) is not fully installed in this game folder.'
    $installer = Join-Path $toolsDir 'Install-DLSS5Stack.ps1'
    if ($InstallStack) {
        Write-Step 'Installing the DLSS5 stack from its upstream publishers'
        & powershell -ExecutionPolicy Bypass -File $installer -GameDir $game -Yes
    } else {
        Write-Warn2 "Run tools\Install-DLSS5Stack.ps1 -GameDir `"$game`" (or re-run this installer with -InstallStack)."
        Write-Warn2 'The plugin will still load, but there is nothing to feed until the stack is present.'
    }
}

# --- ReShade settings -------------------------------------------------------
$reshadeIni = Join-Path $game 'ReShade.ini'
if (Test-Path -LiteralPath $reshadeIni) {
    Write-Step 'Merging recommended ReShade settings'
    $overlayChanged = Set-IniValues -Path $reshadeIni -Section 'OVERLAY' -Values @{
        ShowFPS = '0'; ShowClock = '0'; ShowPresetTransitionMessage = '0'; ShowScreenshotMessage = '0'; ShowPresetName = '0'
    }
    $nrChanged = Set-IniValues -Path $reshadeIni -Section 'RenoDX.DLSS5' -Values @{
        NRCostMeter = '0'; UiWelcomed = '1'
    }
    if ($overlayChanged -or $nrChanged) { Write-Ok 'startup banner/HUD settings updated (ReShade.ini.dlss5bak keeps the original)' }
    else { Write-Ok 'ReShade settings already correct' }
} else {
    Write-Warn2 'ReShade.ini not found: install the stack first, then re-run to apply the recommended settings.'
}

# --- startup banner patch ---------------------------------------------------
$patchScript = Join-Path $toolsDir 'Patch-ReShadeSplash.ps1'
if (Test-Path -LiteralPath (Join-Path $game 'dxgi.dll')) {
    $splashPatched = $false
    try {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $game 'dxgi.dll'))
        if ($bytes.Length -gt 0xD08EA) {
            $slice = ($bytes[0xD08E4..0xD08E9] | ForEach-Object { $_.ToString('X2') }) -join ' '
            $splashPatched = ($slice -eq 'E9 E8 09 00 00 90')
        }
    } catch { }

    if ($PatchSplash) {
        Write-Step 'Removing the ReShade startup banner (binary patch)'
        & powershell -ExecutionPolicy Bypass -File $patchScript -GameDir $game
    } elseif ($splashPatched) {
        Write-Step 'ReShade startup banner already patched'
        Write-Ok 'the splash window stays hidden (restore with tools\Patch-ReShadeSplash.ps1 -Restore)'
    } else {
        Write-Warn2 'The ReShade splash banner still shows for ~5 s at startup. Add -PatchSplash to remove it (a backup of dxgi.dll is kept).'
    }
}

# --- verification -----------------------------------------------------------
if ($Verify) {
    Write-Step 'Verifying the installation'
    $verifyScript = Join-Path $toolsDir 'Verify-Install.ps1'
    if (Test-Path -LiteralPath $verifyScript) {
        & powershell -ExecutionPolicy Bypass -File $verifyScript -GameDir $game
        $code = $LASTEXITCODE
        Write-Host ''
        if ($code -eq 0) { Write-Host 'Installation verified.' -ForegroundColor Green }
        else { Write-Host 'Verification reported failures: see the report above.' -ForegroundColor Red }
        exit $code
    }
}

Write-Host ''
Write-Host 'Done. Start the game and open: Settings -> Graphics -> "DLSS5 Neural Rendering".' -ForegroundColor Green
