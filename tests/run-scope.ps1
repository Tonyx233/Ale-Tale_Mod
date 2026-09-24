param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\ScopeMathTests.exe'
& $compiler /nologo /target:exe ('/out:'+$output) (Join-Path $PSScriptRoot 'ScopeMathTests.cs') (Join-Path $root 'GunScope\ScopeMath.cs')
if ($LASTEXITCODE -ne 0) { throw 'Scope test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Scope math checks failed' }
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll')))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed\Assembly-CSharp.dll'))
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $root 'bin\Tony.TeammateHealthBars.dll'))
try {
    foreach ($check in @(@('GunTool','Update'),@('GunTool','RaycastShot'),@('PlayerInput','GetLookInput'),@('PlayerInput','GetAimInputDown'))) {
        $type = $game.MainModule.Types | Where-Object Name -eq $check[0]
        $method = @($type.Methods | Where-Object Name -eq $check[1])
        if ($method.Count -ne 1 -or $method[0].Parameters.Count -ne 0) { throw ('Scope API changed: '+($check -join '.')) }
    }
    $gun = $game.MainModule.Types | Where-Object Name -eq GunTool
    foreach ($field in @('_state','_isReloading')) {
        if (!($gun.Fields | Where-Object Name -eq $field)) { throw "Missing scope state field: $field" }
    }
    $scope = $mod.MainModule.Types | Where-Object Name -eq MusketScope
    if (!$scope) { throw 'Scope missing from combined DLL' }
    foreach ($name in @('GunUpdated','BeforeRaycast','AfterRaycast','ScaleLook','OnDisable','OnDestroy','OnApplicationFocus')) {
        if (!($scope.Methods | Where-Object Name -eq $name)) { throw "Missing scope handler: $name" }
    }
    # Held tools are Instantiate() clones ("Musket_fp(Clone)"), so the filter cannot use the prefab name.
    $spawn = (($game.MainModule.Types | Where-Object Name -eq PlayerInventory).Methods | Where-Object Name -eq OnInventoryAdd).Body.Instructions | ForEach-Object ToString
    if (!($spawn -match 'ItemData::fpPrefab') -or !($spawn -match 'Object::Instantiate')) { throw 'Native fp tool spawn changed; re-check musket identification' }
    $body = ($scope.Methods | Where-Object Name -eq GunUpdated).Body.Instructions | ForEach-Object ToString
    if (!($body -match 'ldstr "MusketRoot/Musket/Musket1_2_1"')) { throw 'Musket-only mesh filter missing; crossbow must remain unchanged' }
    if ($body -match 'get_name') { throw 'Regression: GunUpdated filters by GameObject name, but held guns are named Musket_fp(Clone)' }
    foreach ($name in @('HorseStable','ItemStacks','YouTubeJukeboxPanel')) {
        if (!($mod.MainModule.Types | Where-Object Name -eq $name)) { throw "Combined pack lost $name" }
    }
    Write-Output 'PASS: scope targets, lifecycle hooks, musket-only filter and combined-pack components'
} finally { $game.Dispose(); $mod.Dispose() }
