<#
.SYNOPSIS
  Generates the WiX v4 source that installs the published app folder, preserving its
  full directory structure.

.DESCRIPTION
  Emits two fragments into <OutFile>:
    * a nested Directory tree (DirectoryRef INSTALLFOLDER + one <Directory> per path level), and
    * a ComponentGroup (Id="AppFiles") with one Component per file, each pointing at the
      directory that mirrors the file's folder in the publish output.

  Every file gets a stable, path-derived GUID plus explicit unique Component/File ids
  (WiX's automatic ids collide for same-named files in different language folders).

  History: the first version put every Component directly in INSTALLFOLDER, which flattened
  the tree (localized resource folders overwrote each other, and models\ / docs\ were lost).
  The second version emitted one Directory per *leaf* path only, so multi-level folders such
  as models\<name>\ lost their parent. This version builds the complete, nested tree.
#>
param(
    [string]$AppFiles = "dist/LocalMeetingSubtitle-win-x64",
    [string]$OutFile  = "installer/GeneratedFiles.wxs"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $AppFiles).Path.TrimEnd('\')

function New-DeterministicGuid([string]$text) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try   { $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)) }
    finally { $md5.Dispose() }
    $bytes = New-Object byte[] 16
    [Array]::Copy($hash, $bytes, 16)
    return (New-Object System.Guid (,$bytes)).ToString()
}

function Esc([string]$s) {
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

# ---- Collect files ------------------------------------------------------------------
$entries = @()
foreach ($f in (Get-ChildItem -Recurse -File $root | Sort-Object FullName)) {
    $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
    $relDir = [System.IO.Path]::GetDirectoryName($rel)
    if ($null -eq $relDir) { $relDir = '' }
    $entries += [pscustomobject]@{ RelDir = $relDir.Replace('\', '/'); Source = $f.FullName; Rel = $rel }
}

# ---- Complete set of directories (every ancestor prefix) ----------------------------
$allDirs = New-Object System.Collections.Generic.HashSet[string]
[void]$allDirs.Add('')
foreach ($e in $entries) {
    if ($e.RelDir -eq '') { continue }
    $parts = $e.RelDir -split '/'
    for ($k = 1; $k -le $parts.Count; $k++) {
        [void]$allDirs.Add((($parts[0..($k - 1)]) -join '/'))
    }
}

# parent -> children
$children = @{}
foreach ($d in $allDirs) {
    if ($d -eq '') { continue }
    $parent = if ($d.Contains('/')) { $d.Substring(0, $d.LastIndexOf('/')) } else { '' }
    if (-not $children.ContainsKey($parent)) { $children[$parent] = New-Object System.Collections.Generic.List[string] }
    $children[$parent].Add($d)
}

# ---- Ids -----------------------------------------------------------------------------
$dirId = @{ '' = 'INSTALLFOLDER' }
$n = 0
foreach ($d in ($allDirs | Where-Object { $_ -ne '' } | Sort-Object)) { $dirId[$d] = "dir$($n)"; $n++ }

# ---- Emit ---------------------------------------------------------------------------
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')

[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

function Emit-Node([string]$path, [int]$indent) {
    if (-not $children.ContainsKey($path)) { return }
    $pad = '  ' * $indent
    foreach ($child in ($children[$path] | Sort-Object)) {
        $name = Esc (($child -split '/')[-1])
        $hasKids = $children.ContainsKey($child) -and $children[$child].Count -gt 0
        if ($hasKids) {
            [void]$sb.AppendLine("$pad<Directory Id=`"$($dirId[$child])`" Name=`"$name`">")
            Emit-Node $child ($indent + 1)
            [void]$sb.AppendLine("$pad</Directory>")
        } else {
            [void]$sb.AppendLine("$pad<Directory Id=`"$($dirId[$child])`" Name=`"$name`" />")
        }
    }
}
Emit-Node '' 3

[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('  </Fragment>')

[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles">')
$i = 0
foreach ($e in $entries) {
    $guid = New-DeterministicGuid "LMS:$($e.Rel)"
    [void]$sb.AppendLine("      <Component Id=`"cmp$i`" Directory=`"$($dirId[$e.RelDir])`" Guid=`"$guid`">")
    [void]$sb.AppendLine("        <File Id=`"fil$i`" Source=`"$(Esc $e.Source)`" />")
    [void]$sb.AppendLine('      </Component>')
    $i++
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$outPath = Join-Path (Get-Location) $OutFile
$sb.ToString() | Set-Content -Path $outPath -Encoding UTF8
$dirCount = ($allDirs | Where-Object { $_ -ne '' }).Count
Write-Output "Generated $outPath : $($entries.Count) files across $dirCount directories."
