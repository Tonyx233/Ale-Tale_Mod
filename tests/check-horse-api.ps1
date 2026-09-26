param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference='Stop'
# Load from bytes: Add-Type -Path refuses the downloaded (Zone.Identifier) BepInEx copy of Cecil.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$checks=@(
    @('ItemManager','Awake',0,'System.Void'),
    @('InventoryItemUseManager','UseInventoryItem',4,'System.Void'),
    @('SaveManager','LoadGame',2,'System.Boolean'),
    @('SaveManager','NewGame',1,'System.Void'),
    @('PlayerMovement','HandleCharacterMovement',0,'System.Void'),
    @('PlayerInput','GetSelectSlotInput',0,'System.Byte')
)
foreach($name in @('GetJumpInputDown','GetJumpInputHeld','GetDashInputDown','GetCrouchInputDown','GetCrouchInputHeld','GetFireInputDown','GetFireInputHeld','GetFireInputReleased','GetAimInputDown','GetAimInputHeld','GetAimInputReleased','GetDropInputDown','GetAutoRunInputDown','GetUseInputDown','GetUseInput','GetUseInputUp')) {
    $checks+=,@('PlayerInput',$name,0,'System.Boolean')
}
foreach($check in $checks){
    $type=$game.MainModule.Types|Where-Object Name -eq $check[0]
    $methods=@($type.Methods|Where-Object Name -eq $check[1])
    if($methods.Count -ne 1 -or $methods[0].Parameters.Count -ne $check[2] -or $methods[0].ReturnType.FullName -ne $check[3]){throw ('Harmony target mismatch: '+($check -join ' '))}
}
# Native X/remove hold and furniture-style inventory return used by horse storage.
$storeApi=@(
    'System.Void PlayerInventory::RemoveChecks()',
    'System.Void Interactive::Interact(Interactive/Event,System.UInt16,System.UInt32)',
    'System.Void Interactive::set_ObjectTitle(System.String)',
    'System.Void Interactive::set_RemoveDescr(System.String)',
    'System.Void Interactive::set_IsRemoveAvailable(System.Boolean)',
    'System.Boolean Interactive::get_IsRemoveAvailable()',
    'System.Boolean ContainerNet::AddNewItem(Item,System.UInt16&,System.Boolean)',
    'System.Boolean ItemManager::GetItemData(System.UInt32,ItemData&)',
    'System.Boolean ContainerManager::GetPlayerContainer(System.UInt64,ContainerNet&)'
)
$signatures=@{}
foreach($type in $game.MainModule.Types){ foreach($method in $type.Methods){ $signatures[$method.FullName]=$true } }
foreach($signature in $storeApi){ if(!$signatures.ContainsKey($signature)){throw "Horse store API missing: $signature"} }
foreach($field in @(@('Interactive','layer'),@('Interactive','removeHoldTime'),@('Interactive','onInteract'),@('PlayerInventory','interactibleLM'),@('FurnitureManager','forbidClientsRemoveFurniture'),@('AppSettingsManager','appSettings'))){
    $type=$game.MainModule.Types|Where-Object Name -eq $field[0]
    if(!($type.Fields|Where-Object { $_.Name -eq $field[1] -and $_.IsPublic })){throw ('Horse store field missing: '+($field -join '.'))}
}
$interactive=$game.MainModule.Types|Where-Object Name -eq 'Interactive'
$event=$interactive.NestedTypes|Where-Object Name -eq 'Event'
if(($event.Fields|Where-Object Name -eq 'RemoveHold').Constant -ne 128){throw 'Interactive.Event.RemoveHold changed'}
$keybinds=($game.MainModule.Types|Where-Object Name -eq 'AppSettings').NestedTypes|Where-Object Name -eq 'Keybinds'
if(!($keybinds.Fields|Where-Object Name -eq 'remove')){throw 'Native remove keybind missing'}
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $PSScriptRoot) 'bin\Tony.TeammateHealthBars.dll'))
if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Horse.model.json')){throw 'Missing embedded horse model'}
if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Horse.model2.json')){throw 'Missing embedded Horse 2 model'}
if($mod.MainModule.Types|Where-Object Name -eq 'TavernCart'){throw 'Old cart code remains in DLL'}
foreach($type in @('HorseStable','TavernHorse','HorseModel','HorseGait','HorseRiderPose','HorseSeats')){
    if(!($mod.MainModule.Types|Where-Object Name -eq $type)){throw "Missing horse type: $type"}
}
if(!(($mod.MainModule.Types|Where-Object Name -eq 'TavernHorse').Methods|Where-Object Name -eq 'PickupInteract')){throw 'Missing horse X-store handler'}
'PASS: '+$checks.Count+' Harmony signatures, '+$storeApi.Count+' horse store APIs, embedded model, horse components, no old cart type'
$mod.Dispose();$game.Dispose()
