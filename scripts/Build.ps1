$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$build=Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
& (Join-Path $root 'src\Build.ps1') -OutputDirectory $build
& (Join-Path $PSScriptRoot 'Setup-Engine.ps1')
