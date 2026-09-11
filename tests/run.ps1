param([switch]$WebView)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null
& $compiler /nologo /target:exe ('/out:'+(Join-Path $root 'bin\UrlTests.exe')) (Join-Path $PSScriptRoot 'UrlTests.cs') (Join-Path $root 'YouTubeJukebox\YouTubeUrl.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& (Join-Path $root 'bin\UrlTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'URL tests failed' }
if ($WebView) {
    $stdout = Join-Path $root 'bin\webview-test.log'
    $stderr = Join-Path $root 'bin\webview-test-error.log'
    $process = Start-Process (Join-Path $root 'bin\browser\Tony.JukeboxBrowser.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (!$process.WaitForExit(15000)) { $process.Kill(); throw 'WebView test timed out' }
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0) { throw 'WebView test failed' }
}
