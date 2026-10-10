param([Parameter(Mandatory=$true)][string]$Decoder)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$Decoder=(Resolve-Path -LiteralPath $Decoder).Path
[Reflection.Assembly]::LoadFrom((Join-Path $root 'build/FLAC-Downloader.exe')) | Out-Null
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('FlacDecoderTest-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $wave=Join-Path $fixture 'tone.wav'
    $writer=[IO.BinaryWriter]::new([IO.File]::Create($wave))
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int]176436)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16)
        $writer.Write([int16]1);$writer.Write([int16]1);$writer.Write([int]44100);$writer.Write([int]88200)
        $writer.Write([int16]2);$writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]176400)
        for($i=0;$i -lt 88200;$i++){$writer.Write([int16](12000*[Math]::Sin(2*[Math]::PI*440*$i/44100)))}
    } finally {$writer.Dispose()}
    $valid=Join-Path $fixture 'valid.flac'
    & $Decoder --silent -o $valid $wave
    if($LASTEXITCODE -ne 0){throw 'Synthetic FLAC encoding failed.'}
    $bytes=[IO.File]::ReadAllBytes($valid)
    $damaged=[byte[]]$bytes.Clone();$damaged[$damaged.Length-10]=$damaged[$damaged.Length-10] -bxor 128
    [IO.File]::WriteAllBytes((Join-Path $fixture 'damaged.flac'),$damaged)
    [IO.File]::WriteAllBytes((Join-Path $fixture 'truncated.flac'),[byte[]]$bytes[0..($bytes.Length-100)])
    $headers=[PlaylistFlac.FlacAudit]::CheckFolder($fixture,[Threading.CancellationToken]::None)
    if($headers.ValidHeaders -ne 3){throw 'Expected three structurally valid headers.'}
    $full=[PlaylistFlac.FlacAudit]::CheckFolder($fixture,[Threading.CancellationToken]::None,$Decoder)
    if($full.TotalFiles -ne 3 -or $full.ValidHeaders -ne 1 -or $full.InvalidHeaders -ne 2){throw 'Full decode did not distinguish valid, damaged, and truncated audio.'}
    if([Convert]::ToBase64String([IO.File]::ReadAllBytes($valid)) -ne [Convert]::ToBase64String($bytes)){throw 'Decoder modified the original.'}
    $cancel=[Threading.CancellationTokenSource]::new();$cancel.Cancel();$cancelled=$false
    try {[PlaylistFlac.FlacAudit]::CheckFolder($fixture,$cancel.Token,$Decoder) | Out-Null} catch {$cancelled=$_.Exception.ToString().Contains('OperationCanceledException')}
    finally {$cancel.Dispose()}
    if(-not $cancelled){throw 'Cancelled verification was not stopped.'}
    'PASS: official decoder integration; valid, damaged, truncated, cancellation, and original preservation.' | Set-Content -LiteralPath (Join-Path $root 'build/audio-decoder-results.txt')
    Get-Content -LiteralPath (Join-Path $root 'build/audio-decoder-results.txt')
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($resolved).StartsWith('FlacDecoderTest-')){throw 'Unsafe fixture cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
