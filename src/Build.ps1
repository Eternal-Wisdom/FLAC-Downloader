param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$appDirectory = Split-Path -Parent $sourceDirectory
if (-not $OutputDirectory) { $OutputDirectory=$appDirectory }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$files = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | ForEach-Object FullName
$testDirectory=Join-Path $appDirectory 'tests'
if(Test-Path -LiteralPath $testDirectory) { $files += Get-ChildItem -LiteralPath $testDirectory -Filter '*.cs' | ForEach-Object FullName }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll','System.Net.Http.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll')
$references += Join-Path (Split-Path -Parent $compiler) 'Microsoft.VisualBasic.dll'
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/langversion:5','/codepage:65001',('/out:' + (Join-Path $OutputDirectory 'FLAC-Downloader.exe')),('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')))
$options += '/win32icon:' + (Join-Path $sourceDirectory 'app.ico')
$options += '/resource:' + (Join-Path $sourceDirectory 'app.ico') + ',PlaylistFlac.app.ico'
foreach ($reference in $references) { $options += '/reference:' + $reference }
& $compiler @options @files
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output 'Built FLAC-Downloader.exe.'
