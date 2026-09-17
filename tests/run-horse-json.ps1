param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'bin'
$managed = Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed'
$refs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /noconfig /nologo /nostdlib+ /target:exe ('/out:' + (Join-Path $out 'HorseJsonTests.exe')) @refs (Join-Path $PSScriptRoot 'HorseJsonTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Horse JSON test compilation failed' }
Set-Content (Join-Path $out 'HorseJsonTests.runtimeconfig.json') '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.14"}}}'
$previousGame = $env:HORSE_TEST_GAME
$env:HORSE_TEST_GAME = $GamePath
Push-Location $out
try {
    & dotnet .\HorseJsonTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Horse JSON tests failed or runtime was blocked' }
} finally {
    Pop-Location
    $env:HORSE_TEST_GAME = $previousGame
}
