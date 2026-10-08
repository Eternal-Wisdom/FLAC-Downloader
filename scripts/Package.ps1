$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$dist=Join-Path $root 'dist'
$version='1.13.0'
if(-not (Test-Path -LiteralPath (Join-Path $build 'FLAC-Downloader.exe'))){throw 'Build the app first.'}
$receiptPath=Join-Path $build 'tested-build.json'
if(-not (Test-Path -LiteralPath $receiptPath)){throw 'Run scripts/Test.ps1 successfully before packaging.'}
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if(-not $receipt.report -or $receipt.report -ne (Get-FileHash -LiteralPath (Join-Path $build 'test-results.xml')).Hash){throw 'The detailed test report changed or is missing. Run scripts/Test.ps1 again.'}
if($receipt.app -ne (Get-FileHash -LiteralPath (Join-Path $build 'FLAC-Downloader.exe')).Hash -or $receipt.engine -ne (Get-FileHash -LiteralPath (Join-Path $build 'engine/sockseek.exe')).Hash){throw 'The application or engine changed after testing. Run scripts/Test.ps1 again.'}
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$stage=Join-Path $build ('package-'+[Guid]::NewGuid().ToString('N'))
$app=Join-Path $stage 'FLAC-Downloader'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'FLAC-Downloader.exe') -Destination $app
Copy-Item -LiteralPath (Join-Path $build 'engine') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $app
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md'),(Join-Path $root 'AI-DISCLOSURE.md') -Destination $app
$guide="FLAC-Downloader $version`r`nMADE USING AI - Created with extensive OpenAI Codex assistance.`r`n`r`nExtract this entire folder and open FLAC-Downloader.exe.`r`nEnter your own Soulseek account, import a CSV or configure Spotify, choose where music goes, then Download FLAC.`r`nNew to Soulseek? Read docs/SOULSEEK-ACCOUNT.md to create a network account.`r`nSee docs/QUICKSTART.md for help. Full source and release instructions are in the accompanying source ZIP.`r`nPrivate settings are created in state/ on first use; no credentials or music are bundled.`r`n"
[IO.File]::WriteAllText((Join-Path $app 'START HERE.txt'),$guide)
$portable=Join-Path $dist "flac-downloader-$version-windows-x64.zip"
Compress-Archive -LiteralPath $app -DestinationPath $portable -Force
$source=Join-Path $stage 'flac-downloader-source'
New-Item -ItemType Directory -Force -Path $source | Out-Null
foreach($name in @('src','tests','scripts','docs','examples','third-party','.github','README.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md','AI-DISCLOSURE.md','.gitignore','.gitattributes')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source -Recurse}
$sourceZip=Join-Path $dist "flac-downloader-$version-source.zip"
# .NET ZIP includes dotfiles, including GitHub workflows and ignore rules.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $sourceZip){Remove-Item -LiteralPath $sourceZip}
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$lines=@($portable,$sourceZip) | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$lines)
Write-Output 'Source and portable release packages created without user state or music.'
