# clean.ps1 — Cleans all build artifacts
[CmdletBinding()]
param()

$root = Resolve-Path "$PSScriptRoot\.."
Write-Host "=== Cleaning build artifacts ===" -ForegroundColor Cyan

$pathsToClean = @(
  "$root\bin",
  "$root\obj",
  "$root\libs\agent-core\bin",
  "$root\libs\agent-core\obj",
  "$root\apps\revit-2024-addin\bin",
  "$root\apps\revit-2024-addin\obj",
  "$root\tests\unit\AutodeskNativeAgent.Core.Tests\bin",
  "$root\tests\unit\AutodeskNativeAgent.Core.Tests\obj",
  "$root\apps\agent-mcp\dist",
  "$root\apps\agent-mcp\node_modules"
)

foreach ($path in $pathsToClean) {
  if (Test-Path $path) {
    Write-Host "  Removing $path"
    Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Write-Host "=== Clean completed ===" -ForegroundColor Green
