# install-revit2024.ps1 — Installs the Revit 2024 add-in to the user's Addins folder
[CmdletBinding()]
param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$addinSource = "$root\apps\revit-2024-addin\bin\$Configuration\net48"
$addinSourceX64 = "$root\apps\revit-2024-addin\bin\x64\$Configuration\net48"
# The add-in sets PlatformTarget=x64; building the SOLUTION lands output in
# bin\x64\Release\net48, while a project-only build lands in bin\Release\net48.
# Prefer whichever is NEWER so a stale DLL is never deployed.
if ((Test-Path "$addinSourceX64\AutodeskNativeAgent.Revit2024.dll") -and
    (Test-Path "$addinSource\AutodeskNativeAgent.Revit2024.dll")) {
    $mStd = (Get-Item "$addinSource\AutodeskNativeAgent.Revit2024.dll").LastWriteTime
    $mX64 = (Get-Item "$addinSourceX64\AutodeskNativeAgent.Revit2024.dll").LastWriteTime
    if ($mX64 -gt $mStd) { $addinSource = $addinSourceX64 }
}
elseif (Test-Path "$addinSourceX64\AutodeskNativeAgent.Revit2024.dll") {
    $addinSource = $addinSourceX64
}
$addinTarget = "$env:APPDATA\Autodesk\Revit\Addins\2024"

Write-Host "=== Installing Revit 2024 Add-in ===" -ForegroundColor Cyan

# Ensure target directory exists
if (-not (Test-Path $addinTarget)) {
  New-Item -ItemType Directory -Path $addinTarget -Force | Out-Null
}

# Copy the manifest
$manifestSource = "$root\apps\revit-2024-addin\Addin\AutodeskNativeAgent.addin"
$manifestTarget = "$addinTarget\AutodeskNativeAgent.addin"
Write-Host "  Manifest: $manifestTarget"
Copy-Item $manifestSource $manifestTarget -Force

# Copy the assembly (NOT RevitAPI.dll / RevitAPIUI.dll)
$dllSource = "$addinSource\AutodeskNativeAgent.Revit2024.dll"
$dllTarget = "$addinTarget\AutodeskNativeAgent.Revit2024.dll"
if (Test-Path $dllSource) {
  Write-Host "  Assembly: $dllTarget"
  Copy-Item $dllSource $dllTarget -Force
} else {
  Write-Warning "Assembly not found at $dllSource — did you build first?"
}

# Copy the core library
$coreDll = "$addinSource\AutodeskNativeAgent.Core.dll"
$coreTarget = "$addinTarget\AutodeskNativeAgent.Core.dll"
if (Test-Path $coreDll) {
  Write-Host "  Core:     $coreTarget"
  Copy-Item $coreDll $coreTarget -Force
}

Write-Host ""
Write-Host "=== Installation complete ===" -ForegroundColor Green
Write-Host "Restart Revit 2024 to load the add-in."
