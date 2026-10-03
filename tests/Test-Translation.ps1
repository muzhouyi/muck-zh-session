[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 for these source-level regression checks. Installation itself supports Windows PowerShell 5.1.' }
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $root 'source\TranslationManager.cs'), (Join-Path $PSScriptRoot 'TranslationTests.cs')
[TranslationRegression]::Run((Join-Path $root 'payload\BepInEx\config'))
