
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReferenceGameDir, [Parameter(Mandatory=$true)][string]$PackageDir, [string]$PreviousModDll)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDir).ProviderPath
if (-not $PreviousModDll) { $PreviousModDll = Join-Path $package '安装文件\BepInEx\plugins\MuckSaveGame.dll' }
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
$autodetect = @(& "$package\tools\Install-MuckSession.ps1" -DryRun)
Assert-True ($autodetect -contains "Game directory: $game") 'Steam library auto-detection failed'
$global:muckPackageTestRunning = $true
$blocked = $false
try { & "$package\tools\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'Close Muck normally*' }
Assert-True $blocked 'Running game was not protected'
$global:muckPackageTestRunning = $false
& "$package\tools\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null
Assert-True (-not (Test-Path -LiteralPath "$game\BepInEx")) 'Dry run changed files'
& "$package\tools\Install-MuckSession.ps1" -GameDir $game | Out-Null
$backup1 = Get-ChildItem -LiteralPath "$game\.muck-zh-backups" -Directory | Select-Object -First 1
Assert-True (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll") 'Plugin missing'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck.exe").Hash -eq $exeHash) 'Muck.exe modified'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck_Data\Managed\Assembly-CSharp.dll").Hash -eq $gameHash) 'Game assembly modified'
Assert-True ((Get-Content -LiteralPath "$($backup1.FullName)\installation.json" -Raw | ConvertFrom-Json).Files.Count -eq 35) 'Fresh install should record 35 files'
# Existing loader reuse and restoration of the previous plugin/config.
Add-Content -LiteralPath "$game\BepInEx\config\BepInEx.cfg" -Value '# local preference marker'
$globalHash = (Get-FileHash -LiteralPath "$game\BepInEx\config\BepInEx.cfg").Hash
$translatorCfg = "$game\BepInEx\config\UU9.Muck.Translater\UU9.Muck.Translater.cfg"
$originalTranslatorCfg = [IO.File]::ReadAllBytes($translatorCfg)
Add-Content -LiteralPath $translatorCfg -Value '# personal preference marker'
$translatorCfgHash = (Get-FileHash -LiteralPath $translatorCfg).Hash
New-Item -ItemType Directory -Path "$game\Saves" -Force | Out-Null
Set-Content -LiteralPath "$game\Saves\personal.mucksave" -Value 'preserve this save'
$saveHash = (Get-FileHash -LiteralPath "$game\Saves\personal.mucksave").Hash
Copy-Item -LiteralPath $PreviousModDll -Destination "$game\BepInEx\plugins\MuckSaveGame.dll" -Force
$updateMode = @(& "$package\tools\Install-MuckSession.ps1" -GameDir $game -DryRun)
Assert-True ($updateMode -like 'Mode: Update*') 'Existing patch was not recognized as an update'

