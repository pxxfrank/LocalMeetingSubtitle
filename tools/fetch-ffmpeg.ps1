<#
.SYNOPSIS
  Fetches the LGPL FFmpeg build used by the file-transcription feature.

.DESCRIPTION
  V0.5 decodes local audio/video through a separately distributed FFmpeg build.
  It must be an **LGPL** build (no --enable-gpl / --enable-nonfree) so it can be
  redistributed as a separate program under the LGPL, together with its license.

  This script downloads that build into third_party/ffmpeg (gitignored) and keeps
  only the runtime files needed to decode + probe media, plus the license.

  The exact build is pinned by URL **and** SHA-256 so a release can be reproduced
  and audited. See docs/LICENSES.md.

.EXAMPLE
  ./tools/fetch-ffmpeg.ps1
  ./tools/fetch-ffmpeg.ps1 -Force     # re-download even if present
#>
param(
    [string]$Root = "third_party/ffmpeg",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# --- Pinned artifact -------------------------------------------------------
$tag = "latest"
$file = "ffmpeg-master-latest-win64-lgpl-shared.zip"
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$tag/$file"
$sha256 = "85e26d3d77c17393e56e49132fda0905a3ced925942e1d8d7d2ebe0a28d58a55"
$inner = "ffmpeg-master-latest-win64-lgpl-shared"

# Files kept at runtime: the CLI programs + the shared libraries they link, and
# the license. (lib/, include/, doc/, presets/ and ffplay.exe are not needed.)
$keep = @(
    "bin/ffmpeg.exe", "bin/ffprobe.exe",
    "bin/avutil-61.dll", "bin/avcodec-63.dll", "bin/avformat-63.dll",
    "bin/avdevice-63.dll", "bin/avfilter-12.dll",
    "bin/swresample-7.dll", "bin/swscale-10.dll",
    "LICENSE.txt"
)

New-Item -ItemType Directory -Force -Path $Root, "third_party/_download" | Out-Null

$marker = Join-Path $Root "bin\ffmpeg.exe"
if ((Test-Path $marker) -and -not $Force) {
    Write-Host "FFmpeg already present at $Root (use -Force to re-download)."
} else {
    $zip = Join-Path "third_party/_download" $file
    if (-not (Test-Path $zip) -or $Force) {
        Write-Host "Downloading $url ..."
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $url -OutFile $zip -TimeoutSec 900
    }

    Write-Host "Verifying SHA-256 ..."
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "SHA-256 mismatch. expected=$sha256 actual=$actual"
    }

    Write-Host "Extracting runtime files into $Root ..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $zip))
    try {
        foreach ($entry in $archive.Entries) {
            $rel = $entry.FullName.Substring($inner.Length + 1)
            if ($keep -notcontains $rel) { continue }
            $dest = Join-Path $Root ($rel -replace '/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
        }
    } finally {
        $archive.Dispose()
    }
}

# --- Verify the build is LGPL (never GPL / nonfree) ------------------------
$ffmpeg = Join-Path $Root "bin\ffmpeg.exe"
$version = (& $ffmpeg -hide_banner -version 2>&1 | Select-Object -First 1) -join ""
Write-Host "FFmpeg: $version"
$config = (& $ffmpeg -hide_banner -version 2>&1) -join "`n"
if ($config -match '--enable-gpl' -or $config -match '--enable-nonfree') {
    throw "This FFmpeg build is GPL/nonfree and must not be redistributed. Pick an LGPL build."
}

$license = Join-Path $Root "LICENSE.txt"
$licenseHead = if (Test-Path $license) { ((Get-Content $license -TotalCount 2) -join ' ').Trim() } else { "(missing!)" }
$total = (Get-ChildItem $Root -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("License : {0}" -f $licenseHead)
Write-Host ("Size    : {0:N1} MB" -f $total)
Write-Host "OK."
