# Builds long test audio from a bundled model's test clip, to exercise file transcription
# (VAD segmentation, the max-length cap and the overlap path). Output goes to testmedia/ (gitignored).
#
# Usage:  pwsh -File tools/make-long-testmedia.ps1 [-Repeats 10]

param(
    [string]$ModelsRoot = (Join-Path $PSScriptRoot '..\models'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\testmedia'),
    [int]$Repeats = 10
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ffmpeg = Join-Path $PSScriptRoot '..\third_party\ffmpeg\bin\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { throw "ffmpeg not found: $ffmpeg (run tools/fetch-ffmpeg.ps1 first)" }

$clip = Join-Path $ModelsRoot 'sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23\test_wavs\0.wav'
if (-not (Test-Path $clip)) { throw "source clip not found: $clip" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$silence = Join-Path $OutDir '_silence-1s.wav'
& $ffmpeg -y -v error -f lavfi -i anullsrc=r=16000:cl=mono -t 1.0 -c:a pcm_s16le $silence

# Continuous: the clip repeated back-to-back, so speech runs past the per-segment cap.
$continuous = Join-Path $OutDir 'long-continuous.wav'
& $ffmpeg -y -v error -stream_loop ($Repeats - 1) -i $clip -c:a pcm_s16le -ar 16000 -ac 1 $continuous

# With gaps: clip / 1 s silence / clip ..., so the VAD finds many separate segments.
$list = Join-Path $OutDir '_concat.txt'
$lines = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $Repeats; $i++) {
    if ($i -gt 0) { $lines.Add("file '" + ($silence -replace '\\', '/') + "'") }
    $lines.Add("file '" + ($clip -replace '\\', '/') + "'")
}
$lines | Set-Content -Encoding ascii $list

$gaps = Join-Path $OutDir 'long-gaps.wav'
& $ffmpeg -y -v error -f concat -safe 0 -i $list -c:a pcm_s16le -ar 16000 -ac 1 $gaps

Remove-Item -Force $silence, $list

foreach ($f in @($continuous, $gaps)) {
    $info = & $ffmpeg -v error -i $f -f null - 2>&1
    Write-Output ("{0}: {1:N0} bytes" -f (Split-Path $f -Leaf), (Get-Item $f).Length)
}
