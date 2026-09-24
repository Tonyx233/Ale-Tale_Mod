param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $root 'bin\ItemStackTests.exe'
$cecil=Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll'
& $compiler /nologo /target:exe ('/out:'+$exe) ('/reference:'+$cecil) (Join-Path $PSScriptRoot 'StackTestDoubles.cs') (Join-Path $PSScriptRoot 'ItemStackTests.cs') @(Get-ChildItem (Join-Path $root 'ItemStacks') -Filter '*.cs' | ForEach-Object FullName)
if($LASTEXITCODE -ne 0){throw 'Stack test compilation failed'}
# Use the installed .NET 8 runtime for the standalone metadata reader. The game's
# Harmony build targets Unity/Mono and is not executed by this test host.
Set-Content (Join-Path $root 'bin\ItemStackTests.runtimeconfig.json') '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}'
& 'C:\Program Files\dotnet\dotnet.exe' $exe $GamePath
if($LASTEXITCODE -ne 0){throw 'Stack tests failed'}
