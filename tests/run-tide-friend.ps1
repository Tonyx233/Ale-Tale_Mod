param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\TideFriendTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) (Join-Path $PSScriptRoot 'TideFriendTests.cs') (Join-Path $PSScriptRoot 'TideFriendTestDoubles.cs') (Join-Path $root 'Tidefork\TideFriend.cs') (Join-Path $root 'Tidefork\TideFriendRules.cs') (Join-Path $root 'Tidefork\TideRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Companion tests did not compile' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Companion behavior regression' }

# Run the real use transaction in isolation; keep Unity/service effects deterministic.
$source = Get-Content -LiteralPath (Join-Path $root 'Tidefork\TideSummons.cs') -Raw -Encoding UTF8
$method = [regex]::Match($source, '(?ms)^        private void Throw\(ContainerNet container, uint itemId, ulong sender\).*?^        }\r?$')
if (!$method.Success) { throw 'Production Throw method not found' }
$extracted = Join-Path $root 'bin\TideSummonUseProduction.cs'
[IO.File]::WriteAllText($extracted, "using System; using System.Linq; using UnityEngine; namespace TonyMods { internal partial class TideSummons {`n" + $method.Value + "`n} }", [Text.Encoding]::UTF8)
$output = Join-Path $root 'bin\TideSummonUseTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) $extracted (Join-Path $PSScriptRoot 'TideSummonUseTests.cs') (Join-Path $root 'Tidefork\TideFriendRules.cs') (Join-Path $root 'Tidefork\TideRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Summon transaction tests did not compile' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Summon transaction regression' }

[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$count = 0
function Body($assembly, $type, $name) {
    $t = $assembly.MainModule.Types | Where-Object Name -eq $type
    $m = @($t.Methods | Where-Object Name -eq $name)
    if ($m.Count -ne 1) { throw "Ambiguous method $type.$name" }
    ($m[0].Body.Instructions | ForEach-Object ToString) -join "`n"
}
function Require($body, $pattern, $reason) {
    if ($body -notmatch $pattern) { throw $reason }; $script:count++
}
try {
    $init = Body $mod TideCreature Initialize
    Require $init 'TideFriend::.ctor' 'Friend controller not attached'
    Require $init 'Vulnerable::SetHpMax' 'Summoned instance needs full maximum health'
    Require $init '(?s)ldc.i4 1000.*NetworkVariable`1<System.UInt16>::set_Value' 'Fresh summon must receive 1000 HP'
    $tick = Body $mod TideCreature Tick
    Require $tick 'TideFriend::ResolveOwner' 'Owner death/disconnect cleanup missing'
    Require $tick 'TideFriend::Follow' 'Follow path missing'
    $target = Body $mod TideCreature Target
    Require $target '(?s)TideFriend::Target.*ret.*TideCreature::Nearest' 'Friend must return before player targeting'
    Require (Body $mod TideCreature Strike) '(?s)StrikeFriends.*ret.*PlayerNet::HitClientRpc' 'Friend melee must return before player damage'
    Require (Body $mod TideCreature Splash) '(?s)SplashFriends.*ret.*PlayerNet::HitClientRpc' 'Friend splash must return before player damage'
    $hit = Body $mod TideFriend Hit
    Require $hit '(?s)Eligible.*Vulnerable::Hit\(System.UInt16,System.UInt16\)' 'Damage must recheck eligible monsters and avoid player-attributed RPC'
    $after = Body $mod TideSummons AfterWeaponHit
    Require $after 'ServerRpcReceiveParams::SenderClientId' 'Owner must come from authoritative RPC sender'
    Require $after 'Record::summoner' 'Owner attack must route only to own companion'
    Require $after 'petHitDepth' 'Native pet attacks must not be assigned to the host'
    Require (Body $mod TideSummons Initialize) 'AfterPetHit' 'Native pet attribution scope must be patched'
    Require (Body $mod TideSummons RegisterFriend) '(?s)doNotRemoveOnUse.*doNotSave.*shopItem' 'Persistent reusable shop item missing'
    Require (Body $mod TideSummons Remove) '(?s)TideCreature::Hide.*RemoveById' 'Recall must stop all future attacks immediately'
    Require (Body $game CreatureHostile HasChasedPlayer) '_chasedPlayer' 'Native owner threat API changed'
    Require (Body $mod TideSummons AfterPetHit) 'petHitDepth' 'Native pet attribution scope must unwind after errors'
    # Players are the only thing that can hurt the companion (native monsters only hit players or defence-quest
    # targets): the host drops those hits, and the M4 shoots through it on every peer.
    $prefix = @(($mod.MainModule.Types | Where-Object Name -eq 'TideSummons').Methods | Where-Object Name -eq 'BeforeWeaponHit')
    Require $prefix[0].ReturnType.FullName '^System\.Boolean$' 'Weapon-hit prefix must be able to skip the receiver'
    Require (Body $mod TideSummons BeforeWeaponHit) 'TideSummons::Friendly' 'Player weapons must not damage the companion'
    Require (Body $mod TideSummons Friendly) 'Record::friendly' 'Companion check must read the replicated friendly flag'
    Require (Body $mod M4Rifle Shoot) 'TideSummons::Friendly' 'M4 bullets must pass through the companion'
    Require (Body $mod M4Rifle Target) 'TideSummons::Friendly' 'M4 sweep must never pick the companion'
    Require (Body $mod TideFriend Follow) 'TideFriend::lastFollow' 'Follow must restart stuck timing after a fight'
    "PASS: $count companion native API, damage routing, lifecycle and packaging checks"
} finally { $mod.Dispose(); $game.Dispose() }
