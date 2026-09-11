param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern'
)
$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GamePath)
$managed = Join-Path $gameRoot 'Ale and Tale Tavern_Data\Managed'
$bepinex = Join-Path $gameRoot 'BepInEx\core\BepInEx.dll'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($required in @($managed, $bepinex, $compiler)) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing build dependency: $required" }
}
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$refs = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$refs += '/reference:' + $bepinex
& $compiler /noconfig /nologo /target:library /optimize+ /nostdlib+ ('/out:' + (Join-Path $output 'Tony.TeammateHealthBars.dll')) @refs (Join-Path $PSScriptRoot 'Plugin.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $output 'Tony.TeammateHealthBars.dll')
