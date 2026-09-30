<#
.SYNOPSIS
    Offline verification of a NeuroMita.DLSS5 installation.

.DESCRIPTION
    Checks the game folder, BepInEx, the plugin, the DLSS5-Feeder/RenoDX stack, the configuration
    files and (when logs are present) whether the master switch actually activated neural rendering.

    Nothing is launched and nothing is modified: this is a read-only report, so it is safe to run
    while the game is closed. Exit code is 0 when there is no FAIL, 1 otherwise.

.PARAMETER GameDir
    The folder that contains NeuroMita.exe (the one that also contains BepInEx\).

.PARAMETER Json
    Emit the result as JSON instead of the human readable report.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Verify-Install.ps1 -GameDir "D:\Games\NeuroMita"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$script:results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [ValidateSet('OK', 'WARN', 'FAIL', 'INFO')][string]$Status,
        [string]$Area,
        [string]$Message,
        [string]$Detail = ''
    )
    $script:results.Add([pscustomobject]@{ Status = $Status; Area = $Area; Message = $Message; Detail = $Detail })
}

function Get-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return [IO.File]::ReadAllText($path)
}

function Get-IniSection([string]$path, [string]$section) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    $current = ''
    foreach ($raw in [IO.File]::ReadAllLines($path)) {
        $line = $raw.Trim()
        if ($line.Length -gt 2 -and $line[0] -eq '[' -and $line[-1] -eq ']') {
            $current = $line.Substring(1, $line.Length - 2); continue
        }
        if ($current -ne $section) { continue }
        $eq = $line.IndexOf('=')
        if ($eq -gt 0) { $map[$line.Substring(0, $eq).Trim()] = $line.Substring($eq + 1).Trim() }
    }
    return $map
}

function Get-CfgValue([string]$path, [string]$key) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    foreach ($raw in [IO.File]::ReadAllLines($path)) {
        $line = $raw.Trim()
        if ($line -match ('^' + [regex]::Escape($key) + '\s*=\s*(.*)$')) { return $Matches[1].Trim() }
    }
    return $null
}

