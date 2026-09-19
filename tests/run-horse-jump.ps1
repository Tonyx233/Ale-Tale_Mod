$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'Horse\TavernHorse.cs') -Raw
$start = $source.IndexOf('        private static bool FilterAction(')
$end = $source.IndexOf('        private void Tell(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production input filter' }
$generated = Join-Path $root 'bin\HorseJumpProduction.cs'
$settleStart = $source.IndexOf('        private void SettleWithoutDriver(')
$settleEnd = $source.IndexOf('        private static bool BeforeMove(', $settleStart)
if ($settleStart -lt 0 -or $settleEnd -le $settleStart) { throw 'Cannot locate production settling method' }
Set-Content $generated ('using System; using System.Reflection; partial class TavernHorse {' + $source.Substring($start, $end-$start) + $source.Substring($settleStart,$settleEnd-$settleStart) + '}')
$exe = Join-Path $root 'bin\HorseJumpTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe ('/out:' + $exe) (Join-Path $PSScriptRoot 'HorseJumpTests.cs') $generated
if ($LASTEXITCODE -ne 0) { throw 'Jump test compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Jump regression failed' }
