$ErrorActionPreference = 'Stop'
Add-Type -Path @((Join-Path $PSScriptRoot 'AtomicSaveFile.cs'), (Join-Path $PSScriptRoot 'SnapshotGate.cs'), (Join-Path $PSScriptRoot 'LobbySyncState.cs'), (Join-Path $PSScriptRoot 'PeerProtocolRegistry.cs'), (Join-Path $PSScriptRoot 'NameplatePlacement.cs'))
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('muck-session-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$script:count = 0
function Assert($ok, $message) { if (-not $ok) { throw $message }; $script:count++ }
$gate = [MuckSaveGame.SnapshotGate]::new()
$peers = [Collections.Generic.Dictionary[int,string]]::new()
$peers.Add(1, 'peer-one'); $peers.Add(2, 'peer-two')
$gate.Begin($peers)
Assert (-not $gate.Complete) 'Incomplete snapshot must wait'
Assert ($gate.Accept(1, 'peer-one', $gate.Generation)) 'Expected peer accepted'
Assert (-not $gate.Accept(1, 'peer-two', $gate.Generation)) 'Spoofed identity rejected'
Assert (-not $gate.Accept(3, 'peer-three', $gate.Generation)) 'Unknown peer rejected'
Assert (-not $gate.Accept(1, 'peer-one', $gate.Generation-1)) 'Delayed packets from older saves rejected'
foreach ($bit in @(1,2,4,8,16)) {$gate.Mark(1,$bit)}
Assert (-not $gate.Complete) 'One missing peer must prevent writing'
foreach ($bit in @(1,2,4,8)) {$gate.Mark(2,$bit)}
Assert (-not $gate.Complete) 'Missing armor must prevent writing'
$gate.Mark(2,16)
Assert $gate.Complete 'All five data parts from both peers complete'
$gate.Begin($peers)
Assert (-not $gate.Complete) 'Old receipt state cleared on next save'
$gate.Clear()
Assert $gate.Complete 'Solo save requires no peer packets'
$sync = [MuckSaveGame.LobbySyncState]::new()
$script:published = 0
$publish = [Action] { $script:published++ }
$sync.PublishIfDue(0, 0, $publish)
Assert ($script:published -eq 0) 'Do not publish before joining a lobby'
# No host InitLobby event is invoked: this is the guest join path that used to fail.
$sync.PublishIfDue(100, 10, $publish)
Assert ($script:published -eq 1) 'Guest join publishes without a host-only callback'
$sync.PublishIfDue(100, 11, $publish)
Assert ($script:published -eq 1) 'Avoid publishing every frame'
$sync.PublishIfDue(100, 12, $publish)
Assert ($script:published -eq 2) 'Republish to recover from early metadata synchronization'
$sync.PublishIfDue(200, 12.5, $publish)
Assert ($script:published -eq 3) 'Rejoining a different lobby publishes immediately'
$sync.PublishIfDue(0, 13, $publish)
$sync.PublishIfDue(200, 13.1, $publish)
Assert ($script:published -eq 4) 'Leaving and rejoining the same lobby publishes immediately'
Assert ($sync.ShouldNotify('missing-peer', 1)) 'First missing-marker notice appears'
Assert (-not $sync.ShouldNotify('missing-peer', 2)) 'Repeated key presses do not spam chat'
Assert ($sync.ShouldNotify('different-peer', 3)) 'A changed failure is explained immediately'
Assert ($sync.ShouldNotify('different-peer', 33)) 'Notice can repeat after the cooldown'
$sync.ClearNotice()
Assert ($sync.ShouldNotify('different-peer', 34)) 'Successful compatibility clears notice suppression'
$sync.Reset()
$sync.PublishIfDue(200, 34, $publish)
Assert ($script:published -eq 5) 'Scene/session reset allows immediate publication'
$registry=[MuckSaveGame.PeerProtocolRegistry]::new()
$connection=[object]::new()
$reconnected=[object]::new()
Assert (-not $registry.Verified(1,42,$connection,'0.9.2')) 'Unconfirmed peer is not trusted'
$registry.Confirm(1,42,$connection,'0.9.2')
Assert ($registry.Verified(1,42,$connection,'0.9.2')) 'Connection confirmed from the peer handshake'
# Lobby data is now absent. This same connection must retain its previously verified protocol.
Assert ($registry.Verified(1,42,$connection,'0.9.2')) 'Loss of lobby metadata does not revoke a live verified connection'
Assert (-not $registry.Verified(1,99,$connection,'0.9.2')) 'Another Steam account cannot inherit a peer proof'
Assert (-not $registry.Verified(1,42,$reconnected,'0.9.2')) 'A reconnect must confirm again'
Assert (-not $registry.Verified(2,42,$connection,'0.9.2')) 'Another client slot cannot inherit the proof'
Assert (-not $registry.Verified(1,42,$connection,'0.9.1')) 'Incompatible wire version stays blocked'
$registry.Confirm(1,42,$reconnected,'0.9.2')
Assert ($registry.Verified(1,42,$reconnected,'0.9.2')) 'Reconnect can confirm a fresh proof'
Assert (-not $registry.Verified(1,42,$connection,'0.9.2')) 'Old connection proof cannot be reused after reconnect'
$hello=[MuckSaveGame.PeerProtocolRegistry]::Hello('0.9.2')
Assert ([MuckSaveGame.PeerProtocolRegistry]::ParseHello($hello) -eq '0.9.2') 'Handshake bytes roundtrip'
Assert ($null -eq [MuckSaveGame.PeerProtocolRegistry]::ParseHello([Text.Encoding]::ASCII.GetBytes('other-packet'))) 'Unrelated packets are ignored'
Assert ($null -eq [MuckSaveGame.PeerProtocolRegistry]::ParseHello([Text.Encoding]::ASCII.GetBytes('MuckSession/1/0.9.2x'))) 'Malformed version strings are ignored'
Assert ($null -eq [MuckSaveGame.PeerProtocolRegistry]::ParseHello([byte[]]::new(100))) 'Oversized packets are ignored'
$registry.Clear()
Assert (-not $registry.Verified(1,42,$reconnected,'0.9.2')) 'Returning to menu clears peer proofs'
$point=[MuckSaveGame.NameplatePlacement]::Place(500,350,10,1000,700,100,40)
Assert (-not $point.Edge -and $point.X -eq 500 -and $point.Y -eq 350) 'Visible teammates stay at their projected heads'
$point=[MuckSaveGame.NameplatePlacement]::Place(1200,350,10,1000,700,100,40)
Assert ($point.Edge -and $point.X -eq 900 -and $point.Arrow -eq '→') 'Right off-screen teammate gets a right edge marker'
$point=[MuckSaveGame.NameplatePlacement]::Place(-200,350,10,1000,700,100,40)
Assert ($point.Edge -and $point.X -eq 100 -and $point.Arrow -eq '←') 'Left marker remains on screen'
$point=[MuckSaveGame.NameplatePlacement]::Place(500,900,10,1000,700,100,40)
Assert ($point.Edge -and $point.Y -eq 660 -and $point.Arrow -eq '↑') 'Top edge keeps the full nameplate visible'
$point=[MuckSaveGame.NameplatePlacement]::Place(500,350,-10,1000,700,100,40)
Assert ($point.Edge -and $point.Y -eq 40 -and $point.Arrow -eq '↓') 'Directly behind teammate gets a stable rear direction'
$point=[MuckSaveGame.NameplatePlacement]::Place(700,350,-10,1000,700,100,40)
Assert ($point.Edge -and $point.X -eq 100 -and $point.Arrow -eq '←') 'Rear projection is inverted correctly'
$point=[MuckSaveGame.NameplatePlacement]::Place(2000,1500,10,1000,700,100,40)
Assert ($point.X -ge 100 -and $point.X -le 900 -and $point.Y -ge 40 -and $point.Y -le 660) 'Diagonal markers do not overflow the screen'
$point=[MuckSaveGame.NameplatePlacement]::Place(400,600,-1,320,180,200,100)
Assert ($point.X -ge 0 -and $point.X -le 320 -and $point.Y -ge 0 -and $point.Y -le 180) 'Small-window markers stay within bounds'
$path = Join-Path $tempRoot 'test.mucksave'
[MuckSaveGame.AtomicSaveFile]::Write($path, [Action[IO.Stream]]{param($s) $b=[Text.Encoding]::UTF8.GetBytes('first'); $s.Write($b,0,$b.Length)})
Assert ([IO.File]::ReadAllText($path) -eq 'first') 'First save written'
[MuckSaveGame.AtomicSaveFile]::Write($path, [Action[IO.Stream]]{param($s) $b=[Text.Encoding]::UTF8.GetBytes('second'); $s.Write($b,0,$b.Length)})
Assert ([IO.File]::ReadAllText($path) -eq 'second') 'New save committed'
Assert ([IO.File]::ReadAllText($path+'.bak') -eq 'first') 'Previous save backed up'
try {[MuckSaveGame.AtomicSaveFile]::Write($path, [Action[IO.Stream]]{param($s) $s.WriteByte(42); throw 'Simulated interrupted write'})} catch {}
Assert ([IO.File]::ReadAllText($path) -eq 'second') 'Failed write retains latest good save'
Assert ([IO.File]::ReadAllText($path+'.bak') -eq 'first') 'Failed write retains backup'
Assert (@(Get-ChildItem -LiteralPath $tempRoot -Filter '*.pending-*').Count -eq 0) 'Pending temp file cleaned'
"PASS: $script:count core checks"
# Only explicitly created test files are removed.
Remove-Item -LiteralPath $path, ($path+'.bak')
Remove-Item -LiteralPath $tempRoot
