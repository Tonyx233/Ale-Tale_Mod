param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\QuickStackTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) (Join-Path $PSScriptRoot 'QuickStackTests.cs') (Join-Path $root 'QuickStack\QuickStackRules.cs')
if ($LASTEXITCODE -ne 0) { throw 'Quick stack test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Quick stack checks failed' }
# Load from bytes: Add-Type -Path refuses the downloaded (Zone.Identifier) BepInEx copy of Cecil.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
try {
    $types = @{}; foreach ($type in $game.MainModule.Types) { $types[$type.Name] = $type }
    $signatures = @{}; foreach ($type in $game.MainModule.Types) { foreach ($method in $type.Methods) { $signatures[$method.FullName] = $method } }
    foreach ($signature in @(
        'System.Void ItemManager::MoveItemToContServerRpc(System.UInt32,System.UInt16,System.UInt16)',
        'System.Boolean ItemManager::GetItemData(System.UInt32,ItemData&)',
        'PlayerInventory/State PlayerInventory::get_state()',
        'System.Void ContainerUI::TakeAll()',
        'System.Void ContainerUI::Sort()',
        'System.Void InventoryUI::OnItemDoubleClickBP(Item)',
        'System.Void SoundManager::Play(SoundEvent)')) {
        if (!$signatures.ContainsKey($signature)) { throw "Quick stack API missing: $signature" }
    }
    foreach ($field in @(@('PlayerInventory','extContainer'),@('PlayerInventory','inventory'),@('ContainerNet','orderedItems'),@('ContainerNet','id'),
        @('ContainerNet','isShop'),@('ContainerNet','isPet'),@('ContainerNet','isGeneric'),@('InventoryUI','invExt'),@('ContainerUI','container'),
        @('ItemData','quest'),@('Item','id'),@('Item','dataId'),@('Item','order'),@('Item','contId'))) {
        if (!($types[$field[0]].Fields | Where-Object { $_.Name -eq $field[1] -and $_.IsPublic })) { throw ('Quick stack field missing: '+($field -join '.')) }
    }
    $state = $types['PlayerInventory'].NestedTypes | Where-Object Name -eq 'State'
    if (($state.Fields | Where-Object Name -eq 'Storage').Constant -ne 10) { throw 'PlayerInventory.State.Storage changed' }
    # Vanilla double-click guards the RPC does not enforce; QuickStackRules mirrors them.
    $double = ($signatures['System.Void InventoryUI::OnItemDoubleClickBP(Item)'].Body.Instructions | ForEach-Object { "$($_.Operand)" }) -join "`n"
    foreach ($needle in @('ItemData::quest','ContainerNet::isGeneric','ItemData::pet','ContainerNet::isPet','ItemManager::MoveItemToContServerRpc')) {
        if (!$double.Contains($needle)) { throw "Vanilla double-click rule changed: $needle" }
    }
    $double = ($signatures['System.Void InventoryUI::OnItemDoubleClickBP(Item)'].Body.Instructions | ForEach-Object { $_.OpCode.Name + ' ' + $_.Operand }) -join "`n"
    if (!$double.Contains('ldc.i4 162')) { throw 'Vanilla move sound changed' }
    # Host drops ids that already left the source, so a repeated press cannot duplicate.
    $rpc = ($signatures['System.Void ItemManager::MoveItemToContServerRpc(System.UInt32,System.UInt16,System.UInt16)'].Body.Instructions | ForEach-Object { "$($_.Operand)" }) -join "`n"
    foreach ($needle in @('ContainerNet::GetItemById','ContainerNet::AddNewItem(Item,System.UInt16&,System.Boolean)','ContainerNet::SetItemById','ContainerNet::RemoveItemById')) {
        if (!$rpc.Contains($needle)) { throw "Move RPC host logic changed: $needle" }
    }
    $enable = ($signatures['System.Void InventoryUI::OnEnable()'].Body.Instructions | ForEach-Object { $_.OpCode.Name + ' ' + $_.Operand }) -join "`n"
    if (!$enable.Contains("ldc.i4.s 9`nclt")) { throw 'Backpack container id threshold (9) changed' }
    $keybinds = $types['AppSettings'].NestedTypes | Where-Object Name -eq 'Keybinds'
    foreach ($ctor in ($keybinds.Methods | Where-Object Name -eq '.ctor')) {
        foreach ($i in $ctor.Body.Instructions) { if ($i.OpCode.Name -like 'ldc.i4*' -and "$($i.Operand)" -eq '113') { throw 'A native keybind now defaults to Q' } }
    }
    $quick = $mod.MainModule.Types | Where-Object Name -eq 'ChestQuickStack'
    if (!$quick) { throw 'ChestQuickStack missing from combined DLL' }
    foreach ($name in @('Initialize','Update','Deposit','EnsureButton','Typing','OnClick','Layout')) {
        if (!($quick.Methods | Where-Object Name -eq $name)) { throw "Missing quick stack method: $name" }
    }
    if (!($mod.MainModule.Types | Where-Object Name -eq 'QuickStackRules')) { throw 'QuickStackRules missing from combined DLL' }
    $awake = (($mod.MainModule.Types | Where-Object Name -eq 'TeammateHealthBars').Methods | Where-Object Name -eq 'Awake').Body.Instructions | ForEach-Object { "$($_.Operand)" }
    if (!($awake -match 'ChestQuickStack::Initialize')) { throw 'Plugin does not start ChestQuickStack' }
    foreach ($name in @('HorseStable','ItemStacks','MusketScope','YouTubeJukeboxPanel')) {
        if (!($mod.MainModule.Types | Where-Object Name -eq $name)) { throw "Combined pack lost $name" }
    }
    Write-Output 'PASS: quick stack game APIs, vanilla guards, host move logic, free Q key and combined-pack wiring'
} finally { $game.Dispose(); $mod.Dispose() }
