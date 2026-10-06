$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
$dist=Join-Path $root 'dist'
$version='1.11.0'
if(-not (Test-Path -LiteralPath (Join-Path $build 'Playlist FLAC.exe'))){throw 'Build the app first.'}
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$stage=Join-Path $build ('package-'+[Guid]::NewGuid().ToString('N'))
$app=Join-Path $stage 'Playlist FLAC'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'Playlist FLAC.exe') -Destination $app
Copy-Item -LiteralPath (Join-Path $build 'engine') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $app -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $app
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md'),(Join-Path $root 'AI-DISCLOSURE.md') -Destination $app
$guide="Playlist FLAC $version`r`nMADE USING AI - Created with extensive OpenAI Codex assistance.`r`n`r`nExtract this entire folder and open Playlist FLAC.exe.`r`nEnter your own Soulseek account, import a CSV or configure Spotify, choose where music goes, then Download FLAC.`r`nSee docs/QUICKSTART.md for help. Full source and release instructions are in the accompanying source ZIP.`r`nPrivate settings are created in state/ on first use; no credentials or music are bundled.`r`n"
[IO.File]::WriteAllText((Join-Path $app 'START HERE.txt'),$guide)
$portable=Join-Path $dist "playlist-flac-$version-windows-x64.zip"
Compress-Archive -LiteralPath $app -DestinationPath $portable -Force
$source=Join-Path $stage 'playlist-flac-source'
New-Item -ItemType Directory -Force -Path $source | Out-Null
foreach($name in @('src','tests','scripts','docs','examples','third-party','.github','README.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md','AI-DISCLOSURE.md','.gitignore','.gitattributes')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source -Recurse}
$sourceZip=Join-Path $dist "playlist-flac-$version-source.zip"
# .NET ZIP includes dotfiles, including GitHub workflows and ignore rules.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path -LiteralPath $sourceZip){Remove-Item -LiteralPath $sourceZip}
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$lines=@($portable,$sourceZip) | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$lines)
Write-Output 'Source and portable release packages created without user state or music.'
