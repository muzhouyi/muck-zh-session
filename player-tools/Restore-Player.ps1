$ErrorActionPreference='Stop'
try {
    Add-Type -AssemblyName System.Windows.Forms
    Write-Host '请先退出游戏。选择游戏 .muck-zh-backups 文件夹内的一次安装备份目录。'
    $picker=New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description='选择含 installation.json 的安装备份子目录'
    if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {Write-Host '已取消。';exit 0}
    $core=Join-Path $PSScriptRoot 'Restore-MuckChinese.ps1'
    if (-not (Test-Path $core)) {$core=Join-Path (Split-Path -Parent $PSScriptRoot) 'Restore-MuckChinese.ps1'}
    & $core -BackupDir $picker.SelectedPath
    Write-Host '恢复完成。Saves 存档和无关模组保留。' -ForegroundColor Green
} catch {Write-Host '恢复未完成：' -ForegroundColor Red;Write-Host $_.Exception.Message;exit 1}
