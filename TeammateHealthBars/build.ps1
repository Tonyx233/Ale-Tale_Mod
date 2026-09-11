param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
# Compatibility entry point: build the complete single-DLL mod pack.
& (Join-Path (Split-Path $PSScriptRoot) 'build.ps1') -GamePath $GamePath
