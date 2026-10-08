$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$exe=Join-Path $build 'FLAC-Downloader.exe'
if(-not (Test-Path -LiteralPath $exe)){throw 'Run scripts/Build.ps1 first.'}
$result=Join-Path $build 'test-results.txt'
$xmlReport=Join-Path $build 'test-results.xml'
$receipt=Join-Path $build 'tested-build.json'
foreach($stale in @($receipt,$result,$xmlReport)){if(Test-Path -LiteralPath $stale){Remove-Item -LiteralPath $stale}}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$runner=Join-Path $build 'FLAC-Downloader.Tests.exe'
$references=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Xml.dll','Microsoft.CSharp.dll',$exe)
$options=@('/nologo','/target:exe','/platform:x64','/optimize+','/langversion:5','/codepage:65001','/main:TestRunner',('/out:'+$runner))
foreach($reference in $references){$options+='/reference:'+$reference}
$files=Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName
& $compiler @options @files
if($LASTEXITCODE -ne 0){throw 'Test runner compilation failed.'}
$oldRevision=$env:FLAC_TEST_REVISION;$oldTree=$env:FLAC_TEST_TREE
try {
    $env:FLAC_TEST_REVISION='unknown';$env:FLAC_TEST_TREE='unknown'
    if(Get-Command git -ErrorAction SilentlyContinue){
        $revision=& git -C $root rev-parse HEAD 2>$null
        if($LASTEXITCODE -eq 0){$env:FLAC_TEST_REVISION=$revision;$tree=& git -C $root status --porcelain 2>$null;$env:FLAC_TEST_TREE=if($tree){'modified'}else{'clean'}}
    }
    $process=Start-Process -FilePath $runner -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $build 'test-output.log') -RedirectStandardError (Join-Path $build 'test-errors.log')
    if(-not $process.WaitForExit(180000)){$process.Kill();$process.WaitForExit();throw 'Offline tests exceeded three minutes; no tested-build receipt created.'}
} finally {$env:FLAC_TEST_REVISION=$oldRevision;$env:FLAC_TEST_TREE=$oldTree}
if(-not (Test-Path -LiteralPath $result) -or -not (Test-Path -LiteralPath $xmlReport)){throw 'Offline tests produced no complete result report.'}
$report=Get-Content -LiteralPath $result -Raw
Write-Output $report
[xml]$junit=Get-Content -LiteralPath $xmlReport -Raw
$suite=$junit.testsuites.testsuite
if($process.ExitCode -ne 0 -or $report -notmatch '^PASS:' -or [int]$suite.tests -le 0 -or [int]$suite.failures -ne 0 -or [int]$suite.skipped -ne 0){throw 'Offline tests failed; see test-results.xml.'}
$appHash=(Get-FileHash -LiteralPath $exe).Hash
$engineHash=(Get-FileHash -LiteralPath (Join-Path $build 'engine/sockseek.exe')).Hash
if(($suite.properties.property | Where-Object name -eq 'app-sha256').value -ne $appHash -or ($suite.properties.property | Where-Object name -eq 'engine-sha256').value -ne $engineHash){throw 'Test report does not match the current binaries.'}
@{app=(Get-FileHash -LiteralPath $exe).Hash;engine=(Get-FileHash -LiteralPath (Join-Path $build 'engine/sockseek.exe')).Hash;runner=(Get-FileHash -LiteralPath $runner).Hash;report=(Get-FileHash -LiteralPath $xmlReport).Hash} | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
