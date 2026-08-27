# install-revit2024.ps1 — Installs the Revit 2024 add-in to the user's Addins folder
[CmdletBinding()]
param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$addinSource = "$root\apps\revit-2024-addin\bin\$Configuration\net48"
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
