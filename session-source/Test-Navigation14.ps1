$ErrorActionPreference='Stop'
function Assert($ok,$label) { if (-not $ok) { throw $label }; $script:checks++ }
$checks=0
$stub='namespace UnityEngine { public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} } }'
$source=$stub+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'revision/ISaveData.cs'))+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'revision/ISaveDataManager.cs'))+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NavigationSave.cs'))+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SaveListInfo.cs'))
Add-Type -TypeDefinition $source
$nav=[MuckSaveGame.NavigationSave]::new(); $nav.EnsureWorld()
foreach ($slot in 1..8) { $nav.SetHome('alice',$slot,[UnityEngine.Vector3]::new($slot,3,4)); $nav.SetHome('bob',$slot,[UnityEngine.Vector3]::new(100+$slot,3,4)) }
$nav.SetHome('alice',9,[UnityEngine.Vector3]::new(9,3,4)); $nav.SetHome('alice',0,[UnityEngine.Vector3]::new(9,3,4))
Assert ($nav.GetHomes('alice').Count -eq 8) 'Home slots must stay within bounds'
$xml=[Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Data')); $nav.SaveXml($xml)
$loaded=[MuckSaveGame.NavigationSave]::new(); $loaded.LoadXml($xml)
foreach ($slot in 1..8) { Assert ($loaded.GetHomes('alice')[$slot].x -eq $slot -and $loaded.GetHomes('bob')[$slot].x -eq 100+$slot) 'Players and numbered homes remain independent' }
$loaded.RemoveHome('alice',1); Assert (-not $loaded.GetHomes('alice').ContainsKey(1)) 'Deleting home1 does not resurrect legacy home'
$loaded.SetHome('alice',2,[UnityEngine.Vector3]::new(999,3,4)); Assert ($loaded.GetHomes('alice')[2].x -eq 999 -and $loaded.GetHomes('bob')[2].x -eq 102) 'Updating a slot preserves other owners'
$legacy=[Xml.Linq.XElement]::Parse('<Data><Home player="alice" x="12" y="3" z="4"/></Data>'); $loaded.LoadXml($legacy)
Assert ($loaded.GetHomes('alice')[1].x -eq 12) 'Old one-home saves migrate to home1'
$loaded.Unload(); $loaded.EnsureWorld(); Assert ($loaded.GetHomes('alice').Count -eq 0) 'A fresh world has no old homes'
$folder=Join-Path $env:TEMP ('muck-metadata-'+[Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $folder | Out-Null
$file=Join-Path $folder 'world.mucksave'; [IO.File]::WriteAllText($file,'<MuckSaveGame><Data><WorldData><CurrentDay>17</CurrentDay></WorldData></Data></MuckSaveGame>')
$info=[MuckSaveGame.SaveListInfo]::Describe($file); Assert ($info -match '存活 17 天' -and $info -match '修改 \d{4}-\d{2}-\d{2}') 'Save metadata shows days and modification timestamp'
[IO.File]::WriteAllText($file,'broken XML'); Assert ([MuckSaveGame.SaveListInfo]::Describe($file) -match '无法读取天数') 'One broken save does not prevent the list opening'
[IO.File]::WriteAllText($file,'<!DOCTYPE test [<!ENTITY external SYSTEM "file:///C:/Windows/win.ini">]><test>&external;</test>'); Assert ([MuckSaveGame.SaveListInfo]::Describe($file) -match '无法读取天数') 'Save metadata rejects external entities'
Write-Output "PASS: $checks numbered home and save metadata checks"
