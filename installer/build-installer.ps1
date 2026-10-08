<#
.SYNOPSIS
  Builds the LocalMeetingSubtitle installer (MSI + Setup.exe) from the published app folder.

.DESCRIPTION
  Requires the WiX v4 CLI as a global .NET tool:
      dotnet tool install --global wix --version 4.0.6
      wix extension add --global WixToolset.Bal.wixext/4.0.6      # only needed for Setup.exe
  and .NET 8 installed anywhere on the machine (DOTNET_ROOT is set to %USERPROFILE%\.dotnet if
  that directory exists).

  Produces:
      dist/LocalMeetingSubtitle-Setup.msi     (per-user MSI, no admin required)
      dist/LocalMeetingSubtitle-Setup.exe     (Burn bootstrapper that chains the MSI)

  Optionally signs both (and the app exe) with a certificate from Cert:\CurrentUser\My.
  NOTE: a self-signed certificate produces an "untrusted" signature; Windows SmartScreen will
  still warn. Only a CA-issued (EV/OV) code-signing certificate yields a trusted signature.

.EXAMPLE
  ./installer/build-installer.ps1
  ./installer/build-installer.ps1 -CertThumbprint 94ED22A334454473C0B064614E9FD5B30A8C79C4
#>
param(
    [string]$AppFiles = "dist/LocalMeetingSubtitle-win-x64",
    [string]$Version  = "0.1.0",
    [string]$CertThumbprint = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

if (-not (Test-Path "$env:USERPROFILE\.dotnet\tools\wix.exe")) {
    throw "WiX CLI not found. Install it with: dotnet tool install --global wix --version 4.0.6"
}

# The WiX global tool needs DOTNET_ROOT when .NET lives outside the default location.
if (-not $env:DOTNET_ROOT -and (Test-Path "$env:USERPROFILE\.dotnet")) {
    $env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}

if (-not (Test-Path $AppFiles)) {
    throw "Published app folder '$AppFiles' not found. Run `dotnet publish` first."
}

Write-Host "[1/4] Regenerating installer/GeneratedFiles.wxs ..."
& "$PSScriptRoot/generate-files-wxs.ps1" -AppFiles $AppFiles -OutFile "installer/GeneratedFiles.wxs"

Write-Host "[2/4] Building MSI ..."
& wix build installer/Product.wxs installer/GeneratedFiles.wxs -arch x64 -o "dist/LocalMeetingSubtitle-Setup.msi"

Write-Host "[3/4] Building Setup.exe (Burn bundle) ..."
& wix build installer/Bundle.wxs -arch x64 -ext WixToolset.Bal.wixext -o "dist/LocalMeetingSubtitle-Setup.exe"

if ($CertThumbprint) {
    Write-Host "[4/4] Signing with certificate $CertThumbprint ..."
    $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint"
    foreach ($f in @("$AppFiles/LocalMeetingSubtitle.exe",
                     "dist/LocalMeetingSubtitle-Setup.msi",
                     "dist/LocalMeetingSubtitle-Setup.exe")) {
        if (Test-Path $f) {
            Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 | Out-Null
        }
    }
} else {
    Write-Host "[4/4] Skipping signing (no -CertThumbprint supplied)."
}

Write-Host "Done."
Get-Item "dist/LocalMeetingSubtitle-Setup.msi", "dist/LocalMeetingSubtitle-Setup.exe" |
    Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
