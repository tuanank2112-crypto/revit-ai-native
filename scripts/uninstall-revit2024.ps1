# uninstall-revit2024.ps1 — Removes the Revit 2024 add-in
[CmdletBinding()]
param()

$addinTarget = "$env:APPDATA\Autodesk\Revit\Addins\2024"
Write-Host "=== Uninstalling Revit 2024 Add-in ===" -ForegroundColor Cyan

$files = @(
  "$addinTarget\AutodeskNativeAgent.addin",
  "$addinTarget\AutodeskNativeAgent.Revit2024.dll",
  "$addinTarget\AutodeskNativeAgent.Core.dll"
)

foreach ($file in $files) {
  if (Test-Path $file) {
    Write-Host "  Removing $file"
    Remove-Item $file -Force
  }
}

Write-Host "=== Uninstall complete ===" -ForegroundColor Green