function Test-Bom([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $fs = [IO.File]::OpenRead($path)
    try {
        if ($fs.Length -lt 3) { return $false }
        $b = New-Object byte[] 3
        [void]$fs.Read($b, 0, 3)
        return ($b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
    } finally { $fs.Dispose() }
}

function Get-FileVersionString([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return ([Diagnostics.FileVersionInfo]::GetVersionInfo($path)).FileVersion } catch { return $null }
}

# --------------------------------------------------------------------------- checks

if (-not (Test-Path -LiteralPath $GameDir)) {
    Write-Host "GameDir not found: $GameDir" -ForegroundColor Red
    exit 1
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path

# 1. game
$exe = Join-Path $GameDir 'NeuroMita.exe'
if (Test-Path -LiteralPath $exe) { Add-Result OK 'game' "NeuroMita.exe found" } else { Add-Result FAIL 'game' "NeuroMita.exe not found in $GameDir" }
if (Test-Path -LiteralPath (Join-Path $GameDir 'GameAssembly.dll')) { Add-Result OK 'game' 'IL2CPP build (GameAssembly.dll present)' } else { Add-Result FAIL 'game' 'GameAssembly.dll missing (not an IL2CPP build?)' }
$dataDir = Join-Path $GameDir 'NeuroMita_Data'
if (Test-Path -LiteralPath $dataDir) { Add-Result OK 'game' 'NeuroMita_Data present' } else { Add-Result FAIL 'game' 'NeuroMita_Data missing' }

# 2. BepInEx
$bep = Join-Path $GameDir 'BepInEx'
$core = Join-Path $bep 'core'
if (Test-Path -LiteralPath (Join-Path $core 'BepInEx.Unity.IL2CPP.dll')) { Add-Result OK 'bepinex' 'BepInEx 6 IL2CPP core present' } else { Add-Result FAIL 'bepinex' 'BepInEx core missing: install BepInEx 6.0.0-be.788 into the game folder' }
if (Test-Path -LiteralPath (Join-Path $GameDir 'winhttp.dll')) { Add-Result OK 'bepinex' 'doorstop proxy (winhttp.dll) present' } else { Add-Result WARN 'bepinex' 'winhttp.dll missing (doorstop proxy)' }
if (Test-Path -LiteralPath (Join-Path $bep 'interop\Assembly-CSharp.dll')) { Add-Result OK 'bepinex' 'interop assemblies generated' } else { Add-Result WARN 'bepinex' 'BepInEx\interop not generated yet: launch the game once' }

# 3. plugin
$plugin = Join-Path $bep 'plugins\NeuroMita.DLSS5.dll'
if (Test-Path -LiteralPath $plugin) {
    $v = Get-FileVersionString $plugin
    Add-Result OK 'plugin' "NeuroMita.DLSS5.dll present$(if ($v) { " (file version $v)" })"
} else {
    Add-Result FAIL 'plugin' 'NeuroMita.DLSS5.dll not found in BepInEx\plugins'
}
if (Test-Path -LiteralPath (Join-Path $bep 'plugins\NM.DLSS5Menu.dll')) {
    Add-Result WARN 'plugin' 'old NM.DLSS5Menu.dll still present: delete it to avoid two plugins loading'
}

# 4. DLSS5 stack
$dxgi = Join-Path $GameDir 'dxgi.dll'
if (Test-Path -LiteralPath $dxgi) {
    $v = Get-FileVersionString $dxgi
    Add-Result OK 'stack' "ReShade proxy dxgi.dll present$(if ($v) { " (file version $v)" })"
} else {
    Add-Result FAIL 'stack' 'dxgi.dll (ReShade) missing: install ReShade 6.8 add-on build'
}
$stackFiles = @(
    @{ Path = 'dlss5-feed.addon64';        Name = 'DLSS5-Feeder add-on';        Required = $true },
    @{ Path = 'renodx-dlss5.addon64';      Name = 'RenoDX DLSS5 add-on';        Required = $true },
    @{ Path = 'nvngx_dlss.dll';            Name = 'DLSS SR runtime';            Required = $true },
    @{ Path = 'nvngx_dlssnr.dll';          Name = 'DLSS NR model (RTX 50 only)'; Required = $true },
    @{ Path = 'reshade-shaders\Shaders\DLSS5_Feed.fx'; Name = 'DLSS5_Feed.fx';   Required = $true }
)
foreach ($f in $stackFiles) {
    $p = Join-Path $GameDir $f.Path
    if (Test-Path -LiteralPath $p) {
        $len = (Get-Item -LiteralPath $p).Length
        Add-Result OK 'stack' "$($f.Name) present" ("{0:N0} bytes" -f $len)
    } else {
        Add-Result $(if ($f.Required) { 'FAIL' } else { 'WARN' }) 'stack' "$($f.Name) missing"
    }
}
$preset = Join-Path $GameDir 'ReShadePreset.ini'
if ((Get-Text $preset) -match 'DLSS5_Feed') { Add-Result OK 'stack' 'ReShade preset enables DLSS5_Feed' } else { Add-Result WARN 'stack' 'ReShadePreset.ini does not mention DLSS5_Feed' }

# 5. feeder config
$cfg = Join-Path $GameDir 'dlss5-feed.cfg'
if (Test-Path -LiteralPath $cfg) {
    if (Test-Bom $cfg) { Add-Result FAIL 'config' 'dlss5-feed.cfg has a UTF-8 BOM: DLSS5-Feeder ignores the file in that state' } else { Add-Result OK 'config' 'dlss5-feed.cfg has no BOM' }
    $enabled = Get-CfgValue $cfg 'enabled'
    $mode = Get-CfgValue $cfg 'mode'
    if ($enabled -eq '1') { Add-Result OK 'config' 'enabled=1 (feeder keeps watching the file)' }
    else { Add-Result FAIL 'config' "enabled=$enabled : never write 0 here, the feeder stops watching its config and the switch gets stuck until the game restarts" }
    if ($mode -eq '2') { Add-Result OK 'config' 'mode=2 (full DLSS path; this is what the master switch drives)' }
    elseif ($mode -eq '0') { Add-Result WARN 'config' 'mode=0 : neural rendering is switched OFF' }
    else { Add-Result WARN 'config' "mode='$mode' (expected 2 or 0)" }
} else {
    Add-Result WARN 'config' 'dlss5-feed.cfg not created yet: it appears after the first run with the feeder installed'
}

# 6. ReShade / RenoDX settings
$ini = Join-Path $GameDir 'ReShade.ini'
$nr = Get-IniSection $ini 'RenoDX.DLSS5'
if ($nr.Count -gt 0) {
    Add-Result OK 'reshade' '[RenoDX.DLSS5] section present'
    $recommended = @(
        @{ Key = 'ShowFPS';                    Value = '0'; Section = 'OVERLAY' },
        @{ Key = 'ShowPresetTransitionMessage'; Value = '0'; Section = 'OVERLAY' },
        @{ Key = 'NRCostMeter';                 Value = '0'; Section = 'RenoDX.DLSS5' },
        @{ Key = 'UiWelcomed';                  Value = '1'; Section = 'RenoDX.DLSS5' }
    )
    foreach ($item in $recommended) {
        $values = if ($item.Section -eq 'OVERLAY') { Get-IniSection $ini 'OVERLAY' } else { $nr }
        $actual = if ($values.ContainsKey($item.Key)) { $values[$item.Key] } else { $null }
        if ($actual -eq $item.Value) { Add-Result OK 'reshade' "$($item.Key)=$actual (recommended value)" }
        else { Add-Result WARN 'reshade' "$($item.Key)=$actual (recommended $($item.Value)): run installer/install.bat to apply" }
    }
} else {
    Add-Result WARN 'reshade' 'ReShade.ini has no [RenoDX.DLSS5] section (DLSS5 stack not installed yet?)'
}

# 7. ReShade startup banner patch (only meaningful for the known 6.8.0 build)
if (Test-Path -LiteralPath $dxgi) {
    try {
        $bytes = [IO.File]::ReadAllBytes($dxgi)
        if ($bytes.Length -gt 0xD08EA) {
            $slice = ($bytes[0xD08E4..0xD08E9] | ForEach-Object { $_.ToString('X2') }) -join ' '
            if ($slice -eq 'E9 E8 09 00 00 90') { Add-Result OK 'banner' 'ReShade splash patch applied (startup banner stays hidden)' }
            elseif ($slice -eq '0F 84 E7 09 00 00') { Add-Result INFO 'banner' 'ReShade splash not patched: a banner shows while shaders compile (~5 s). tools\Patch-ReShadeSplash.ps1 can remove it' }
            else { Add-Result INFO 'banner' "unrecognised bytes at the splash site ($slice): different ReShade build, patch not applicable" }
        }
    } catch { Add-Result WARN 'banner' "could not read dxgi.dll: $($_.Exception.Message)" }
}

# 8. runtime evidence (optional, from the previous session's logs)
$log = Join-Path $bep 'LogOutput.log'
if (Test-Path -LiteralPath $log) {
    $text = Get-Text $log
    $patchOk = ([regex]::Matches($text, 'patch .*: OK')).Count
    $patchFail = ([regex]::Matches($text, 'patch .*: (FAILED|threw)')).Count
    $exceptions = ([regex]::Matches($text, 'Exception')).Count
    if ($patchFail -eq 0 -and $patchOk -ge 10) { Add-Result OK 'runtime' "all $patchOk Harmony patches installed" }
    elseif ($patchFail -gt 0) { Add-Result FAIL 'runtime' "$patchFail patch(es) failed to install (see BepInEx\LogOutput.log)" }
    else { Add-Result INFO 'runtime' "no patch evidence in BepInEx\LogOutput.log yet (launch the game once)" }
    if ($exceptions -eq 0) { Add-Result OK 'runtime' 'no exceptions in the BepInEx log' } else { Add-Result WARN 'runtime' "$exceptions exception line(s) in the BepInEx log" }
    if ($text -match 'presets ready in registry: (\d+)') { Add-Result OK 'runtime' "custom settings registered: $($Matches[1])" }
    if ($text -match 'L1 nav row injected') { Add-Result OK 'runtime' 'settings row injected into the game menu' }
    if ($text -match 'watchdog: (feed resumed normally|resumed after recovery)') { Add-Result OK 'runtime' 'feed watchdog observed a successful (re)start' }
    if ($text -match 'FEED NOT RESUMED') { Add-Result FAIL 'runtime' 'watchdog reported that feeding never resumed' }
} else {
    Add-Result INFO 'runtime' 'BepInEx\LogOutput.log not found (game never launched with BepInEx)'
}

$feedLog = Join-Path $GameDir 'dlss5-feed.log'
if (Test-Path -LiteralPath $feedLog) {
    $lines = [IO.File]::ReadAllLines($feedLog)
    $activity = @($lines | Where-Object { $_ -match '600 frames|first frame fed|delivered \(' })
    $ready = @($lines | Where-Object { $_ -match 'feature ready' })
    $last = if ($lines.Count -gt 0) { $lines[-1] } else { '' }
    if ($ready.Count -gt 0) { Add-Result OK 'feed' "DLSS feature ready ($($ready.Count) report(s))" ($ready[-1].Trim()) } else { Add-Result WARN 'feed' 'no "feature ready" line yet' }
    if ($activity.Count -ge 2) {
        Add-Result OK 'feed' "feeding active: $($activity.Count) activity reports" ($activity[-1].Trim())
    } elseif ($activity.Count -eq 1) {
        Add-Result WARN 'feed' 'only one activity report: feeding started but did not continue'
    } else {
        Add-Result WARN 'feed' 'no frame-delivery activity in dlss5-feed.log'
    }
    if ($last) { Add-Result INFO 'feed' 'last feeder line' $last.Trim() }
    # switch verdict
    $mode = Get-CfgValue $cfg 'mode'
    if ($mode -eq '2' -and $activity.Count -ge 2) { Add-Result OK 'switch' 'master switch ACTIVE: mode=2 and frames are being fed' }
    elseif ($mode -eq '0') { Add-Result INFO 'switch' 'master switch is OFF (mode=0)' }
    else { Add-Result WARN 'switch' "switch state uncertain (mode=$mode, activity reports=$($activity.Count))" }
} else {
    Add-Result INFO 'feed' 'dlss5-feed.log not found (game not launched with the feeder yet)'
}

# --------------------------------------------------------------------------- report

$ok = @($script:results | Where-Object Status -eq 'OK').Count
$warn = @($script:results | Where-Object Status -eq 'WARN').Count
$fail = @($script:results | Where-Object Status -eq 'FAIL').Count

if ($Json) {
    [pscustomobject]@{ GameDir = $GameDir; Ok = $ok; Warn = $warn; Fail = $fail; Results = $script:results } |
        ConvertTo-Json -Depth 4
} else {
    Write-Host ''
    Write-Host 'NeuroMita.DLSS5 offline verification' -ForegroundColor Cyan
    Write-Host "Game folder: $GameDir"
    Write-Host ('-' * 78)
    foreach ($r in $script:results) {
        $color = switch ($r.Status) { 'OK' { 'Green' } 'WARN' { 'Yellow' } 'FAIL' { 'Red' } default { 'Gray' } }
        $tag = switch ($r.Status) { 'OK' { '[ OK ]' } 'WARN' { '[WARN]' } 'FAIL' { '[FAIL]' } default { '[INFO]' } }
        Write-Host ("{0} {1,-8} {2}" -f $tag, $r.Area, $r.Message) -ForegroundColor $color
        if ($r.Detail) { Write-Host ("              " + $r.Detail) -ForegroundColor DarkGray }
    }
    Write-Host ('-' * 78)
    $verdict = if ($fail -gt 0) { 'FAILED' } elseif ($warn -gt 0) { 'PASSED with warnings' } else { 'PASSED' }
    $vcolor = if ($fail -gt 0) { 'Red' } elseif ($warn -gt 0) { 'Yellow' } else { 'Green' }
    Write-Host ("$ok OK   $warn warnings   $fail failures   ->  $verdict") -ForegroundColor $vcolor
    Write-Host ''
}

if ($fail -gt 0) { exit 1 } else { exit 0 }
