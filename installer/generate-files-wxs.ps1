<#
.SYNOPSIS
  Generates a WiX v4 fragment (installer/GeneratedFiles.wxs) that contains one Component per
  file in the publish output directory, with stable path-derived GUIDs.

.DESCRIPTION
  WiX v4.0.6 has no `Files` harvesting element and Heat is removed in v4, so we synthesise the
  component group ourselves. Re-run this whenever the publish output changes.
#>
param(
    [string]$AppFiles = "dist/LocalMeetingSubtitle-win-x64",
    [string]$OutFile  = "installer/GeneratedFiles.wxs"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $AppFiles).Path

function New-DeterministicGuid([string]$text) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try   { $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) }
    finally { $md5.Dispose() }
    $bytes = New-Object byte[] 16
    [Array]::Copy($hash, $bytes, 16)
    return (New-Object System.Guid (,$bytes)).ToString()
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles">')

$files = Get-ChildItem -Recurse -File $root | Sort-Object FullName
$count = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
    $guid = New-DeterministicGuid "LMS:$rel"
    $src = $f.FullName.Replace('&', '&amp;').Replace('"', '&quot;')
    [void]$sb.AppendLine("      <Component Id=`"cmp$count`" Directory=`"INSTALLFOLDER`" Guid=`"$guid`">")
    [void]$sb.AppendLine("        <File Id=`"fil$count`" Source=`"$src`" />")
    [void]$sb.AppendLine("      </Component>")
    $count++
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$outPath = Join-Path (Get-Location) $OutFile
$sb.ToString() | Set-Content -Path $outPath -Encoding UTF8
Write-Output "Generated $outPath with $count file components."
