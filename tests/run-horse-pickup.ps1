$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$horse=Get-Content (Join-Path $root 'Horse\TavernHorse.cs') -Raw
$stable=Get-Content (Join-Path $root 'Horse\HorseStable.cs') -Raw
function Slice([string]$text,[string]$from,[string]$to){
    $start=$text.IndexOf($from)
    $end=if($start -lt 0){-1}else{$text.IndexOf($to,$start+1)}
    if($start -lt 0 -or $end -le $start){throw "Cannot locate production code: $from"}
    $text.Substring($start,$end-$start)
}
$constants=[regex]::Match($horse,'(?m)^[ \t]*private const float MountRange[^\r\n]*').Value
if(!$constants){throw 'Cannot locate production horse range constants'}
$generated=Join-Path $root 'bin\HorsePickupProduction.cs'
Set-Content $generated ('using System; using TonyMods; partial class TavernHorse {'+$constants+
    (Slice $horse '        private void ServerRequest(' '        private Vector3 MountPoint(')+
    (Slice $horse '        // Parked body box shared' '        private void Broadcast()')+
    (Slice $horse '        private void UpdatePickup()' '        private static string RemoveKeyName()')+
    '} partial class HorseStable {'+(Slice $stable '        // Host-only X/remove store' '        private void Disconnect()')+'}')
$exe=Join-Path $root 'bin\HorsePickupTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe ('/out:'+$exe) (Join-Path $PSScriptRoot 'HorsePickupTests.cs') $generated (Join-Path $root 'Horse\HorseSeats.cs') (Join-Path $root 'Horse\HorseVariant.cs')
if($LASTEXITCODE -ne 0){throw 'Pickup test compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Pickup regression failed'}
