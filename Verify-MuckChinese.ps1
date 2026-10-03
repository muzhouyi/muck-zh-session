[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).ProviderPath
$plugin = Join-Path $GameDir 'BepInEx\plugins\UU9.Muck.Translater.dll'
$manifestData = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
$expected = @($manifestData) | Where-Object Path -eq 'BepInEx\plugins\UU9.Muck.Translater.dll'
if (-not (Test-Path -LiteralPath $plugin) -or (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash -ne $expected.SHA256) { throw 'Installed plugin differs from this package.' }
$dictionaryRoot = Join-Path $GameDir 'BepInEx\config\UU9.Muck.Translater\Translation'
$keys = @{}
foreach ($file in Get-ChildItem -LiteralPath $dictionaryRoot -Filter '*.cfg' -File -Recurse | Sort-Object FullName) {
    foreach ($line in [IO.File]::ReadAllLines($file.FullName, [Text.Encoding]::UTF8)) {
        if ($line.StartsWith('#') -or $line.StartsWith('//')) { continue }
        $index = $line.IndexOf('=')
        if ($index -gt 0) {
            $key = $line.Substring(0, $index).Trim()
            if (-not $keys.ContainsKey($key)) { $keys[$key] = $line.Substring($index + 1).Trim() }
        }
    }
}
if (-not $keys.ContainsKey('Put rock on hotbar') -or -not $keys.ContainsKey('Can revive {0} in {1} seconds')) { throw 'Supplemental tutorial/dynamic translations were not installed.' }
Write-Output "Dictionary entries: $($keys.Count) (base package: 757)."
$logFile = Join-Path $GameDir 'BepInEx\LogOutput.log'
if (-not (Test-Path -LiteralPath $logFile)) { throw 'No BepInEx log yet. Start Muck once, then verify again.' }
$log = [IO.File]::ReadAllText($logFile)
if ($log -notmatch 'Loading \[UU9 Muck Translater 1\.0\.2\]' -or $log -notmatch 'Chainloader startup complete') { throw 'Current log does not confirm translator 1.0.2 startup.' }
$errors = @($log -split '\r?\n' | Where-Object { $_ -match '\[Error\s*:|\[Fatal\s*:' })
if ($errors.Count) { $errors; throw 'Game log contains errors. Ask your agent to inspect them.' }
Write-Output "Startup confirmed. Log timestamp: $((Get-Item -LiteralPath $logFile).LastWriteTime.ToString('s'))"
Write-Output 'The three Harmony GetTextElement signature warnings also occur on the verified source machine; startup succeeds via a fallback method.'
Write-Output 'Visual check still required: menu, six tutorial steps, item tooltips, trading, revive countdown, and F6 English/Chinese toggle.'
