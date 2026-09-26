param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\M4RulesTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) (Join-Path $PSScriptRoot 'M4RulesTests.cs') (Join-Path $root 'M4\M4Rules.cs') (Join-Path $root 'M4\M4Sound.cs') (Join-Path $root 'GunScope\ScopeMath.cs')
if ($LASTEXITCODE -ne 0) { throw 'M4 test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'M4 rule checks failed' }
node (Join-Path $PSScriptRoot 'validate-m4-model.js')
if ($LASTEXITCODE -ne 0) { throw 'M4 model validation failed' }

[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$managed = Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
$fmod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'FMODUnity.dll'))
$netcode = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Unity.Netcode.Runtime.dll'))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
$count = 0
function Def($asm, $name) { $t = @($asm.MainModule.Types) | Where-Object { $_.FullName -eq $name -or $_.Name -eq $name } | Select-Object -First 1; if (!$t) { throw "Missing type $name" }; $t }
function Sig($m) { ($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ',' }
function Method($type, $name, $sig) {
    $hit = @($type.Methods | Where-Object { $_.Name -eq $name -and ($null -eq $sig -or (Sig $_) -eq $sig) })
    if ($hit.Count -lt 1) { throw "API changed: $($type.Name).$name($sig)" }
    $script:count++; $hit[0]
}
function Field($type, $name, $typeName) {
    $f = $type.Fields | Where-Object Name -eq $name
    if (!$f -or ($typeName -and $f.FieldType.Name -ne $typeName)) { throw "API changed: field $($type.Name).$name" }
    $script:count++
}
function Body($method) { $method.Body.Instructions | ForEach-Object ToString }
try {
    $gun = Def $game 'GunTool'
    foreach ($f in @(@('_state','State'),@('_isReloading','Boolean'),@('_shotTimer','Single'),@('_gunItemId','UInt32'),@('ammoData','ItemData'),
        @('_fireNoise','Single'),@('_damageDeviation','UInt16'),@('_breakSoundEvent','SoundEvent'),@('endPoint','Transform'),@('fireRate','Single'),
        @('clipContent','Int32'),@('clipSize','Int32'),@('triggerType','TriggerType'),@('spreadAngle','Single'),@('damage','UInt16'))) { Field $gun $f[0] $f[1] }
    foreach ($m in @(@('Fire',''),@('Reload',''),@('CheckReload',''),@('SetItem','Item'),@('Selected',''),@('RaycastShot',''),@('SetState','State'))) { [void](Method $gun $m[0] $m[1]) }
    # The two native limits the M4 patches exist for; if either disappears, re-check the patches.
    $fire = Body (Method $gun 'Fire' '')
    if (!($fire -match 'Animator::SetTrigger') -or !($fire -match 'GunTool::SetState')) { throw 'Native Fire no longer animation-gated; re-check M4 fire patch' }
    $reload = @((Method $gun 'Reload' '').Body.Instructions)
    $add = [Array]::FindIndex($reload, [Predicate[object]]{ param($i) "$($i.Operand)" -match 'AddItemChargeServerRpc' })
    if ($add -lt 1 -or $reload[$add - 1].OpCode.Code -ne 'Ldc_I4_1') { Write-Warning 'Native Reload no longer loads a single round; M4 reload patch may now be redundant' } else { $count++ }
    $control = Body (Method $gun 'UpdateControllerState' '')
    if (!($control -match 'GunTool::triggerType') -or !($control -match 'GunTool::Fire')) { throw 'Native trigger handling changed' }

    $container = Def $game 'ContainerNet'
    foreach ($m in @(@('RemoveItemChargeServerRpc','UInt32,UInt16'),@('AddItemChargeServerRpc','UInt32,UInt16'),@('RemoveAmountServerRpc','UInt16,UInt16'),@('GetItemAmount','UInt16'),@('GetItemById','UInt32,Item&,Boolean'))) { [void](Method $container $m[0] $m[1]) }
    $items = Def $game 'ItemManager'
    $damage = Body (Method $items 'DamageToolServerRpc' 'UInt32,UInt16,ServerRpcParams')
    if (!($damage -match 'ItemData::durabilityPerHit') -or !($damage -match 'mul')) { throw 'DamageToolServerRpc no longer scales by count; batching unsafe' }
    [void](Method $items 'Awake' '')
    # 1 coin = 1000 rounds relies on native shop stacks: Item(ItemData).amount = maxStack, price per stack when !buyByOne.
    $ctor = Body (Method (Def $game 'Item') '.ctor' 'ItemData')
    if (!($ctor -match 'ItemData::maxStack') -or !($ctor -match 'Item::amount')) { throw 'Item(ItemData) no longer sets amount = maxStack' }
    $buy = Body (Method $items 'BuyServerRpc' $null)
    if (!($buy -match 'ItemData::buyByOne') -or !($buy -match 'ItemData::GetItemPrice')) { throw 'Shop purchase flow changed' }
    $price = Body (Method (Def $game 'ItemData') 'GetItemPrice' 'Rarity')
    if (!($price -match 'ItemData::buyByOne') -or !($price -match 'ItemData::maxStack')) { throw 'Shop price rule changed' }

    $tp = Def $game 'PlayerAnimTP'
    $pose = Body (Method $tp 'OnSelectedHandItemDataId' 'UInt32,UInt32')
    if (!($pose -match 'ldstr "TorsoState"') -or !($pose -match 'ldc.i4 360') -or !($pose -match 'ItemData::tpPrefab')) { throw 'Third-person gun pose changed' }
    [void](Method $tp 'DisableHandRig' ''); Field $tp 'animator' 'Animator'
    $coll = Def $game 'CollectibleNet'
    [void](Method $coll 'OnNetworkSpawn' ''); [void](Method $coll 'OnItemValueChanged' 'Item,Item'); Field $coll 'item' $null
    $move = Def $game 'PlayerMovement'
    Field $move '_cameraVerticalAngle' 'Single'; Field $move 'playerTransform' 'Transform'
    if (!((Body (Method $move 'Update' '')) -match 'PlayerMovement::_cameraVerticalAngle')) { throw 'Camera pitch field no longer drives the view' }
    if (!((Body (Method (Def $game 'FmodManager') 'Start' '')) -match 'ldstr "bus:/Sound"')) { throw 'Sound bus path changed' }
    [void](Method (Def $game 'PlayerNoiseManager') 'NoiseServerRpc' 'Vector3,Single,ServerRpcParams')
    $spawn = Body (Method (Def $game 'PlayerInventory') 'OnInventoryAdd' 'UInt16')
    if (!($spawn -match 'ItemData::fpPrefab') -or !($spawn -match 'GunTool::SetItem')) { throw 'Native fp gun spawn changed' }

    $core = Def $fmod 'FMOD.System'
    [void](Method $core 'createSound' 'Byte[],MODE,CREATESOUNDEXINFO&,Sound&'); [void](Method $core 'playSound' 'Sound,ChannelGroup,Boolean,Channel&'); [void](Method $core 'getMasterChannelGroup' 'ChannelGroup&')
    $channel = Def $fmod 'FMOD.Channel'
    foreach ($m in @(@('set3DAttributes','VECTOR&,VECTOR&'),@('set3DMinMaxDistance','Single,Single'),@('set3DLevel','Single'),@('setVolume','Single'),@('setPitch','Single'),@('setPaused','Boolean'))) { [void](Method $channel $m[0] $m[1]) }
    $runtime = Def $fmod 'FMODUnity.RuntimeManager'
    [void](Method $runtime 'get_CoreSystem' ''); [void](Method $runtime 'GetBus' 'String'); [void](Method $runtime 'get_IsInitialized' '')
    [void](Method (Def $netcode 'FastBufferWriter') 'WriteBytesSafe' 'Byte[],Int32,Int32')
    [void](Method (Def $netcode 'FastBufferReader') 'ReadBytesSafe' 'Byte[]&,Int32,Int32')

    foreach ($name in @('M4Armory','M4Rifle','M4Model','M4Audio','M4Sound','M4Rules','MusketScope','HorseStable','ItemStacks','ChestQuickStack','YouTubeJukeboxPanel')) { [void](Def $mod "TonyMods.$name"); $count++ }
    $armory = Def $mod 'TonyMods.M4Armory'
    $handlers = @{ RegisterItems='ItemManager'; AfterSetItem='GunTool,Item'; AfterSelected='GunTool'; BeforeFire='GunTool'; BeforeReload='GunTool'; BeforeCheckReload='GunTool';
        BeforeHandItem='PlayerAnimTP,GameObject&'; AfterHandItem='PlayerAnimTP,UInt32,GameObject'; AfterCollectibleSpawn='CollectibleNet'; AfterCollectibleItem='CollectibleNet,Item' }
    foreach ($h in $handlers.Keys) {
        $m = Method $armory $h $handlers[$h]
        if ($h -in @('BeforeFire','BeforeReload','BeforeCheckReload') -and $m.ReturnType.Name -ne 'Boolean') { throw "$h must be a skipping prefix" }
        foreach ($p in $m.Parameters) { if ($p.Name -notin @('__instance','__0','__1','__state')) { throw "Harmony parameter $($p.Name) in $h must be __instance or positional" } }
    }
    $init = Body (Method $armory 'Initialize' 'ConfigFile,ManualLogSource')
    foreach ($target in @('"Awake"','"SetItem"','"Selected"','"Fire"','"Reload"','"CheckReload"','"OnSelectedHandItemDataId"','"OnNetworkSpawn"','"OnItemValueChanged"')) { if (!($init -match [Regex]::Escape($target))) { throw "Patch target $target not installed" }; $count++ }
    foreach ($res in @(@('Tony.M4.model.json','M4\Assets\model.json'),@('Tony.M4.icon.png','M4\Assets\m4-icon.png'),@('Tony.M4.ammo.png','M4\Assets\ammo-icon.png'))) {
        $r = $mod.MainModule.Resources | Where-Object Name -eq $res[0]
        if (!$r) { throw "Missing resource $($res[0])" }
        $expected = [IO.File]::ReadAllBytes((Join-Path $root $res[1]))
        if ([Convert]::ToBase64String($r.GetResourceData()) -ne [Convert]::ToBase64String($expected)) { throw "Stale resource $($res[0])" }
        $count++
    }
    # Host ServerRpcs run synchronously and re-enter through OnItemsChanged -> CheckReload -> Reload.
    # 0.14.0 sent RPCs before clearing its counters / marking the reload and overflowed the stack.
    $rifle = Def $mod 'TonyMods.M4Rifle'
    function Index($lines, $pattern) { [Array]::FindIndex([object[]]$lines, [Predicate[object]]{ param($l) $l -match $pattern }) }
    $flush = @(Body (Method $rifle 'Flush' ''))
    $take = Index $flush 'M4Batch::Take'
    $firstRpc = Index $flush 'ServerRpc'
    if ($take -lt 0 -or $firstRpc -lt 0 -or $take -gt $firstRpc) { throw 'Flush must clear the batch before sending any RPC' }
    $start = @(Body (Method $rifle 'StartReload' ''))
    $mark = Index $start 'M4Rifle::ReloadingField'
    $state = Index $start 'M4Rifle::SetStateMethod'
    $nested = Index $start 'M4Rifle::Flush'
    $remove = Index $start 'RemoveAmountServerRpc'
    $addCharge = Index $start 'AddItemChargeServerRpc'
    if ($mark -lt 0 -or $state -lt 0 -or $mark -gt $nested -or $state -gt $nested -or $nested -gt $remove -or $remove -gt $addCharge) { throw 'StartReload must mark the reload before Flush and the inventory RPCs' }
    if (!((Body (Method $armory 'BeforeCheckReload' 'GunTool')) -match 'M4Rifle::get_Busy')) { throw 'CheckReload prefix must ignore a reload in progress' }
    $count += 3
    $scopeGun = Body (Method (Def $mod 'TonyMods.MusketScope') 'GunUpdated' 'GunTool')
    $isM4 = [Array]::FindIndex([object[]]$scopeGun, [Predicate[object]]{ param($l) $l -match 'M4Armory::IsM4' })
    $mesh = [Array]::FindIndex([object[]]$scopeGun, [Predicate[object]]{ param($l) $l -match 'ldstr "MusketRoot/Musket/Musket1_2_1"' })
    if ($isM4 -lt 0 -or $mesh -lt 0 -or $isM4 -gt $mesh) { throw 'Scope must recognise the M4 before the musket mesh (M4 is spawned from Musket_fp)' }
    $count++
    $plugin = Body (Method (Def $mod 'TonyMods.TeammateHealthBars') 'Awake' '')
    if (!($plugin -match 'M4Armory::Initialize')) { throw 'Plugin does not start the M4' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'bin\Tony.TeammateHealthBars.dll')).FileVersion
    $expectedVersion = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim() + '.0'
    if ($version -ne $expectedVersion) { throw "FileVersion $version != $expectedVersion" }
    $count += 2
    Write-Output "PASS: $count M4 game-API, FMOD, Netcode, patch and packaging checks"
} finally { $game.Dispose(); $fmod.Dispose(); $netcode.Dispose(); $mod.Dispose() }