& "$package\tools\Install-MuckSession.ps1" -GameDir $game | Out-Null
$backup2 = Get-ChildItem -LiteralPath "$game\.muck-zh-backups" -Directory | Where-Object FullName -ne $backup1.FullName | Select-Object -First 1
Assert-True ((Get-FileHash -LiteralPath "$game\BepInEx\config\BepInEx.cfg").Hash -eq $globalHash) 'Existing global config was overwritten'
Assert-True ((Get-Content -LiteralPath "$($backup2.FullName)\installation.json" -Raw | ConvertFrom-Json).Files.Count -eq 13) 'Existing loader should install only 13 plugin and translation files'
& "$package\tools\Restore-MuckChinese.ps1" -BackupDir $backup2.FullName -DryRun | Out-Null
& "$package\tools\Restore-MuckChinese.ps1" -BackupDir $backup2.FullName | Out-Null
Assert-True (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll") 'Second installation restore should preserve original translator'
Assert-True ((Get-FileHash -LiteralPath $translatorCfg).Hash -eq $translatorCfgHash) 'Personal translation preferences were overwritten'
Assert-True ((Get-FileHash -LiteralPath "$game\Saves\personal.mucksave").Hash -eq $saveHash) 'An existing save changed during update'
Assert-True ((Get-FileHash -LiteralPath "$game\BepInEx\plugins\MuckSaveGame.dll").Hash -eq (Get-FileHash -LiteralPath $PreviousModDll).Hash) 'Update restoration did not restore the previous version'
Copy-Item -LiteralPath "$package\安装文件\BepInEx\plugins\MuckSaveGame.dll" -Destination "$game\BepInEx\plugins\MuckSaveGame.dll" -Force
[IO.File]::WriteAllBytes($translatorCfg,$originalTranslatorCfg)
# User edits block restoration before any file is changed.
$changed = "$game\BepInEx\config\UU9.Muck.Translater\Translation\000-zh-completion.cfg"
$priorBytes = [IO.File]::ReadAllBytes($changed)
Add-Content -LiteralPath $changed -Value 'FriendCustom=custom'
$blocked = $false
try { & "$package\tools\Restore-MuckChinese.ps1" -BackupDir $backup1.FullName | Out-Null } catch { $blocked = $_.Exception.Message -like 'Changed after installation:*' }
Assert-True $blocked 'Restore did not protect user edits'
[IO.File]::WriteAllBytes($changed,$priorBytes)
Copy-Item -LiteralPath "$package\安装文件\BepInEx\config\BepInEx.cfg" -Destination "$game\BepInEx\config\BepInEx.cfg" -Force
& "$package\tools\Restore-MuckChinese.ps1" -BackupDir $backup1.FullName | Out-Null
Assert-True (-not (Test-Path -LiteralPath "$game\BepInEx\plugins\UU9.Muck.Translater.dll")) 'Fresh installation files were not removed'
Assert-True ((Get-FileHash -LiteralPath "$game\Muck.exe").Hash -eq $exeHash) 'Restore modified game exe'
Assert-True (Test-Path -LiteralPath "$game\personal-note.txt") 'Restore removed unrelated file'
# Unknown loader is rejected.
Set-Content -LiteralPath "$game\winhttp.dll" -Value 'other mod'
$blocked = $false
try { & "$package\tools\Install-MuckSession.ps1" -GameDir $game | Out-Null } catch { $blocked = $_.Exception.Message -like 'Existing winhttp.dll*' }
Assert-True $blocked 'Unknown loader was overwritten'
Remove-Item -LiteralPath "$game\winhttp.dll"
# Old SaveUtility and another save-mod copy are rejected before mutation.
Set-Content -LiteralPath "$game\BepInEx\plugins\old-save.dll" -Value 'flarfo.saveutility'
$blocked = $false
try { & "$package\tools\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'SaveUtility is incompatible*' }
Assert-True $blocked 'Conflicting save mod was accepted'
Remove-Item -LiteralPath "$game\BepInEx\plugins\old-save.dll"
Set-Content -LiteralPath "$game\BepInEx\plugins\duplicate-save.dll" -Value 'MuckSaveGame.MichMcb'
$blocked = $false
try { & "$package\tools\Install-MuckSession.ps1" -GameDir $game -DryRun | Out-Null } catch { $blocked = $_.Exception.Message -like 'Another copy of MuckSaveGame exists*' }
Assert-True $blocked 'Duplicate save mod was accepted'
Remove-Item -LiteralPath "$game\BepInEx\plugins\duplicate-save.dll"
# Corrupt payload is rejected before backup/copy.
$badPackage = Join-Path $fixtureRoot 'tampered-package'
New-Item -ItemType Directory -Path $badPackage -Force | Out-Null
Copy-Item -Path "$package\*" -Destination $badPackage -Recurse -Force
Add-Content -LiteralPath "$badPackage\安装文件\BepInEx\plugins\UU9.Muck.Translater.dll" -Value 'corrupted'
$blocked = $false
try { & "$badPackage\tools\Install-MuckSession.ps1" -GameDir $game | Out-Null } catch { $blocked = $_.Exception.Message -like 'Package integrity check failed:*' }
Assert-True $blocked 'Corrupt payload was accepted'
Write-Output "PASS: $checks installer/restore checks. Fixture: $fixtureRoot"
