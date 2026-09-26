param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\TideRulesTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) (Join-Path $PSScriptRoot 'TideRulesTests.cs') (Join-Path $root 'Tidefork\TideRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tidefork rules build failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Tidefork rules failed' }
node (Join-Path $PSScriptRoot 'validate-tide-model.js')
if ($LASTEXITCODE -ne 0) { throw 'Tidefork geometry failed' }
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
$count = 0
function TypeDef($asm,$name) { $t=$asm.MainModule.Types | Where-Object Name -eq $name; if (!$t) { throw "Missing type $name" }; $t }
function Method($t,$name,$sig) {
 $m=@($t.Methods | Where-Object { $_.Name -eq $name -and (($null -eq $sig) -or (($_.Parameters | ForEach-Object {$_.ParameterType.Name}) -join ',') -eq $sig) })
 if($m.Count -ne 1){throw "Ambiguous/missing $($t.Name).$name($sig)"}; $script:count++; $m[0]
}
function Calls($m) { ($m.Body.Instructions | ForEach-Object ToString) -join "`n" }
try {
 foreach($target in @(@('ItemManager','Awake',''),@('InventoryItemUseManager','UseInventoryItem','ContainerNet,UInt32,ItemData,UInt64'),
  @('CreatureBase','OnHpChanged','UInt16,UInt16'),@('CreatureBase','SetState','State'),@('CreatureHostile','FixedUpdate',''),
  @('CreatureHostile','OnAnim','String'),@('CreatureHostile','OnAlarm','PlayerNet'),@('CreatureHostile','OnHit',''),
  @('CreatureHostile','OnDeath',''),@('Vulnerable','OnDeath',''),@('SaveManager','LoadGame','String,Boolean'),@('SaveManager','NewGame','String'))) {
   if($target[0] -eq 'SaveManager'){[void](Method (TypeDef $game $target[0]) $target[1] $null)}
   else {[void](Method (TypeDef $game $target[0]) $target[1] $target[2])}
 }
 foreach($api in @(@('SpawnManager','ManualSpawn','Type,Vector3,Quaternion,Spawnable&,Boolean'),@('SpawnManager','RemoveById','UInt16,Boolean'),
  @('PlayerNet','HitClientRpc','Int16,Vector3,Single,Boolean,EffectType'),@('Vulnerable','SetHpMax','UInt16'),
  @('ContainerNet','RemoveItemAmount','UInt32,UInt16'),@('Master','HasConnectingClients',''),
  @('PlayerNet','HitEffectClientRpc','EffectType,Int32,Byte,Vector3,Single,Boolean'),@('EffectsController','AddEffectServerRpc','EffectType,Single,Int32,ServerRpcParams'))) {
  [void](Method (TypeDef $game $api[0]) $api[1] $api[2])
 }
 # 潮彈 slow: the owner's effect RPC must add a native effect that replaces (not stacks) and lowers move speed.
 $effectRpc=Calls (Method (TypeDef $game 'PlayerNet') 'HitEffectClientRpc' $null)
 if($effectRpc -notmatch [regex]::Escape('EffectsController::AddEffectServerRpc(EffectsController/EffectType,System.Single,System.Int32')){throw 'Native hit effect RPC changed'}; $count++
 $addEffect=Calls (Method (TypeDef $game 'EffectsController') 'AddEffectServerRpc' 'EffectType,Single,Int32,ServerRpcParams')
 if($addEffect -notmatch 'EffectsController::RemoveSameTypeEffect'){throw 'Native effects now stack; the shell slow would compound'}; $count++
 $newEffect=Calls (Method (TypeDef $game 'EffectsController') 'OnNewEffectAdded' $null)
 if($newEffect -notmatch 'PlayerNet::IncreaseSpeedPercentage'){throw 'Native slow no longer lowers move speed'}; $count++
 $spawn = Calls (Method (TypeDef $game 'SpawnManager') 'ManualSpawn' $null)
 if($spawn -notmatch 'NetworkObject::Spawn' -or $spawn -notmatch '_manualSpawn'){throw 'Native spawn lifecycle changed'}; $count++
 $gun=Calls (Method (TypeDef $game 'GunTool') 'RaycastShot' '')
 if($gun -notmatch 'TryGetComponent<Interactive>' -or $gun -notmatch 'GetComponentInParent<Vulnerable>'){throw 'Native weapon collider contract changed'}; $count++
 if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Tidefork.model.json')){throw 'Missing Tidefork model'}; $count++
 foreach($name in @('TideSummons','TideCreature','TideModel','TideRules','M4Armory','HorseStable','ChestQuickStack','YouTubeJukeboxPanel','ItemStacks')){[void](TypeDef $mod $name);$count++}
 $manager=TypeDef $mod 'TideSummons';$creature=TypeDef $mod 'TideCreature'
 if(@((TypeDef $mod 'TideRules').Fields | Where-Object { $_.Name -in 'Limit','Lifetime' }).Count){throw 'Room cap and lifetime must stay removed'}; $count++
 $broadcast=Calls (Method $manager 'Broadcast' '')
 if($broadcast -notmatch 'Enumerable::Take'){throw 'Snapshots must be chunked for unbounded idol counts'}; $count++
 if($creature.BaseType.Name -ne 'MonoBehaviour'){throw 'Do not append a NetworkBehaviour to native prefab'}; $count++
 $use=Calls (Method $manager 'Throw' 'ContainerNet,UInt32,UInt64')
 foreach($pattern in @('get_IsServer','TideRules::CanUse','HasConnectingClients','TideSummons::Landing','TideCreature::Initialize','ContainerNet::RemoveItemAmount')) {
  if($use -notmatch [regex]::Escape($pattern)){throw "Missing use gate $pattern"};$count++
 }
 if($use.IndexOf('TideCreature::Initialize') -gt $use.IndexOf('ContainerNet::RemoveItemAmount')){throw 'Do not charge before successful creation'}; $count++
 $receive=Calls (Method $manager 'Receive' 'UInt64,FastBufferReader')
 foreach($pattern in @('TideSummons::Valid','get_IsServer')){if($receive -notmatch [regex]::Escape($pattern)){throw "Missing protocol gate $pattern"};$count++}
 # ServerClientId is compiled as the constant zero; require the sender==0 guard before deserialization.
 if($receive -notmatch '(?s)ldarg\.1\s+IL_\w+: ldc\.i4\.0\s+IL_\w+: conv\.i8\s+IL_\w+: beq.*leave'){throw 'Missing host-only sender comparison'};$count++
 $tick=Calls (Method $creature 'Tick' '')
 foreach($pattern in @('get_IsServer','TideRules::CrossedHit','TideCreature::Strike','TideRules::ShotDue','TideCreature::Splash','TideRules::InShotRange','TideCreature::Fire','NavMeshPathStatus')){if($tick -notmatch [regex]::Escape($pattern)){throw "Missing combat gate $pattern"};$count++}
 $strike=Calls (Method $creature 'Strike' 'Byte')
 foreach($pattern in @('TideRules::InHit','TideCreature::ClearSight','PlayerNet::HitClientRpc')){if($strike -notmatch [regex]::Escape($pattern)){throw "Missing hit gate $pattern"};$count++}
 $splash=Calls (Method $creature 'Splash' '')
 foreach($pattern in @('TideRules::InSplash','TideCreature::Blocked','PlayerNet::HitClientRpc','PlayerNet::HitEffectClientRpc')){if($splash -notmatch [regex]::Escape($pattern)){throw "Missing shell hit gate $pattern"};$count++}
 $fire=Calls (Method $creature 'Fire' 'PlayerNet,Vector3,Double')
 foreach($pattern in @('TideRules::InShotRange','TideCreature::Ground','TideCreature::ClearArc','TideSummons/Record::shotAt','TideCreature::Enter')){if($fire -notmatch [regex]::Escape($pattern)){throw "Missing shell plan gate $pattern"};$count++}
 if($fire.IndexOf('Record::shotAt') -gt $fire.IndexOf('TideCreature::Enter')){throw 'Shell fields must be set before the action snapshot is marked'}; $count++
 if((Calls (Method $creature 'Enter' 'Byte')) -notmatch [regex]::Escape('TideSummons/Record::shotAt')){throw 'Death must cancel a shell in the air'}; $count++
 $sceneMax=((TypeDef $mod 'TideRules').Fields | Where-Object Name -eq 'SceneNameMax').Constant
 $valid=Calls (Method $manager 'Valid' 'Snapshot')
 if($valid -notmatch [regex]::Escape('TideRules::ValidShot')){throw 'Missing shell protocol gate'}; $count++
 # SceneNameMax is a constant, so Valid carries its inlined value.
 if($valid -notmatch "ldc\.i4\.s $sceneMax\b"){throw 'Scene name bound missing'}; $count++
 if((Calls (Method $manager 'Update' '')) -notmatch 'Tony\.Tidefork\.v3'){throw 'Shell snapshots need their own channel version'}; $count++
 # Snapshot parts travel as UTF-16 (FastBufferWriter.WriteValueSafe(string) writes two bytes per char). A part
 # of ChunkSize worst-case records must fit UnityTransport's 6144-byte payload used by the LAN/relay managers.
 $widest=@{UInt64=20;Byte=3;Int32=11;Double=24;Single=15;Vector3=61;String=2+$sceneMax}
 function Width($fields){ $chars=2+$fields.Count-1; foreach($f in $fields){ if(!$widest.ContainsKey($f.FieldType.Name)){throw "No JSON width for $($f.FieldType.Name)"}; $chars+=$f.Name.Length+3+$widest[$f.FieldType.Name] }; $chars }
 $record=Width @(($manager.NestedTypes | Where-Object Name -eq 'Record').Fields | Where-Object { $_.IsPublic -and !$_.IsStatic })
 $chunk=($manager.Fields | Where-Object Name -eq 'ChunkSize').Constant
 $scalars=@(($manager.NestedTypes | Where-Object Name -eq 'Snapshot').Fields | Where-Object { $_.IsPublic -and !$_.IsStatic -and $_.Name -ne 'records' })
 $partChars=(Width $scalars)+1+'"records":[]'.Length+$chunk*$record+$chunk-1
 $partBytes=4+2*$partChars+64
 if($partBytes -gt 6144){throw "Worst-case snapshot part is $partBytes bytes; lower ChunkSize"}; $count++
 "Worst-case snapshot part: $chunk records, $partBytes of 6144 bytes"
 $init=Calls (Method (TypeDef $mod 'TeammateHealthBars') 'Awake' '')
 if($init -notmatch 'TideSummons::Initialize'){throw 'Module not initialized'};$count++
 $version=(Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
 $info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
 if($info.FileVersion -ne "$version.0" -or $info.ProductVersion -ne $version){throw 'Version metadata mismatch'};$count++
 "PASS: $count Tidefork native API, authority, packaging and lifecycle checks"
} finally { $game.Dispose(); $mod.Dispose() }
