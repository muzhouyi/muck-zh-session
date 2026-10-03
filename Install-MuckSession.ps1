[CmdletBinding()]
param([string]$GameDir, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$payloadRoot = Join-Path $packageRoot 'payload'

function Resolve-MuckDirectory {
    param([string]$Requested)
    if ($Requested) { return (Resolve-Path -LiteralPath $Requested).ProviderPath.TrimEnd('\') }
    $steamRoots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $value = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($value.SteamPath) { $steamRoots += $value.SteamPath }
        if ($value.InstallPath) { $steamRoots += $value.InstallPath }
    }
    $libraries = @($steamRoots)
    foreach ($root in $steamRoots) {
        $file = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $file) {
            $vdf = [IO.File]::ReadAllText($file)
            foreach ($match in [regex]::Matches($vdf, '"path"\s*"([^"]+)"')) {
                $libraries += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    $candidates = @($libraries | Select-Object -Unique | ForEach-Object {
        $candidate = Join-Path $_ 'steamapps\common\Muck'
        if (Test-Path -LiteralPath (Join-Path $candidate 'Muck.exe')) {
            (Resolve-Path -LiteralPath $candidate).ProviderPath.TrimEnd('\')
        }
    } | Select-Object -Unique)
    if ($candidates.Count -ne 1) {
        throw "Found $($candidates.Count) Muck installations. Supply -GameDir with the folder containing Muck.exe."
    }
    return $candidates[0]
}

function Get-SafePath {
    param([string]$Root, [string]$Relative)
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "Unsafe package path: $Relative"
    }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped its root.' }
    return $full
}

$GameDir = Resolve-MuckDirectory $GameDir
foreach ($relative in @('Muck.exe', 'Muck_Data\Managed\Assembly-CSharp.dll', 'Muck_Data\Managed\Unity.TextMeshPro.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $GameDir $relative) -PathType Leaf)) { throw "Not a supported Muck Mono installation: missing $relative" }
}
if (Get-Process -Name Muck -ErrorAction SilentlyContinue) { throw 'Close Muck normally before installing. No game process will be terminated.' }
$exeBytes = [IO.File]::ReadAllBytes((Join-Path $GameDir 'Muck.exe'))
if ($exeBytes.Length -lt 64) { throw 'Invalid Muck.exe.' }
$peOffset = [BitConverter]::ToInt32($exeBytes, 60)
if ($peOffset -lt 0 -or $peOffset + 6 -gt $exeBytes.Length -or [BitConverter]::ToUInt16($exeBytes, $peOffset + 4) -ne 0x8664) { throw 'This package requires Windows x64 Muck.' }
$fontFolder = [Environment]::GetFolderPath('Fonts')
$fontFile = @('msyh.ttc', 'simhei.ttf', 'simsun.ttc') | Where-Object { Test-Path -LiteralPath (Join-Path $fontFolder $_) } | Select-Object -First 1
if (-not $fontFile) { throw 'No supported CJK system font found. Ask your agent to provide a Chinese font and configure FontFile before manual installation.' }

$existingCore = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
$reuseLoader = Test-Path -LiteralPath $existingCore
if ($reuseLoader) {
    $version = [Reflection.AssemblyName]::GetAssemblyName($existingCore).Version
    if ($version.Major -ne 5) { throw "Existing BepInEx $version is incompatible. Do not overwrite it automatically." }
    $doorstopFile = Join-Path $GameDir 'doorstop_config.ini'
    if (-not (Test-Path -LiteralPath $doorstopFile)) { throw 'Existing loader has no doorstop_config.ini. Ask your agent to inspect the mod setup.' }
    $doorstop = [IO.File]::ReadAllText($doorstopFile)
    if ($doorstop -notmatch '(?im)^\s*enabled\s*=\s*true\s*$' -or $doorstop -notmatch '(?im)^\s*targetAssembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Preloader\.dll\s*$') {
        throw 'Existing Doorstop configuration differs from the supported setup. Ask your agent to inspect it.'
    }
    if (-not ((Test-Path -LiteralPath (Join-Path $GameDir 'winhttp.dll')) -or (Test-Path -LiteralPath (Join-Path $GameDir 'version.dll')))) { throw 'Existing BepInEx has no known Windows loader hook.' }
    Write-Output "Reusing existing BepInEx $version; core files and BepInEx.cfg will be preserved."
} else {
    foreach ($relative in @('winhttp.dll', 'version.dll', 'doorstop_config.ini')) {
        if (Test-Path -LiteralPath (Join-Path $GameDir $relative)) { throw "Existing $relative may belong to another mod. Inspect it before installation." }
    }
}
# Session mod is tied to the verified game build; do not install against unknown APIs.
$expectedGameHash = '5F9D0DC1E0E72F5493013C92D05ED9FB8628019F54F8BC02162DAD0D2F20810C'
if ((Get-FileHash -LiteralPath (Join-Path $GameDir 'Muck_Data\Managed\Assembly-CSharp.dll')).Hash -ne $expectedGameHash) { throw 'Unsupported Muck build. Ask your agent to check compatibility before installing.' }
$sessionDestination = Join-Path $GameDir 'BepInEx\plugins\MuckSaveGame.dll'
foreach ($dll in Get-ChildItem -LiteralPath (Join-Path $GameDir 'BepInEx\plugins') -Filter '*.dll' -File -Recurse -ErrorAction SilentlyContinue) {
    $metadata = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dll.FullName))
    if ($metadata.Contains('MuckSaveGame.MichMcb')) {
        if ($dll.FullName -ne $sessionDestination) { throw 'Another copy of MuckSaveGame exists. Reconcile it before installing.' }
    } elseif ($metadata.Contains('flarfo.saveutility')) { throw 'SaveUtility is incompatible. Back it up and disable it before installing this package.' }
}
$duplicates = @(Get-ChildItem -LiteralPath (Join-Path $GameDir 'BepInEx\plugins') -Filter 'UU9.Muck.Translater.dll' -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne (Join-Path $GameDir 'BepInEx\plugins\UU9.Muck.Translater.dll') })
if ($duplicates.Count) { throw 'Another copy of the translator exists in a plugin subfolder. Remove the duplicate setup before installing.' }

$manifestData = Get-Content -LiteralPath (Join-Path $packageRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
$manifest = @($manifestData)
foreach ($entry in $manifest) {
    $source = Get-SafePath $payloadRoot $entry.Path
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Package integrity check failed: $($entry.Path)" }
}
$plan = @($manifest | Where-Object { -not $reuseLoader -or $_.Path -like 'BepInEx\plugins\*' -or $_.Path -like 'BepInEx\config\UU9.Muck.Translater\*' })
Write-Output "Game directory: $GameDir"
Write-Output "Chinese font: $fontFile"
Write-Output "Files to install/update: $($plan.Count)"
if ($DryRun) { $plan.Path; Write-Output 'Dry run complete. No files changed.'; return }

$backupRoot = Join-Path $GameDir ('.muck-zh-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$records = @()
$history = [ordered]@{ PackageVersion = '0.9.2-session-with-zh-1.0.2'; GameDir = $GameDir; InstalledAt = (Get-Date).ToString('o'); Completed = $false; Files = @() }
$stateFile = Join-Path $backupRoot 'installation.json'
try {
    foreach ($entry in $plan) {
        $source = Get-SafePath $payloadRoot $entry.Path
        $target = Get-SafePath $GameDir $entry.Path
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        $record = [ordered]@{ Path = $entry.Path; PreviouslyExisted = $existed; OriginalSHA256 = $null; InstalledSHA256 = $null }
        if ($existed) {
            $saved = Get-SafePath (Join-Path $backupRoot 'original') $entry.Path
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved -Force
            $record.OriginalSHA256 = (Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash
        }
        $records += $record
        $history.Files = $records
        $history | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFile -Encoding UTF8
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        if ($entry.Path -eq 'BepInEx\config\UU9.Muck.Translater\UU9.Muck.Translater.cfg') {
            $content = [IO.File]::ReadAllText($source) -replace '(?m)^FontFile\s*=.*$', "FontFile = $fontFile"
            [IO.File]::WriteAllText($target, $content, [Text.UTF8Encoding]::new($false))
        } else { Copy-Item -LiteralPath $source -Destination $target -Force }
        $record.InstalledSHA256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        $history | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }
    $history.Completed = $true
    $history | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFile -Encoding UTF8
} catch {
    $failure = $_
    for ($i = $records.Count - 1; $i -ge 0; $i--) {
        $record = $records[$i]
        $target = Get-SafePath $GameDir $record.Path
        if ($record.PreviouslyExisted) { Copy-Item -LiteralPath (Get-SafePath (Join-Path $backupRoot 'original') $record.Path) -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
    $history['RolledBack'] = $true
    $history | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    throw $failure
}
Write-Output 'Installed Chinese 1.0.2 and personal session edition 0.9.2. Both players need this exact package. Start Muck through Steam, then run Verify-MuckSession.ps1.'
Write-Output "Backup directory: $backupRoot"
Write-Output 'Host F7 saves; host F8 pauses/resumes the group. F5 reloads Chinese; F6 toggles language. Restart after installation.'
