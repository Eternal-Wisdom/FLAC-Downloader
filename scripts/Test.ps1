$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$exe=Join-Path $build 'Playlist FLAC.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run scripts/Build.ps1 first.'}
$process=Start-Process -FilePath $exe -ArgumentList '--self-test' -PassThru -WindowStyle Hidden
if(-not $process.WaitForExit(180000)){throw 'Offline tests exceeded three minutes.'}
$result=Join-Path $build 'test-results.txt'
if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Content -LiteralPath $result -Raw) -notmatch '^PASS:'){throw ('Offline tests failed. Inspect '+$result)}
Get-Content -LiteralPath $result
