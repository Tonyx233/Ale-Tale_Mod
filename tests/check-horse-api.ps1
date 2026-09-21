param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$checks=@(
    @('ItemManager','Awake',0,'System.Void'),
    @('InventoryItemUseManager','UseInventoryItem',4,'System.Void'),
    @('SaveManager','LoadGame',2,'System.Boolean'),
    @('SaveManager','NewGame',1,'System.Void'),
    @('PlayerMovement','HandleCharacterMovement',0,'System.Void')
)
foreach($name in @('GetJumpInputDown','GetJumpInputHeld','GetDashInputDown','GetCrouchInputDown','GetCrouchInputHeld','GetFireInputDown','GetFireInputHeld','GetFireInputReleased','GetAimInputDown','GetAimInputHeld','GetAimInputReleased','GetDropInputDown','GetAutoRunInputDown','GetUseInputDown','GetUseInput','GetUseInputUp')) {
    $checks+=,@('PlayerInput',$name,0,'System.Boolean')
}
foreach($check in $checks){
    $type=$game.MainModule.Types|Where-Object Name -eq $check[0]
    $methods=@($type.Methods|Where-Object Name -eq $check[1])
    if($methods.Count -ne 1 -or $methods[0].Parameters.Count -ne $check[2] -or $methods[0].ReturnType.FullName -ne $check[3]){throw ('Harmony target mismatch: '+($check -join ' '))}
}
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path (Split-Path $PSScriptRoot) 'bin\Tony.TeammateHealthBars.dll'))
if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Horse.model.json')){throw 'Missing embedded horse model'}
if(!($mod.MainModule.Resources|Where-Object Name -eq 'Tony.Horse.model2.json')){throw 'Missing embedded Horse 2 model'}
if($mod.MainModule.Types|Where-Object Name -eq 'TavernCart'){throw 'Old cart code remains in DLL'}
foreach($type in @('HorseStable','TavernHorse','HorseModel','HorseGait','HorseRiderPose','HorseSeats')){
    if(!($mod.MainModule.Types|Where-Object Name -eq $type)){throw "Missing horse type: $type"}
}
'PASS: '+$checks.Count+' Harmony signatures, embedded model, horse components, no old cart type'
$mod.Dispose();$game.Dispose()
