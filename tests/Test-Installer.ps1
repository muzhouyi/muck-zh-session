
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReferenceGameDir)

$ErrorActionPreference = 'Stop'
$package = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('muck-zh-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$game = Join-Path $fixtureRoot 'Library\steamapps\common\Muck'
New-Item -ItemType Directory -Path "$game\Muck_Data\Managed" -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $ReferenceGameDir 'Muck.exe') -Destination "$game\Muck.exe"
Copy-Item -LiteralPath (Join-Path $ReferenceGameDir 'Muck_Data\Managed\Assembly-CSharp.dll') -Destination "$game\Muck_Data\Managed\Assembly-CSharp.dll"
Set-Content -LiteralPath "$game\Muck_Data\Managed\Unity.TextMeshPro.dll" -Value 'unchanged TMP marker'
Set-Content -LiteralPath "$game\personal-note.txt" -Value 'keep me'
function Get-Process { [CmdletBinding()] param([string]$Name) if ($global:muckPackageTestRunning) { return @([pscustomobject]@{ProcessName='Muck'}) }; return @() }
$fakeSteamRoot = Join-Path $fixtureRoot 'Steam'
$global:muckPackageTestSteamRoot = $fakeSteamRoot
New-Item -ItemType Directory -Path (Join-Path $fakeSteamRoot 'steamapps') -Force | Out-Null
$vdf = '"libraryfolders" { "1" { "path" "' + (Join-Path $fixtureRoot 'Library').Replace('\','\\') + '" } }'
[IO.File]::WriteAllText((Join-Path $fakeSteamRoot 'steamapps\libraryfolders.vdf'), $vdf)
function Get-ItemProperty { [CmdletBinding()] param([string]$LiteralPath) return [pscustomobject]@{SteamPath=$global:muckPackageTestSteamRoot} }
$checks = 0
function Assert-True($Condition, $Message) { if (-not $Condition) {throw $Message}; $script:checks++ }
$exeHash = (Get-FileHash -LiteralPath "$game\Muck.exe").Hash
$gameHash = (Get-FileHash -LiteralPath "$game\Muck_Data\Managed\Assembly-CSharp.dll").Hash
$autodetect = @(& "$package\Install-MuckSession.ps1" -DryRun)
Assert-True ($autodetect -contains "Game directory: $game") 'Steam library auto-detection failed'
$global:muckPackageTestRunning = $true
$blocked = $false
try { & "$package\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'Close Muck normally*' }
Assert-True $blocked 'Running game was not protected'
$global:muckPackageTestRunning = $false
& "$package\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null
Assert-True (-not (Test-Path -LiteralPath "$game\BepInEx")) 'Dry run changed files'
& "$package\Install-MuckSession.ps1" -GameDir $game | Out-Null
$backup1 = Get-ChildItem -LiteralPath "$game\.muck-zh-backups" -Directory | Select-Object -First 1
Assert-True (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll") 'Plugin missing'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck.exe").Hash -eq $exeHash) 'Muck.exe modified'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck_Data\Managed\Assembly-CSharp.dll").Hash -eq $gameHash) 'Game assembly modified'
Assert-True ((Get-Content -LiteralPath "$($backup1.FullName)\installation.json" -Raw | ConvertFrom-Json).Files.Count -eq 35) 'Fresh install should record 35 files'
# Existing loader reuse and restoration of the previous plugin/config.
Add-Content -LiteralPath "$game\BepInEx\config\BepInEx.cfg" -Value '# local preference marker'
$globalHash = (Get-FileHash -LiteralPath "$game\BepInEx\config\BepInEx.cfg").Hash
& "$package\Install-MuckSession.ps1" -GameDir $game | Out-Null
$backup2 = Get-ChildItem -LiteralPath "$game\.muck-zh-backups" -Directory | Where-Object FullName -ne $backup1.FullName | Select-Object -First 1
Assert-True ((Get-FileHash -LiteralPath "$game\BepInEx\config\BepInEx.cfg").Hash -eq $globalHash) 'Existing global config was overwritten'
Assert-True ((Get-Content -LiteralPath "$($backup2.FullName)\installation.json" -Raw | ConvertFrom-Json).Files.Count -eq 14) 'Existing loader should install only 14 plugin and translation files'
& "$package\Restore-MuckChinese.ps1" -BackupDir $backup2.FullName -DryRun | Out-Null
& "$package\Restore-MuckChinese.ps1" -BackupDir $backup2.FullName | Out-Null
Assert-True (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll") 'Second installation restore should preserve original translator'
# User edits block restoration before any file is changed.
$changed = "$game\BepInEx\config\UU9.Muck.Translater\Translation\000-zh-completion.cfg"
$priorBytes = [IO.File]::ReadAllBytes($changed)
Add-Content -LiteralPath $changed -Value 'FriendCustom=custom'
$blocked = $false
try { & "$package\Restore-MuckChinese.ps1" -BackupDir $backup1.FullName | Out-Null } catch { $blocked = $_.Exception.Message -like 'Changed after installation:*' }
Assert-True $blocked 'Restore did not protect user edits'
[IO.File]::WriteAllBytes($changed,$priorBytes)
Copy-Item -LiteralPath "$package\payload\BepInEx\config\BepInEx.cfg" -Destination "$game\BepInEx\config\BepInEx.cfg" -Force
& "$package\Restore-MuckChinese.ps1" -BackupDir $backup1.FullName | Out-Null
Assert-True (-not (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll")) 'Fresh installation files were not removed'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck.exe").Hash -eq $exeHash) 'Restore modified game exe'
Assert-True (Test-Path -LiteralPath "$game\personal-note.txt") 'Restore removed unrelated file'
# Unknown loader is rejected.
Set-Content -LiteralPath "$game\winhttp.dll" -Value 'other mod'
$blocked = $false
try { & "$package\Install-MuckSession.ps1" -GameDir $game | Out-Null } catch { $blocked = $_.Exception.Message -like 'Existing winhttp.dll*' }
Assert-True $blocked 'Unknown loader was overwritten'
Remove-Item -LiteralPath "$game\winhttp.dll"
# Old SaveUtility and another save-mod copy are rejected before mutation.
Set-Content -LiteralPath "$game\BepInEx\plugins\old-save.dll" -Value 'flarfo.saveutility'
$blocked = $false
try { & "$package\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'SaveUtility is incompatible*' }
Assert-True $blocked 'Conflicting save mod was accepted'
Remove-Item -LiteralPath "$game\BepInEx\plugins\old-save.dll"
Set-Content -LiteralPath "$game\BepInEx\plugins\duplicate-save.dll" -Value 'MuckSaveGame.MichMcb'
$blocked = $false
try { & "$package\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'Another copy of MuckSaveGame exists*' }
Assert-True $blocked 'Duplicate save mod was accepted'
Remove-Item -LiteralPath "$game\BepInEx\plugins\duplicate-save.dll"
# Corrupt payload is rejected before backup/copy.
$badPackage = Join-Path $fixtureRoot 'tampered-package'
New-Item -ItemType Directory -Path $badPackage -Force | Out-Null
Copy-Item -Path "$package\*" -Destination $badPackage -Recurse -Force
Add-Content -LiteralPath "$badPackage\payload\BepInEx\plugins\UU9.Muck.Translater.dll" -Value 'corrupted'
$blocked = $false
try { & "$badPackage\Install-MuckSession.ps1" -GameDir $game | Out-Null } catch { $blocked = $_.Exception.Message -like 'Package integrity check failed:*' }
Assert-True $blocked 'Corrupt payload was accepted'
Write-Output "PASS: $checks installer/restore checks. Fixture: $fixtureRoot"
