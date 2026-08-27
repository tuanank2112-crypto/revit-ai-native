# build-all.ps1 — Builds the entire Autodesk Native Agent solution
# Usage: .\scripts\build-all.ps1
[CmdletBinding()]
param(
  [string]$Configuration = "Release",
  [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
Write-Host "=== Autodesk Native Agent — Build All ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration | Platform: $Platform"
Write-Host ""

# Step 1: Restore NuGet packages
Write-Host "[1/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore "$root\AutodeskNativeAgent.sln"
if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed."; exit 1 }

# Step 2: Build Core (net48 + net8.0)
Write-Host "[2/5] Building Core library..." -ForegroundColor Yellow
dotnet build "$root\libs\agent-core\AutodeskNativeAgent.Core.csproj" -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { Write-Error "Core build failed."; exit 1 }

# Step 3: Build Tests
Write-Host "[3/5] Building test project..." -ForegroundColor Yellow
dotnet build "$root\tests\unit\AutodeskNativeAgent.Core.Tests\AutodeskNativeAgent.Core.Tests.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Error "Test build failed."; exit 1 }

# Step 4: Build MCP TypeScript
Write-Host "[4/5] Building MCP TypeScript server..." -ForegroundColor Yellow
$mcpDir = "$root\apps\agent-mcp"
if (Test-Path "$mcpDir\package.json") {
  Push-Location $mcpDir
  npm install --silent
  npm run build
  if ($LASTEXITCODE -ne 0) { Write-Error "MCP build failed."; exit 1 }
  Pop-Location
} else {
  Write-Host "  (MCP directory not found, skipping)" -ForegroundColor DarkGray
}

# Step 5: Build Revit 2024 Add-in
Write-Host "[5/5] Building Revit 2024 add-in..." -ForegroundColor Yellow
dotnet build "$root\apps\revit-2024-addin\AutodeskNativeAgent.Revit2024.csproj" -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { Write-Error "Revit add-in build failed."; exit 1 }

Write-Host ""
Write-Host "=== Build completed successfully ===" -ForegroundColor Green
