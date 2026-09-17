param([switch]$WebView, [switch]$Playback, [string]$VideoId = 'M7lc1UVf-VE')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\UrlTests.exe')) (Join-Path $PSScriptRoot 'UrlTests.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& (Join-Path $root 'bin\UrlTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'URL tests failed' }
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\HorseTests.exe')) (Join-Path $PSScriptRoot 'HorseTests.cs') (Join-Path $root 'Horse\HorseSeats.cs') (Join-Path $root 'Horse\HorseGait.cs')
if ($LASTEXITCODE -ne 0) { throw 'Seat test build failed' }
& (Join-Path $root 'bin\HorseTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Seat tests failed' }
if ($WebView) {
    $stdout = Join-Path $root 'bin\webview-test.log'
    $stderr = Join-Path $root 'bin\webview-test-error.log'
    $process = Start-Process (Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'WebView test timed out' }
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0) { throw 'WebView test failed' }
    & $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/out:'+(Join-Path $root 'bin\HostTests.exe')) (Join-Path $PSScriptRoot 'HostTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Host test build failed' }
    $process = Start-Process (Join-Path $root 'bin\HostTests.exe') -ArgumentList ('"'+(Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe')+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'bin\host-test.log')
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw 'Host test timed out' }
    Get-Content (Join-Path $root 'bin\host-test.log')
    if ($process.ExitCode -ne 0) { throw 'Host test failed' }
}
if ($Playback) {
    if ($VideoId -notmatch '^[A-Za-z0-9_-]{11}$') { throw 'Invalid playback test video ID' }
    $stdout = Join-Path $root 'bin\playback-test.log'
    $stderr = Join-Path $root 'bin\playback-test-error.log'
    $process = Start-Process (Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe') -ArgumentList @('--playback-test', $VideoId) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (!$process.WaitForExit(55000)) { $process.Kill(); throw 'Playback test timed out' }
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $stdout -SimpleMatch 'PLAYER_STATE 1' -Quiet)) { throw 'Video did not reach playing state' }
}
