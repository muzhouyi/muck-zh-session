$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ContainerTrade.cs')
$checks=0
function Assert($ok,$message){if(-not $ok){throw $message};$script:checks++}
function Quantity($stack,$id){if($stack.Id -eq $id){return $stack.Amount};return 0}
foreach($slotId in @(-1,1,2)){foreach($mouseId in @(-1,1,2)){foreach($a in @(0,1,2,5,12)){foreach($b in @(0,1,2,5,12)){foreach($right in @($false,$true)){
 $slot=[MuckSaveGame.ItemStack]::new($slotId,$a);$mouse=[MuckSaveGame.ItemStack]::new($mouseId,$b)
 $r=[MuckSaveGame.ContainerTrade]::Click($slot,$mouse,$right,$true,12)
 foreach($id in @(1,2)) {Assert ((Quantity $slot $id)+(Quantity $mouse $id) -eq (Quantity $r.Slot $id)+(Quantity $r.Mouse $id)) 'Transfers must conserve each item type'}
 Assert ($r.Slot.Amount -ge 0 -and $r.Slot.Amount -le 12 -and $r.Mouse.Amount -ge 0 -and $r.Mouse.Amount -le 12) 'No negative or overflowing stacks'
}}}}}
$r=[MuckSaveGame.ContainerTrade]::Click([MuckSaveGame.ItemStack]::new(1,5),[MuckSaveGame.ItemStack]::new(-1,0),$true,$true,12)
Assert ($r.Mouse.Amount -eq 3 -and $r.Slot.Amount -eq 2) 'Right click takes the rounded-up half'
$r=[MuckSaveGame.ContainerTrade]::Click([MuckSaveGame.ItemStack]::new(1,12),[MuckSaveGame.ItemStack]::new(1,2),$false,$true,12)
Assert ($r.Mouse.Amount -eq 2 -and $r.Slot.Amount -eq 12) 'A full stack accepts nothing'
# Test the same ledger used by the host: the second delivery cannot execute its mutation.
$ledger=[MuckSaveGame.ReceiptBook[int]]::new();$script:balance=10
$operation=[Func[int]] { $script:balance--; return $script:balance }
$first=$ledger.RunOnce('alice/request-1',$operation);$repeat=$ledger.RunOnce('alice/request-1',$operation)
Assert ($first -eq 9 -and $repeat -eq 9 -and $script:balance -eq 9) 'Repeated packet executes once'
$second=$ledger.RunOnce('bob/request-1',$operation)
Assert ($second -eq 8 -and $script:balance -eq 8) 'Different players have distinct request scopes'
$ledger.Clear();$new=$ledger.RunOnce('alice/request-1',$operation)
Assert ($new -eq 7) 'New sessions reset receipts'
# Two consumers target one item. After the first transfer, the second expected snapshot is stale.
$actual=[MuckSaveGame.ItemStack]::new(1,1);$expected=$actual
$winner=[MuckSaveGame.ContainerTrade]::Click($actual,[MuckSaveGame.ItemStack]::new(-1,0),$false,$true,12);$actual=$winner.Slot
Assert (-not $actual.Equals($expected)) 'The losing consumer must fail the host snapshot comparison'
Assert ($winner.Mouse.Amount -eq 1 -and $actual.Empty) 'Only one copy is transferred'
# Navigation save code uses only coordinates, not Unity runtime services.
$stub='namespace UnityEngine { public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} } }'
$source=$stub+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'revision/ISaveData.cs'))+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'revision/ISaveDataManager.cs'))+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NavigationSave.cs'))
Add-Type -TypeDefinition $source
$nav=[MuckSaveGame.NavigationSave]::new();$nav.EnsureWorld();$world=$nav.RoomId
$nav.Homes['alice']=[UnityEngine.Vector3]::new(123.5,8,-46.25)
$xml=[Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('Data'));$nav.SaveXml($xml)
$loaded=[MuckSaveGame.NavigationSave]::new();$loaded.LoadXml($xml);$loaded.ApplyLoadedData()
Assert ($loaded.RoomId -eq $world -and $loaded.Homes['alice'].x -eq 123.5 -and $loaded.Homes['alice'].z -eq -46.25) 'Home and unique world id survive saving/loading'
$loaded.Unload();$loaded.EnsureWorld()
Assert ($loaded.RoomId -ne $world -and $loaded.Homes.Count -eq 0) 'A fresh world never inherits homes, even with the same map seed'
$bad=[Xml.Linq.XElement]::Parse('<Data world="bad"><Home player="alice" x="NaN" y="0" z="0"/></Data>')
$loaded.LoadXml($bad);$loaded.ApplyLoadedData()
Assert ($loaded.Homes.Count -eq 0 -and $loaded.RoomId.Length -eq 32) 'Invalid markers and invalid world ids are handled safely'
"PASS: $checks navigation and container checks"

