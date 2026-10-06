$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $root 'third-party\sockseek.json') -Raw | ConvertFrom-Json
$engine=Join-Path $root 'build\engine'
$exe=Join-Path $engine 'sockseek.exe'
New-Item -ItemType Directory -Force -Path $engine | Out-Null
if(-not (Test-Path -LiteralPath $exe) -or (Get-FileHash -LiteralPath $exe).Hash -ne $manifest.exeSha256) {
    $archive=Join-Path $root 'build\sockseek-upstream.zip'
    if(-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $manifest.archiveSha256) {Invoke-WebRequest -UseBasicParsing -Uri $manifest.url -OutFile $archive}
    if((Get-FileHash -LiteralPath $archive).Hash -ne $manifest.archiveSha256){throw 'Upstream archive checksum mismatch.'}
    $extract=Join-Path $root ('build\engine-extract-'+[Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $extract
    $candidate=@(Get-ChildItem -LiteralPath $extract -Filter sockseek.exe -Recurse)
    if($candidate.Count -ne 1 -or (Get-FileHash -LiteralPath $candidate[0].FullName).Hash -ne $manifest.exeSha256){throw 'Upstream executable checksum mismatch.'}
    Copy-Item -LiteralPath $candidate[0].FullName -Destination $exe -Force
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $engine 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $root 'third-party\sockseek-3.0.5-source.zip') -Destination $engine -Force
Write-Output 'Pinned Sockseek engine verified.'
