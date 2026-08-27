# start-mcp.ps1 — Starts the MCP TypeScript server
[CmdletBinding()]
param()

$mcpDir = Resolve-Path "$PSScriptRoot\..\apps\agent-mcp"
Write-Host "=== Starting MCP Server ===" -ForegroundColor Cyan

# Ensure dependencies are installed
if (-not (Test-Path "$mcpDir\node_modules")) {
  Write-Host "  Installing dependencies..." -ForegroundColor Yellow
  Push-Location $mcpDir
  npm install
  Pop-Location
}

# Ensure the TypeScript is compiled
if (-not (Test-Path "$mcpDir\dist\index.js")) {
  Write-Host "  Compiling TypeScript..." -ForegroundColor Yellow
  Push-Location $mcpDir
  npm run build
  Pop-Location
}

Write-Host "  Starting MCP server on stdio..." -ForegroundColor Green
Write-Host "  (Connect Antigravity/Claude to this process)"
Write-Host ""
node "$mcpDir\dist\index.js"
