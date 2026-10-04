param([Parameter(Mandatory=$true)][string]$GameDir, [string]$RoslynDir = $PSHOME, [switch]$Smoke, [string]$SmokeSource = 'SessionSmoke.cs')
$ErrorActionPreference = 'Stop'
$taskRoot = Join-Path $PSScriptRoot 'revision'
$managed = Join-Path $GameDir 'Muck_Data\Managed'
$loaderCore = Join-Path $GameDir 'BepInEx\core'
Add-Type -Path (Join-Path $RoslynDir 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $RoslynDir 'Microsoft.CodeAnalysis.CSharp.dll')
$syntax = [Microsoft.CodeAnalysis.SyntaxTree[]]@(Get-ChildItem -LiteralPath $taskRoot -Filter '*.cs' -Recurse | ForEach-Object { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName)) })
if ($Smoke) { $syntax = [Microsoft.CodeAnalysis.SyntaxTree[]]@([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot $SmokeSource)))) }
$refPaths = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object FullName)
$refPaths += @("$loaderCore\BepInEx.dll", "$loaderCore\0Harmony.dll")
if ($Smoke) { $refPaths += Join-Path $taskRoot 'MuckSaveGame.dll' }
$references = [Microsoft.CodeAnalysis.MetadataReference[]]@($refPaths | ForEach-Object {[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_)})
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithAllowUnsafe($true)
$assemblyName = if ($Smoke) {'SessionSmoke'} else {'MuckSaveGame'}
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($assemblyName, $syntax, $references, $options)
$resourcePath = Join-Path $taskRoot 'MuckSaveGameAssets'
$provider = [Func[IO.Stream]] { [IO.File]::OpenRead($resourcePath) }.GetNewClosure()
$resources = [Microsoft.CodeAnalysis.ResourceDescription[]]@([Microsoft.CodeAnalysis.ResourceDescription]::new('MuckSaveGame.MuckSaveGameAssets', $provider, $true))
if ($Smoke) { $resources = [Microsoft.CodeAnalysis.ResourceDescription[]]@() }
$outputPath = if ($Smoke) {Join-Path $PSScriptRoot 'SessionSmoke.dll'} else {Join-Path $taskRoot 'MuckSaveGame.dll'}
$output = [IO.File]::Create($outputPath)
try { $result = $compilation.Emit($output, $null, $null, $null, $resources) } finally { $output.Dispose() }
$result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object {$_.ToString()}
if (-not $result.Success) {throw 'Compilation failed'}
Get-Item -LiteralPath $outputPath | Select-Object Name,Length
