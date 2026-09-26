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
  @('ContainerNet','RemoveItemAmount','UInt32,UInt16'),@('Master','HasConnectingClients',''))) {
  [void](Method (TypeDef $game $api[0]) $api[1] $api[2])
 }
 $spawn = Calls (Method (TypeDef $game 'SpawnManager') 'ManualSpawn' $null)
 if($spawn -notmatch 'NetworkObject::Spawn' -or $spawn -notmatch '_manualSpawn'){throw 'Native spawn lifecycle changed'}; $count++
 $gun=Calls (Method (TypeDef $game 'GunTool') 'RaycastShot' '')
 if($gun -notmatch 'TryGetComponent<Interactive>' -or $gun -notmatch 'GetComponentInParent<Vulnerable>'){throw 'Native weapon collider contract changed'}; $count++
 if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Tidefork.model.json')){throw 'Missing Tidefork model'}; $count++
 foreach($name in @('TideSummons','TideCreature','TideModel','TideRules','M4Armory','HorseStable','ChestQuickStack','YouTubeJukeboxPanel','ItemStacks')){[void](TypeDef $mod $name);$count++}
 $manager=TypeDef $mod 'TideSummons';$creature=TypeDef $mod 'TideCreature'
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
 foreach($pattern in @('get_IsServer','TideRules::CrossedHit','TideCreature::Strike')){if($tick -notmatch [regex]::Escape($pattern)){throw "Missing combat gate $pattern"};$count++}
 $strike=Calls (Method $creature 'Strike' 'Byte')
 foreach($pattern in @('TideRules::InHit','TideCreature::ClearSight','PlayerNet::HitClientRpc')){if($strike -notmatch [regex]::Escape($pattern)){throw "Missing hit gate $pattern"};$count++}
 $init=Calls (Method (TypeDef $mod 'TeammateHealthBars') 'Awake' '')
 if($init -notmatch 'TideSummons::Initialize'){throw 'Module not initialized'};$count++
 $version=(Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
 $info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
 if($info.FileVersion -ne "$version.0" -or $info.ProductVersion -ne $version){throw 'Version metadata mismatch'};$count++
 "PASS: $count Tidefork native API, authority, packaging and lifecycle checks"
} finally { $game.Dispose(); $mod.Dispose() }
