[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameDir, [string]$CompilerAssemblyDir = $PSHOME)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'Muck_Data\Managed'
$loaderCore = Join-Path (Split-Path -Parent $PSScriptRoot) 'payload\BepInEx\core'
foreach ($dll in @('Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll')) {
    $path = Join-Path $CompilerAssemblyDir $dll
    if (-not (Test-Path -LiteralPath $path)) { throw 'Rebuilding requires PowerShell 7 with bundled Roslyn, or -CompilerAssemblyDir pointing to compatible Roslyn assemblies. Rebuilding is not needed to install.' }
    Add-Type -Path $path
}
$syntax = [Microsoft.CodeAnalysis.SyntaxTree[]]@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName)) })
$refPaths = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.TextCoreModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','Unity.TextMeshPro.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll') | ForEach-Object { Join-Path $managed $_ }
$refPaths += @((Join-Path $loaderCore 'BepInEx.dll'), (Join-Path $loaderCore '0Harmony.dll'))
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@($refPaths | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('UU9.Muck.Translater', $syntax, $references, $options)
$outputPath = Join-Path $PSScriptRoot 'UU9.Muck.Translater.rebuilt.dll'
$stream = [IO.File]::Create($outputPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object { $_.ToString() }
if (-not $result.Success) { throw 'Compilation failed.' }
Write-Output "Rebuilt DLL: $outputPath"
Write-Output 'The packaged payload was not changed. Re-run regression tests and update the payload manifest before redistributing a new build.'
