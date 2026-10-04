[CmdletBinding()]
param([string]$GameDir,[switch]$DryRun)
$ErrorActionPreference='Stop'
$core=Join-Path $PSScriptRoot 'Install-MuckSession.ps1'
if (-not (Test-Path $core)) {$core=Join-Path (Split-Path -Parent $PSScriptRoot) 'Install-MuckSession.ps1'}
function Invoke-Install([string]$Directory) {
    $parameters=@{DryRun=$DryRun}
    if ($Directory) {$parameters.GameDir=$Directory}
    $messages=@(& $core @parameters)
    if ($messages -like 'Mode: Update*') { Write-Host '已检测到旧补丁，将更新到 v1.4。存档和个人配置保留。' } else { Write-Host '首次安装 v1.4。' }
    $messages | Where-Object {$_ -like 'Game directory:*' -or $_ -like 'Backup directory:*'} | ForEach-Object {Write-Host $_}
}
Write-Host 'Muck 汉化与联机便利补丁' -ForegroundColor Cyan
Write-Host '请先正常关闭 Muck。安装会备份改动文件，保留存档与无关模组。'
try {
    try {Invoke-Install $GameDir} catch {
        if ($GameDir -or $_.Exception.Message -notlike 'Found * Muck installations.*') {throw}
        Add-Type -AssemblyName System.Windows.Forms
        $picker=New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description='未能自动找到游戏。请选择含有 Muck.exe 的游戏目录（Steam → Muck → 管理 → 浏览本地文件）。'
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {Write-Host '已取消，未安装。';exit 0}
        $GameDir=$picker.SelectedPath
        Invoke-Install $GameDir
    }
    if ($DryRun) {Write-Host '检查通过，预览模式没有修改游戏。' -ForegroundColor Green}
    else {Write-Host '安装完成！请从 Steam 启动游戏。房主和朋友都要安装同一个版本。' -ForegroundColor Green}
} catch {
    Write-Host '安装未完成。' -ForegroundColor Red
    $message=$_.Exception.Message
    if ($message -like 'Close Muck normally*') {Write-Host '请正常退出 Muck 后再运行安装。'}
    elseif ($message -like 'Unsupported Muck build*') {Write-Host '当前游戏版本不受支持。本包适用于 Windows Steam Muck 1.3 / Build 7077400。'}
    elseif ($message -like 'No supported CJK*') {Write-Host '系统缺少中文字体。请先在 Windows 添加中文语言与字体，再重试。'}
    elseif ($message -like 'Package integrity*') {Write-Host '安装文件不完整或已被改动，请重新下载并完整解压安装包。'}
    elseif ($message -like 'SaveUtility*' -or $message -like 'Another copy*') {Write-Host '检测到重复或冲突的模组。请备份旧模组，再按使用说明处理。'}
    else {Write-Host $message}
    Write-Host '打开包内“使用说明.html”查看安装和常见问题。'
    exit 1
}
