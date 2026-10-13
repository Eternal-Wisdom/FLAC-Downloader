param(
    [Parameter(Mandatory=$true)][string]$ReaCli,
    [string]$AppPath,
    [string]$ReportPath
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $AppPath){$AppPath=Join-Path $root 'build/FLAC-Downloader.exe'}
if(-not $ReportPath){$ReportPath=Join-Path $root 'build/rea-release-summary.json'}
$ReaCli=(Resolve-Path -LiteralPath $ReaCli).Path
$AppPath=(Resolve-Path -LiteralPath $AppPath).Path
# REA reads the compiled artifact; it does not launch the target app.
function Inspect([string]$operation){
    $result=& node $ReaCli $operation $AppPath --json
    if($LASTEXITCODE -ne 0){throw ('REA inspection failed: '+$operation)}
    $evidence=($result -join "`n") | ConvertFrom-Json
    $observed=$evidence.normalized_result
    if($observed.coverage.state -ne 'complete'){throw ('Incomplete REA coverage: '+$operation)}
    if($observed.artifact.sha256 -ne (Get-FileHash -LiteralPath $AppPath).Hash.ToLowerInvariant()){throw 'REA evidence does not match the inspected file.'}
    return $observed
}
$artifact=Inspect 'inspect-managed-artifact'
$native=Inspect 'inspect-managed-native-boundaries'
if($artifact.assembly.name -ne 'FLAC-Downloader' -or $artifact.classification.implementation -ne 'cil' -or $artifact.classification.managed_architecture -ne 'x86_64'){throw 'Unexpected release assembly or architecture.'}
$allowed=@('System.Windows.Forms','mscorlib','System.Drawing','System.Net.Http','System','System.Core','System.Web.Extensions','System.Security','Microsoft.VisualBasic')
$references=@($artifact.references | ForEach-Object {$_.name})
foreach($name in $references){if($allowed -notcontains $name){throw ('Review unexpected assembly dependency: '+$name)}}
$imports=@($native.pinvoke_imports)
if($imports.Count -ne 1 -or $imports[0].import_scope_name -ne 'user32.dll' -or $imports[0].import_name -ne 'SendMessage' -or $imports[0].char_set -ne 'unicode' -or $imports[0].call_convention -ne 'winapi'){throw 'Review changed native import declarations.'}
# Public-safe summary: never copy raw REA evidence, which includes local paths.
$report=@{assembly=$artifact.assembly.name;version=$artifact.assembly.version;sha256=$artifact.artifact.sha256;architecture=$artifact.classification.managed_architecture;references=$references;nativeImport='user32.dll:SendMessage (Unicode, WinAPI)';coverage='complete metadata and native declarations';limits='Static declarations only; not a runtime, privacy, throughput, or full method audit.'}
$parent=Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output ('REA release inspection passed for '+$artifact.assembly.version+'. Summary excludes local paths.')
