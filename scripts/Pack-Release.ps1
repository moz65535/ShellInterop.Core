<#
.SYNOPSIS
  Packs the three ShellInterop NuGet packages with release path normalization, then runs the PDB path gate.

.DESCRIPTION
  Passes -p:ShellInteropReleasePathMap=true (see Directory.Build.props) so the DLL CodeView PDB path is
  /_/... instead of a developer-machine path, then runs scripts/Test-PackagePdbPaths.ps1 on the output.
  Does not publish anything. Run it from a clean source checkout/copy; the repo root is derived from this
  script's location, so no absolute path is hard-coded.

.PARAMETER OutputDirectory
  Directory that receives the .nupkg files. Must not already contain .nupkg files.

.EXAMPLE
  pwsh scripts/Pack-Release.ps1 -OutputDirectory C:\work\nupkg-out
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$out = (Resolve-Path $OutputDirectory).Path
if (Get-ChildItem -LiteralPath $out -Filter *.nupkg -ErrorAction SilentlyContinue) {
    throw "OutputDirectory already contains .nupkg files: $out"
}

foreach ($name in 'ShellInterop.Core', 'ShellInterop.JumpList', 'ShellInterop.Notifications') {
    $proj = Join-Path $root "src\$name\$name.csproj"
    & dotnet pack $proj -c $Configuration -o $out -p:ShellInteropReleasePathMap=true --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed: $name" }
}

& (Join-Path $PSScriptRoot 'Test-PackagePdbPaths.ps1') -Path $out
exit $LASTEXITCODE
