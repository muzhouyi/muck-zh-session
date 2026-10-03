[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Raw | ConvertFrom-Json
$entries = @($data)
foreach ($entry in $entries) {
    if ([IO.Path]::IsPathRooted($entry.Path) -or $entry.Path -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Unsafe manifest path.' }
    $file = Join-Path $PSScriptRoot $entry.Path
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Package file missing or changed: $($entry.Path)" }
}
Write-Output "PASS: $($entries.Count) package files match their SHA256 manifest."
Write-Output 'For source authenticity, compare the ZIP hash with the separately provided .sha256 file.'
