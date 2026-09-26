param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference='Stop'
$version=(Get-Content (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'version.txt must contain major.minor.patch'}
$target=(Resolve-Path -LiteralPath $Path).Path
$temp=Join-Path $env:TEMP ('tony-version-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$source=Join-Path $temp 'Version.cs'
$dll=Join-Path $temp 'TONY_BIG_SET.dll'
@"
using System.Reflection;
[assembly: AssemblyTitle("Tony Ale & Tale Mods")]
[assembly: AssemblyProduct("Tony Ale & Tale Mods")]
[assembly: AssemblyDescription("Health panel, YouTube jukebox, two/five-seat horses, musket scope, chest quick stack and M4A1 rifle")]
[assembly: AssemblyCompany("Tony")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
public class VersionResource {}
"@ | Set-Content -LiteralPath $source -Encoding UTF8
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library "/out:$dll" $source
if($LASTEXITCODE -ne 0){throw 'Version resource compilation failed'}
if(-not ('TonyVersionNative' -as [type])){
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TonyVersionNative {
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr LoadLibraryEx(string p,IntPtr f,uint flags);
 [DllImport("kernel32",SetLastError=true)] public static extern IntPtr FindResource(IntPtr h,IntPtr name,IntPtr type);
 [DllImport("kernel32")] public static extern uint SizeofResource(IntPtr h,IntPtr r);
 [DllImport("kernel32")] public static extern IntPtr LoadResource(IntPtr h,IntPtr r);
 [DllImport("kernel32")] public static extern IntPtr LockResource(IntPtr r);
 [DllImport("kernel32")] public static extern bool FreeLibrary(IntPtr h);
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr BeginUpdateResource(string p,bool delete);
 [DllImport("kernel32",SetLastError=true)] public static extern bool UpdateResource(IntPtr h,IntPtr type,IntPtr name,ushort lang,byte[] data,uint length);
 [DllImport("kernel32",SetLastError=true)] public static extern bool EndUpdateResource(IntPtr h,bool discard);
}
'@
}
$handle=[TonyVersionNative]::LoadLibraryEx($dll,[IntPtr]::Zero,2)
if($handle -eq [IntPtr]::Zero){throw 'Cannot load version resource'}
try {
 $resource=[TonyVersionNative]::FindResource($handle,[IntPtr]1,[IntPtr]16)
 $size=[TonyVersionNative]::SizeofResource($handle,$resource)
 if($size -eq 0){throw 'Missing version resource'}
 $bytes=New-Object byte[] $size
 [Runtime.InteropServices.Marshal]::Copy([TonyVersionNative]::LockResource([TonyVersionNative]::LoadResource($handle,$resource)),$bytes,0,$bytes.Length)
} finally {[void][TonyVersionNative]::FreeLibrary($handle)}
$update=[TonyVersionNative]::BeginUpdateResource($target,$false)
if($update -eq [IntPtr]::Zero){throw 'Cannot update DLL version resource'}
if(-not [TonyVersionNative]::UpdateResource($update,[IntPtr]16,[IntPtr]1,0,$bytes,$size)){
 [void][TonyVersionNative]::EndUpdateResource($update,$true);throw 'Version resource update failed'
}
if(-not [TonyVersionNative]::EndUpdateResource($update,$false)){throw 'Version resource commit failed'}
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo($target)
if($info.FileVersion -ne "$version.0" -or $info.ProductVersion -ne $version){throw 'Version verification failed'}
"Verified: $($info.FileVersion) / $($info.ProductVersion) / $($info.ProductName)"
