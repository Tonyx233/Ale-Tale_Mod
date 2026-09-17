$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $root 'Horse\TavernHorse.cs') -Raw
$start=$source.IndexOf('        private bool MountPathBlocked(PlayerNet player)')
$end=$source.IndexOf('        private void Note(', $start)
if($start -lt 0 -or $end -le $start){throw 'Cannot locate production mount-path method'}
$generated=Join-Path $root 'bin\MountPathProduction.cs'
Set-Content $generated ('using TonyMods; partial class TavernHorse {'+$source.Substring($start,$end-$start)+'}')
$exe=Join-Path $root 'bin\MountPathTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe ('/out:'+$exe) (Join-Path $PSScriptRoot 'MountPathTests.cs') $generated (Join-Path $root 'Horse\HorseSeats.cs')
if($LASTEXITCODE -ne 0){throw 'Mount-path test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Mount-path regression failed'}
