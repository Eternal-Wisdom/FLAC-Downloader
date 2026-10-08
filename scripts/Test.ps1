$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$exe=Join-Path $build 'FLAC-Downloader.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run scripts/Build.ps1 first.'}
$result=Join-Path $build 'test-results.txt'
$receipt=Join-Path $build 'tested-build.json'
if(Test-Path -LiteralPath $receipt){Remove-Item -LiteralPath $receipt}
if(Test-Path -LiteralPath $result){Remove-Item -LiteralPath $result}
$process=Start-Process -FilePath $exe -ArgumentList '--self-test' -PassThru -WindowStyle Hidden
if(-not $process.WaitForExit(180000)){$process.Kill();throw 'Offline tests exceeded three minutes.'}
$result=Join-Path $build 'test-results.txt'
if(-not (Test-Path -LiteralPath $result)){throw 'Offline tests produced no result report.'}
$report=Get-Content -LiteralPath $result -Raw
Write-Output $report
if($process.ExitCode -ne 0 -or $report -notmatch '^PASS:'){throw 'Offline tests failed; see the report above.'}
@{app=(Get-FileHash -LiteralPath $exe).Hash;engine=(Get-FileHash -LiteralPath (Join-Path $build 'engine/sockseek.exe')).Hash} | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
