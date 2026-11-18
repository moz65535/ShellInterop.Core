<#
.SYNOPSIS
  Release gate: fails if a built .nupkg/.snupkg leaks a developer-machine absolute path.

.DESCRIPTION
  For every .dll inside each package, reads the PE Debug Directory and checks the
  CodeView (RSDS) PDB path directly (not a string search). A path is acceptable when it is
  a bare file name, a relative path, or a normalized virtual path under /_/ (PathMap output).
  Drive-letter, UNC and any other rooted path fails.

  Also does a minimal text scan of .nuspec/.xml/.psmdcp/.json/.props/.targets entries and of
  any .pdb entries (snupkg) for drive-letter paths. Markdown entries only produce warnings
  because documentation may legitimately show example paths.

.PARAMETER Path
  One or more .nupkg/.snupkg files, or directories that are searched (non-recursively) for them.

.PARAMETER ExpectedDll
  DLL file names that must be present and checked across the given packages.
  Defaults to the three ShellInterop assemblies.

.EXAMPLE
  pwsh scripts/Test-PackagePdbPaths.ps1 -Path .\artifacts\nupkg

.NOTES
  Exit code 0 = pass, 1 = leak or missing expected DLL, 2 = usage error.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string[]]$Path,
    [string[]]$ExpectedDll = @('ShellInterop.Core.dll', 'ShellInterop.JumpList.dll', 'ShellInterop.Notifications.dll')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$drivePath = '(?<![A-Za-z0-9])[A-Za-z]:\\[^\s"<>|*?]+'
$packages = foreach ($p in $Path) {
    if (Test-Path -LiteralPath $p -PathType Container) {
        Get-ChildItem -LiteralPath $p -File | Where-Object { $_.Extension -in '.nupkg', '.snupkg' }
    }
    elseif (Test-Path -LiteralPath $p -PathType Leaf) { Get-Item -LiteralPath $p }
    else { Write-Error "Not found: $p"; exit 2 }
}
if (-not $packages) { Write-Error 'No .nupkg/.snupkg found.'; exit 2 }

function Test-SafePdbPath([string]$pdbPath) {
    if ([string]::IsNullOrEmpty($pdbPath)) { return $false }
    if ($pdbPath.StartsWith('/_/')) { return $true }              # PathMap virtual root
    if ($pdbPath -match '^[A-Za-z]:' -or $pdbPath.StartsWith('\\') -or $pdbPath.StartsWith('/')) { return $false }
    return $true                                                  # bare name or relative
}

$failures = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$checked = New-Object System.Collections.Generic.List[string]
$textExt = '.nuspec', '.xml', '.psmdcp', '.json', '.props', '.targets'

foreach ($pkg in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        foreach ($e in $zip.Entries) {
            if ($e.Length -eq 0) { continue }
            $ext = [System.IO.Path]::GetExtension($e.Name).ToLowerInvariant()
            $ms = New-Object System.IO.MemoryStream
            $s = $e.Open(); $s.CopyTo($ms); $s.Dispose()
            $bytes = $ms.ToArray()
            $label = "$($pkg.Name)!$($e.FullName)"

            if ($ext -eq '.dll') {
                $reader = New-Object System.Reflection.PortableExecutable.PEReader((New-Object System.IO.MemoryStream (, $bytes)))
                try {
                    $cv = @($reader.ReadDebugDirectory() | Where-Object { $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView })
                    if ($cv.Count -eq 0) {
                        $failures.Add("$label : no CodeView entry (symbol reference missing)")
                    }
                    foreach ($entry in $cv) {
                        $data = $reader.ReadCodeViewDebugDirectoryData($entry)
                        $ok = Test-SafePdbPath $data.Path
                        $checked.Add(("{0,-4} {1} CodeView='{2}'" -f $(if ($ok) { 'OK' } else { 'FAIL' }), $label, $data.Path))
                        if (-not $ok) { $failures.Add("$label : CodeView PDB path leaks local path: $($data.Path)") }
                    }
                }
                finally { $reader.Dispose() }
                # Minimal supplementary scan: rooted drive paths embedded as ASCII/UTF-16 in the DLL.
                foreach ($enc in [System.Text.Encoding]::ASCII, [System.Text.Encoding]::Unicode) {
                    $m = [regex]::Match($enc.GetString($bytes), '[A-Za-z]:\\(Users|Documents and Settings)\\[^\s"<>|*?\x00]*')
                    if ($m.Success) { $failures.Add("$label : embedded local path string: $($m.Value)") }
                }
            }
            elseif ($ext -eq '.pdb') {
                foreach ($enc in [System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode) {
                    $m = [regex]::Match($enc.GetString($bytes), $drivePath)
                    if ($m.Success) { $failures.Add("$label : local path in PDB: $($m.Value)") }
                }
            }
            elseif ($textExt -contains $ext) {
                $m = [regex]::Match([System.Text.Encoding]::UTF8.GetString($bytes), $drivePath)
                if ($m.Success) { $failures.Add("$label : local path in text: $($m.Value)") }
            }
            elseif ($ext -eq '.md') {
                $m = [regex]::Match([System.Text.Encoding]::UTF8.GetString($bytes), '[A-Za-z]:\\Users\\[^\s"<>|*?]*')
                if ($m.Success) { $warnings.Add("$label : user-profile path in markdown: $($m.Value)") }
            }
        }
    }
    finally { $zip.Dispose() }
}

foreach ($exp in $ExpectedDll) {
    if (-not ($checked | Where-Object { $_ -like "*!*$exp CodeView=*" })) {
        $failures.Add("Expected DLL not inspected: $exp")
    }
}

$checked | ForEach-Object { Write-Host $_ }
$warnings | ForEach-Object { Write-Warning $_ }
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    Write-Host "RESULT: FAIL ($($failures.Count) problem(s))" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: PASS ($($checked.Count) CodeView entr$(if ($checked.Count -eq 1) {'y'} else {'ies'}) checked)" -ForegroundColor Green
exit 0
