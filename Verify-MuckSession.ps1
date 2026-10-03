param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
$manifestData=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
foreach($entry in @($manifestData)|Where-Object {$_.Path -in @('BepInEx\plugins\MuckSaveGame.dll','BepInEx\plugins\UU9.Muck.Translater.dll','BepInEx\config\UU9.Muck.Translater\Translation\001-zh-session.cfg')}) {
    $target=Join-Path $GameDir $entry.Path
    if(-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne $entry.SHA256){throw "Installed file mismatch: $($entry.Path)"}
}
$logPath=Join-Path $GameDir 'BepInEx\LogOutput.log'
if(-not(Test-Path -LiteralPath $logPath)){throw 'Start Muck once to produce the startup log.'}
$log=[IO.File]::ReadAllText($logPath)
if($log -notmatch 'Loaded MuckSaveGame 0\.9\.2 session edition!' -or $log -notmatch 'Loading \[UU9 Muck Translater 1\.0\.2\]' -or $log -notmatch 'Chainloader startup complete'){throw 'Expected mod startup not confirmed in the current log.'}
$errors=@($log -split '\r?\n'|Where-Object {$_ -match '\[(Error|Fatal)\s*:(MuckSaveGame|UU9 Muck)'})
if($errors.Count){$errors;throw 'A mod error is present; inspect before continuing.'}
Write-Output 'Installed DLL hashes and startup verified. Actual two-computer coop still needs the checklist in README.'
