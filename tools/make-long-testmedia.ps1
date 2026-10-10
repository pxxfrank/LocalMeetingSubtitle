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

# Two speakers: A, gap, A, gap, B, gap, B -> the first half is speaker A, the second half speaker B.
# Used to exercise Phase 3 (diarization -> role-tagged dialogue).
$speakerA = Join-Path $ModelsRoot '_diar-eval\fangjun-sr-1.wav'
$speakerB = Join-Path $ModelsRoot '_diar-eval\leijun-sr-1.wav'
$twoSpeakers = $null
if ((Test-Path $speakerA) -and (Test-Path $speakerB)) {
    $sil03 = Join-Path $OutDir '_sil03.wav'
    $sil05 = Join-Path $OutDir '_sil05.wav'
    & $ffmpeg -y -v error -f lavfi -i anullsrc=r=16000:cl=mono -t 0.3 -c:a pcm_s16le $sil03
    & $ffmpeg -y -v error -f lavfi -i anullsrc=r=16000:cl=mono -t 0.5 -c:a pcm_s16le $sil05

    $list2 = Join-Path $OutDir '_concat2.txt'
    $lines2 = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($speakerA, $sil03, $speakerA, $sil05, $speakerB, $sil03, $speakerB)) {
        $lines2.Add("file '" + ($entry -replace '\\', '/') + "'")
    }
    $lines2 | Set-Content -Encoding ascii $list2

    $twoSpeakers = Join-Path $OutDir 'two-speakers.wav'
    & $ffmpeg -y -v error -f concat -safe 0 -i $list2 -c:a pcm_s16le -ar 16000 -ac 1 $twoSpeakers
    Remove-Item -Force $sil03, $sil05, $list2
}

Remove-Item -Force $silence, $list

$produced = @($continuous, $gaps)
if ($twoSpeakers) { $produced += $twoSpeakers }
foreach ($f in $produced) {
    & $ffmpeg -v error -i $f -f null - 2>&1 | Out-Null
    Write-Output ("{0}: {1:N0} bytes" -f (Split-Path $f -Leaf), (Get-Item $f).Length)
}
