<#
.SYNOPSIS
  Packs the selected ShellInterop NuGet packages with release path normalization, then runs the PDB path gate.

.DESCRIPTION
  Passes -p:ShellInteropReleasePathMap=true (see Directory.Build.props) so the DLL CodeView PDB path is
  /_/... instead of a developer-machine path. Afterwards it verifies that the set of produced .nupkg files
  (PackageId + version) exactly matches the selected packages and the <Version> in each csproj, then runs
  scripts/Test-PackagePdbPaths.ps1 for every selected package. The PDB gate cannot be skipped; if it fails,
  this script exits non-zero. Does not publish anything. The repo root is derived from this script's location.

  Packing JumpList also builds its ProjectReference (Core), but a Core .nupkg is produced only when Core is selected.

.PARAMETER OutputDirectory
  Directory that receives the .nupkg files. It is created when missing and MUST be empty; it is never cleaned.

.PARAMETER Package
  Packages to pack: Core, JumpList, Notifications. Defaults to all three. Order and duplicates do not matter;
  each selected package is packed once.

.EXAMPLE
  pwsh scripts/Pack-Release.ps1 -OutputDirectory C:\work\nupkg-out
.EXAMPLE
  pwsh scripts/Pack-Release.ps1 -OutputDirectory C:\work\out -Package Core,JumpList
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Configuration = 'Release',
    [string[]]$Package = @('Core', 'JumpList', 'Notifications')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$all = [ordered]@{
    Core          = 'ShellInterop.Core'
    JumpList      = 'ShellInterop.JumpList'
    Notifications = 'ShellInterop.Notifications'
}
# Accept both `-Package Core,JumpList` (array) and `pwsh -File ... -Package Core,JumpList` (one comma-joined string).
# Validate explicitly (case-insensitive), then use canonical order without duplicates.
$requested = @($Package | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($requested.Count -eq 0) { throw 'No package selected. Use -Package Core, JumpList and/or Notifications.' }
$unknown = @($requested | Where-Object { $n = $_; -not ($all.Keys | Where-Object { $_ -ieq $n }) })
if ($unknown) { throw "Unknown package name(s): $($unknown -join ', '). Valid: $($all.Keys -join ', ')." }
$selected = @($all.Keys | Where-Object { $k = $_; $requested | Where-Object { $_ -ieq $k } })

if (Test-Path -LiteralPath $OutputDirectory) {
    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) { throw "OutputDirectory is not a directory: $OutputDirectory" }
    if (Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1) {
        throw "OutputDirectory must be empty (it is never cleaned): $OutputDirectory"
    }
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$out = (Resolve-Path -LiteralPath $OutputDirectory).Path

# Expected PackageId -> Version from each selected csproj.
$expected = @{}
foreach ($key in $selected) {
    $name = $all[$key]
    $proj = Join-Path $root "src\$name\$name.csproj"
    $ver = ([xml](Get-Content -LiteralPath $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $ver) { throw "No <Version> found in $proj" }
    $expected[$name] = $ver
}

foreach ($key in $selected) {
    $name = $all[$key]
    $proj = Join-Path $root "src\$name\$name.csproj"
    & dotnet pack $proj -c $Configuration -o $out -p:ShellInteropReleasePathMap=true --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed: $name" }
}

# Produced set must equal the selected set (no missing, no extra, no duplicates, matching versions).
Add-Type -AssemblyName System.IO.Compression.FileSystem
$produced = @()
foreach ($f in Get-ChildItem -LiteralPath $out -File -Filter *.nupkg) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($f.FullName)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' -and $_.FullName -notlike '*/*' } | Select-Object -First 1
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    $produced += [pscustomobject]@{ Id = $nuspec.package.metadata.id; Version = $nuspec.package.metadata.version; File = $f.Name }
}
$problems = @()
foreach ($id in $expected.Keys) {
    $m = @($produced | Where-Object { $_.Id -eq $id })
    if ($m.Count -eq 0) { $problems += "Missing package: $id" }
    elseif ($m.Count -gt 1) { $problems += "Multiple packages for ${id}: $($m.File -join ', ')" }
    elseif ($m[0].Version -ne $expected[$id]) { $problems += "Version mismatch for ${id}: csproj $($expected[$id]) but package $($m[0].Version)" }
}
foreach ($p in $produced) {
    if (-not $expected.ContainsKey($p.Id)) { $problems += "Unexpected package produced: $($p.File)" }
}
if (Get-ChildItem -LiteralPath $out -Force | Where-Object { $_.Extension -ne '.nupkg' -and $_.Extension -ne '.snupkg' }) {
    $problems += 'Unexpected non-package files in the output directory.'
}
if ($problems) { throw ("Package set check failed:`n  " + ($problems -join "`n  ")) }

# PDB gate: mandatory for every selected package (no skip option).
$dlls = $selected | ForEach-Object { "$($all[$_]).dll" }
& (Join-Path $PSScriptRoot 'Test-PackagePdbPaths.ps1') -Path $out -ExpectedDll $dlls
exit $LASTEXITCODE
