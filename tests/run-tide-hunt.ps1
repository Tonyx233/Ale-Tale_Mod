param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Get-Content (Join-Path $root 'Tidefork\TideHuntHome.cs') -Raw -Encoding UTF8
$method = [regex]::Match($source, '(?ms)^        internal void DropAt\(Vector3 point\).*?^        }\r?$')
if (!$method.Success) { throw 'Production delivery method missing' }
$extracted = Join-Path $root 'bin\TideHuntDeliveryProduction.cs'
[IO.File]::WriteAllText($extracted, "using System; using System.Linq; using UnityEngine; using HarmonyLib; namespace TonyMods { public partial class TideHuntHome {`n" + $method.Value + "`n} }", [Text.Encoding]::UTF8)
$output = Join-Path $root 'bin\TideHuntTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) $extracted (Join-Path $root 'Tidefork\TideHunter.cs') (Join-Path $root 'Tidefork\TideHuntRules.cs') (Join-Path $PSScriptRoot 'TideHuntTestDoubles.cs') (Join-Path $PSScriptRoot 'TideHuntTests.cs')
if ($LASTEXITCODE) { throw 'Hunting tests compile failed' }
& $output
if ($LASTEXITCODE) { throw 'Hunting behavior regression' }

[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
$checks = 0
function Require($ok, $message) { if (!$ok) { throw $message }; $script:checks++ }
function NativeType($asm,$name) { $asm.MainModule.Types | Where-Object Name -eq $name }
function AllMethods($t) { $t.Methods; foreach ($n in $t.NestedTypes) { AllMethods $n } }
function Body($asm,$type,$method) { ((AllMethods (NativeType $asm $type)) | Where-Object Name -match ('^'+$method+'$') | ForEach-Object { $_.Body.Instructions | ForEach-Object ToString }) -join "`n" }
try {
    # Verify native item/save locals and factory signatures used by the furniture result marker.
    foreach ($m in (NativeType $game FurnitureManager).Methods | Where-Object Name -eq PlaceFurnitureServerRpc) {
        Require ($m.Body.Variables[2].VariableType.FullName -eq 'Item') 'Placement no longer has selected Item at local 2'
        $calls = @($m.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'Object::Instantiate<UnityEngine.GameObject>' })
        Require ($calls.Count -eq 1) 'Placement instantiate call count changed'
        Require ($calls[0].Operand.Parameters.Count -eq 3) 'Placement instantiate signature changed'
    }
    $load = (NativeType $game FurnitureManager).Methods | Where-Object Name -eq OnNetworkSpawn
    Require ($load.Body.Variables[2].VariableType.FullName -eq 'SavedDevice') 'Load no longer has SavedDevice at local 2'
    $first = @($load.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'Object::Instantiate<UnityEngine.GameObject>' })[0]
    Require ($first.Operand.Parameters.Count -eq 4) 'Load instantiate signature changed'
    foreach ($pair in @(@('CreatureBase','lootItems'),@('CreatureBase','lootMoney'),@('CreatureBase','isLootAvailable'),@('CreatureBase','rarity'))) {
        Require ($null -ne ((NativeType $game $pair[0]).Fields | Where-Object Name -eq $pair[1])) "Missing native $($pair -join '.')"
    }
    Require ((Body $game ContainerNet AddNewItem) -match '(?s)ldarg\.2.*Item::amount.*stind.i2') 'Native out value is no longer initialized to remaining amount'
    Require ((Body $game Furniture GetSavedDevice) -match 'ContainerNet::items') 'Native house cargo is not serialized'
    Require ((Body $game Furniture GetSavedDevice) -match 'ItemData::id') 'Independent building item ID is not serialized'
    Require ((Body $game Furniture SetSavedDevice) -match 'ContainerNet::savedContainer') 'Native cargo load path missing'
    Require ((Body $game ContainerNet OnNetworkSpawn) -match 'SavedDevice/Container::name') 'Variant marker is not restored'
    Require ((Body $game HelperHouse ToggleHelperActiveServerRpc) -match 'isHelperActive') 'Native helper toggle changed'
    Require ((Body $game SpawnManager Spawn) -notmatch 'ManualSpawn') 'Natural spawn path now delegates to manual spawning'
    Require ((Body $mod TideHuntBuilding AfterNatural) -match 'Natural') 'Natural provenance not captured'
    Require ((Body $mod TideHunter Allowed) -match 'Natural') 'Eligibility must require natural provenance'
    Require ((Body $mod TideHunter Hit) -match 'Allowed') 'Impact must recheck eligibility'
    Require ((Body $mod TideCreature Strike) -match 'StrikeHunt') 'Hunter melee branch missing'
    Require ((Body $mod TideCreature Splash) -match 'StrikeHunt') 'Hunter splash branch missing'
    Require ((Body $mod TideCreature Initialize) -match 'Record::hunting') 'Hunter must not get owner-bound companion controller'
    Require ((Body $mod TideSummons '<Throw>b__.*') -match 'Record::hunting') 'Companion recall must exclude building hunters'
    Require ((Body $mod TideSummons SpawnHunter) -match 'peers') 'Hunter spawn must require peer handshake'
    Require ((Body $mod TideHuntHome DropAt) -match '(?s)CollectibleManager::Spawn.*ContainerNet::RemoveItemById') 'Delivery removal must follow successful spawn'
    Require ((Body $mod TideHuntBuilding HouseSpawn) -notmatch 'NetworkBehaviour') 'Do not add NetworkBehaviours to native house'
    Require ((Body $mod TideHuntBuilding '(Register|<Register>.*)') -match 'ItemData::netPrefab') 'Native building template lookup missing'
    Require ((Body $mod TideHuntHome GetDelivery) -match 'TavernSign') 'Delivery must locate the tavern front'
    Require ((Body $mod TideHuntHome Initialize) -match 'SaveManager::isLoadingGame') 'Fresh placement must not inherit a serialized inactive helper default'
    Require ((Body $game HelperHouse OnNetworkSpawn) -match '(?s)SaveManager::isLoadingGame.*HelperHouse::savedHelperHouse') 'Native helper restore guard changed'
    $spawnGuard = [regex]::Match($source, 'if \(Body == null[^\r\n]*').Value
    Require ($spawnGuard -and $spawnGuard -notmatch 'hasDelivery') 'Worker visibility must not depend on finding a delivery point'
    Require ((Body $mod TideSummons SpawnHunter) -match 'TideHuntHome::Report') 'Silent spawn failures must report waiting reasons'
    Require ((Body $mod TideHuntHome Update) -notmatch 'SaveManager::isLoadingGame') 'Loaded sessions must not remain blocked by the persistent load-origin flag'
    Require ((Body $mod TideHuntHome Update) -match 'Master::isHostReady') 'Workers must wait for actual host readiness'
    Require ((Body $game PlayerManager OnPlayerSpawn) -match '(?s)ldc.i4.1.*Master::SetHostReady') 'Native player spawn no longer marks host ready'
    Require ((Body $game PlayerManager OnNetworkDespawn) -match '(?s)ldc.i4.0.*Master::SetHostReady') 'Native player teardown no longer resets host readiness'
    foreach ($name in @('Boar','Wolf','Bear','Hornet','Turtle','Crab','Toad','Rabbit','Wildfowl','SkeletonWarrior2H','SkeletonWarriorShield','OrcMelee','Zombie','Spider','ZombieHound','Ghoul','Mummy','Snake','Snail','Slug')) {
        $enum = (NativeType $game Spawnable).NestedTypes | Where-Object Name -eq Type
        Require ($null -ne ($enum.Fields | Where-Object Name -eq $name)) "Native species removed: $name"
    }
    "PASS: $checks hunting native placement, persistence, loot and authority contracts"
} finally { $game.Dispose(); $mod.Dispose() }
