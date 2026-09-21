param([switch]$WebView, [switch]$Playback, [switch]$Speaker, [switch]$Playlist, [switch]$QueueOnly,
    [string]$VideoId = 'M7lc1UVf-VE',
    [string]$PlaylistId = 'PLdrk_BM8q45oxliginXQPrQuoTk2ppFjV',
    [string]$PlaylistVideoId = 'pEdxU1F-FE8')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
if (!$QueueOnly) {
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\UrlTests.exe')) (Join-Path $PSScriptRoot 'UrlTests.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& (Join-Path $root 'bin\UrlTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'URL tests failed' }
}
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\SpeakerTests.exe')) (Join-Path $PSScriptRoot 'SpeakerTests.cs') (Join-Path $root 'YouTubeJukebox\JukeboxState.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Speaker test build failed' }
& (Join-Path $root 'bin\SpeakerTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Speaker state tests failed' }
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\PlaylistTests.exe')) (Join-Path $PSScriptRoot 'PlaylistTests.cs') (Join-Path $root 'YouTubeJukebox\JukeboxState.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Playlist test build failed' }
& (Join-Path $root 'bin\PlaylistTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Playlist state tests failed' }
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $root 'bin\SpeakerNetworkTests.exe')) (Join-Path $PSScriptRoot 'SpeakerNetworkTests.cs') (Join-Path $root 'YouTubeJukebox\JukeboxState.cs') (Join-Path $root 'YouTubeJukebox\JukeboxSpeaker.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Speaker network test build failed' }
& (Join-Path $root 'bin\SpeakerNetworkTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Speaker network tests failed' }
if (!$QueueOnly) {
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\HorseTests.exe')) (Join-Path $PSScriptRoot 'HorseTests.cs') (Join-Path $root 'Horse\HorseSeats.cs') (Join-Path $root 'Horse\HorseGait.cs')
if ($LASTEXITCODE -ne 0) { throw 'Seat test build failed' }
& (Join-Path $root 'bin\HorseTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Seat tests failed' }
}
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
if ($Speaker) {
    $stdout = Join-Path $root 'bin\speaker-test.log'
    $process = Start-Process (Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe') -ArgumentList '--speaker-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $root 'bin\speaker-error.log')
    if (!$process.WaitForExit(55000)) { $process.Kill(); throw 'Speaker playback timed out' }
    Get-Content $stdout
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $stdout -SimpleMatch 'SPEAKER_TEST=hidden-progress,pause,seek,volume,resume,reopen,stop:PASS' -Quiet)) { throw 'Speaker playback failed' }
    & $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/out:'+(Join-Path $root 'bin\SpeakerPairTests.exe')) (Join-Path $PSScriptRoot 'SpeakerPairTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Speaker pair build failed' }
    $stdout = Join-Path $root 'bin\pair-test.log'
    $process = Start-Process (Join-Path $root 'bin\SpeakerPairTests.exe') -ArgumentList ('"'+(Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe')+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $root 'bin\pair-error.log')
    if (!$process.WaitForExit(58000)) { $process.Kill(); throw 'Speaker pair timed out' }
    Get-Content $stdout
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $stdout -SimpleMatch 'SPEAKER_PAIR_TEST=late-join,clock,independent-volume,pause,cleanup:PASS' -Quiet)) { throw 'Speaker pair failed' }
}
if ($Playlist) {
    if ($PlaylistId -notmatch '^[A-Za-z0-9_-]{10,150}$' -or $PlaylistVideoId -notmatch '^[A-Za-z0-9_-]{11}$') { throw 'Invalid playlist test identifiers' }
    & $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $root 'bin\PlaylistPlaybackTests.exe')) (Join-Path $PSScriptRoot 'PlaylistPlaybackTests.cs') (Join-Path $root 'YouTubeJukebox\JukeboxState.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Playlist playback test build failed' }
    $stdout = Join-Path $root 'bin\playlist-pair.log'
    $process = Start-Process (Join-Path $root 'bin\PlaylistPlaybackTests.exe') -ArgumentList @(('"'+(Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe')+'"'), $PlaylistId, $PlaylistVideoId) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $root 'bin\playlist-pair-error.log')
    if (!$process.WaitForExit(110000)) { $process.Kill(); throw 'Playlist pair timed out' }
    Get-Content $stdout
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $stdout -SimpleMatch 'PLAYLIST_PAIR_TEST=non-interrupting-import,queue-sort,hidden-host,ended,shared-next:PASS' -Quiet)) { throw 'Playlist pair failed' }
}
