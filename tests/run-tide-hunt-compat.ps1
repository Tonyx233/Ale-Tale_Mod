param(
    [Parameter(Mandatory=$true)][string]$FarmerOwlPath,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'bin'
$managed = Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed'
$refs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$refs += '/reference:' + (Join-Path $GamePath 'BepInEx\core\0Harmony.dll')
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /noconfig /nologo /nostdlib+ /target:exe ('/out:' + (Join-Path $out 'TideHuntCompatibilityTests.exe')) @refs (Join-Path $PSScriptRoot 'TideHuntCompatibilityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compatibility test compilation failed' }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 ('/out:' + (Join-Path $out 'MonoTestHost.exe')) (Join-Path $PSScriptRoot 'MonoTestHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono test host compilation failed' }
Set-Content (Join-Path $out 'MonoTestHost.runtimeconfig.json') '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.14"}}}'
$previousGame = $env:HORSE_TEST_GAME
$env:HORSE_TEST_GAME = $GamePath
$farmer = (Resolve-Path -LiteralPath $FarmerOwlPath).Path
Push-Location $out
try {
    $result = & dotnet .\MonoTestHost.exe .\TideHuntCompatibilityTests.exe $farmer 2>&1
    $result | Write-Output
    if (($result -join "`n") -notmatch 'PASS: [0-9]+ production transpiler checks') { throw 'Missing successful compatibility result' }
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility regression' }
} finally { Pop-Location; $env:HORSE_TEST_GAME = $previousGame }
