[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BackupDir, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Muck -ErrorAction SilentlyContinue) { throw 'Close Muck normally before restoring.' }
$BackupDir = (Resolve-Path -LiteralPath $BackupDir).ProviderPath.TrimEnd('\')
$stateFile = Join-Path $BackupDir 'installation.json'
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
if ($state.RolledBack -or $state.Restored) { throw 'This installation has already been rolled back/restored.' }
if (-not $state.Completed) { throw 'Installation did not finish. Please inspect the transaction before restoring.' }
$gameRoot = (Resolve-Path -LiteralPath $state.GameDir).ProviderPath.TrimEnd('\')
$expectedBackupParent = [IO.Path]::GetFullPath((Join-Path $gameRoot '.muck-zh-backups')).TrimEnd('\') + '\'
$expectedSessionParent = [IO.Path]::GetFullPath((Join-Path $gameRoot '.muck-session-backups')).TrimEnd('\') + '\'
if ((-not $BackupDir.StartsWith($expectedBackupParent, [StringComparison]::OrdinalIgnoreCase) -and -not $BackupDir.StartsWith($expectedSessionParent, [StringComparison]::OrdinalIgnoreCase)) -or -not (Test-Path -LiteralPath (Join-Path $gameRoot 'Muck.exe'))) { throw 'Backup directory does not belong to the recorded Muck installation.' }
function Get-SafePath {
    param([string]$Root, [string]$Relative)
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Unsafe backup path.' }
    if ($Relative -notmatch '^(BepInEx[\\/]|winhttp\.dll$|doorstop_config\.ini$)') { throw 'Backup entry outside the package scope.' }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup path escaped its root.' }
    return $path
}
$files = @($state.Files)
# Preflight all entries before changing anything; user edits are never overwritten silently.
foreach ($entry in $files) {
    $target = Get-SafePath $gameRoot $entry.Path
    if ((Test-Path -LiteralPath $target -PathType Leaf) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.InstalledSHA256) { throw "Changed after installation: $($entry.Path). Back up or reconcile the edit before restoring." }
    if ($entry.PreviouslyExisted) {
        $saved = Get-SafePath (Join-Path $BackupDir 'original') $entry.Path
        if (-not (Test-Path -LiteralPath $saved -PathType Leaf) -or (Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $entry.OriginalSHA256) { throw "Original backup is missing or changed: $($entry.Path)" }
    }
}
Write-Output "Restore $($files.Count) package files in $gameRoot"
if ($DryRun) { $files.Path; Write-Output 'Dry run complete. No files changed.'; return }
for ($i = $files.Count - 1; $i -ge 0; $i--) {
    $entry = $files[$i]
    $target = Get-SafePath $gameRoot $entry.Path
    if ($entry.PreviouslyExisted) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Get-SafePath (Join-Path $BackupDir 'original') $entry.Path) -Destination $target -Force
    } elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
}
$state | Add-Member -NotePropertyName Restored -NotePropertyValue $true -Force
$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stateFile -Encoding UTF8
Write-Output 'Restored the pre-installation files. Empty directories and the backup were kept.'
