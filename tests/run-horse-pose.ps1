$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'bin\HorsePoseTests.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
& $compiler /nologo /target:exe ('/out:' + $output) (Join-Path $PSScriptRoot 'HorsePoseTests.cs') (Join-Path $root 'Horse\HorsePoseMath.cs')
if ($LASTEXITCODE -ne 0) { throw 'Horse pose test build failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Horse pose tests failed' }
