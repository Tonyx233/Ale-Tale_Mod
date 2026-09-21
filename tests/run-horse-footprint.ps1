$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $root 'Horse\TavernHorse.cs') -Raw
$start=$source.IndexOf('        private Vector3 MountPoint(')
$end=$source.IndexOf('        private bool MountPathBlocked(', $start)
if($start -lt 0 -or $end -le $start){throw 'Cannot locate production footprint methods'}
$generated=Join-Path $root 'bin\HorseFootprintProduction.cs'
Set-Content $generated ('using System; using TonyMods; partial class TavernHorse {'+$source.Substring($start,$end-$start)+'}')
$exe=Join-Path $root 'bin\HorseFootprintTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe ('/out:'+$exe) (Join-Path $PSScriptRoot 'HorseFootprintTests.cs') $generated (Join-Path $root 'Horse\HorseSeats.cs')
if($LASTEXITCODE -ne 0){throw 'Footprint test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Footprint regression failed'}
