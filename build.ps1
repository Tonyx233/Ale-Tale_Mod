param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Ale and Tale Tavern')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$package = Join-Path $PSScriptRoot '.packages\webview2'
if (!(Test-Path (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot '.packages') | Out-Null
    $archive = Join-Path $PSScriptRoot '.packages\webview2.zip'
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2903.40/microsoft.web.webview2.1.0.2903.40.nupkg' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $package -Force
}
$outDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $outDir | Out-Null
$hostDir = Join-Path $outDir 'browser'
New-Item -ItemType Directory -Force $hostDir | Out-Null
Copy-Item (Join-Path $package 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $package 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $package 'runtimes\win-x64\native\WebView2Loader.dll') $hostDir -Force
$helper = Join-Path $hostDir 'Tony.JukeboxBrowser.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$helper) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll ('/reference:'+(Join-Path $hostDir 'Microsoft.Web.WebView2.Core.dll')) ('/reference:'+(Join-Path $hostDir 'Microsoft.Web.WebView2.WinForms.dll')) (Join-Path $PSScriptRoot 'YouTubeJukebox\BrowserHost.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\BrowserQueue.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\JukeboxState.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Browser build failed' }
$managed = Join-Path $GamePath 'Ale and Tale Tavern_Data\Managed'
$refs = @(Get-ChildItem $managed -Filter '*.dll' | ForEach-Object { '/reference:'+$_.FullName })
$refs += '/reference:'+(Join-Path $GamePath 'BepInEx\core\BepInEx.dll')
$refs += '/reference:'+(Join-Path $GamePath 'BepInEx\core\0Harmony.dll')
Copy-Item (Join-Path $package 'LICENSE.txt') (Join-Path $hostDir 'WebView2-LICENSE.txt') -Force
Copy-Item (Join-Path $package 'NOTICE.txt') (Join-Path $hostDir 'WebView2-NOTICE.txt') -Force
$resources = @(Get-ChildItem $hostDir -File | Where-Object Extension -in '.dll','.exe','.txt' | ForEach-Object {'/resource:'+$_.FullName+',Tony.Payload.'+$_.Name})
$resources += '/resource:'+(Join-Path $PSScriptRoot 'Tidefork\Assets\model.json')+',Tony.Tidefork.model.json'
$tideSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'Tidefork') -Filter '*.cs' | ForEach-Object FullName)
$compileArgs = @(Write-Output /nologo /target:library /optimize+ /nostdlib+ ('/out:'+(Join-Path $outDir 'Tony.TeammateHealthBars.dll')) @refs @resources @tideSources (Join-Path $PSScriptRoot 'TeammateHealthBars\Plugin.cs') @(Get-ChildItem (Join-Path $PSScriptRoot 'GunScope') -Filter '*.cs' | ForEach-Object FullName) @(Get-ChildItem (Join-Path $PSScriptRoot 'ItemStacks') -Filter '*.cs' | ForEach-Object FullName) @(Get-ChildItem (Join-Path $PSScriptRoot 'QuickStack') -Filter '*.cs' | ForEach-Object FullName) @(Get-ChildItem (Join-Path $PSScriptRoot 'M4') -Filter '*.cs' | ForEach-Object FullName) (Join-Path $PSScriptRoot 'YouTubeJukebox\YouTubeUrl.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\YouTubeJukeboxPanel.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\JukeboxSpeaker.cs') (Join-Path $PSScriptRoot 'YouTubeJukebox\JukeboxState.cs') @(Get-ChildItem (Join-Path $PSScriptRoot 'Horse') -Filter '*.cs' | ForEach-Object FullName) ('/resource:'+(Join-Path $PSScriptRoot 'Horse\Assets\model.json')+',Tony.Horse.model.json') ('/resource:'+(Join-Path $PSScriptRoot 'Horse\Assets\model2.json')+',Tony.Horse.model2.json') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\model.json')+',Tony.M4.model.json') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\m4-icon.png')+',Tony.M4.icon.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\ammo-icon.png')+',Tony.M4.ammo.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\scope-reddot.png')+',Tony.M4.scope2.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\scope-holo.png')+',Tony.M4.scope3.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\scope-acog.png')+',Tony.M4.scope4.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\scope-brass.png')+',Tony.M4.scope5.png') ('/resource:'+(Join-Path $PSScriptRoot 'M4\Assets\scope-sniper.png')+',Tony.M4.scope6.png'))
$responseFile = Join-Path $outDir 'plugin-build.rsp'
[IO.File]::WriteAllLines($responseFile, @($compileArgs | ForEach-Object { '"' + $_ + '"' }), [Text.Encoding]::UTF8)
& $compiler /noconfig ('@'+$responseFile)
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
& (Join-Path $PSScriptRoot 'Set-ModVersion.ps1') -Path (Join-Path $outDir 'Tony.TeammateHealthBars.dll')
Write-Output (Join-Path $outDir 'Tony.TeammateHealthBars.dll')
