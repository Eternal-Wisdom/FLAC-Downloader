$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$exe=Join-Path $build 'FLAC-Downloader.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run scripts/Build.ps1 first.'}
$process=Start-Process -FilePath $exe -ArgumentList '--self-test' -PassThru -WindowStyle Hidden
if(-not $process.WaitForExit(180000)){throw 'Offline tests exceeded three minutes.'}
$result=Join-Path $build 'test-results.txt'
if(-not (Test-Path -LiteralPath $result)){throw 'Offline tests produced no result report.'}
$report=Get-Content -LiteralPath $result -Raw
Write-Output $report
if($process.ExitCode -ne 0 -or $report -notmatch '^PASS:'){throw 'Offline tests failed; see the report above.'}
