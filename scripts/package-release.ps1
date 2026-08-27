# package-release.ps1 — Packages a release zip for distribution
[CmdletBinding()]
param(
  [string]$Version = "1.0.0",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$staging = "$root\artifacts\release-staging"
$zipPath = "$root\artifacts\AutodeskNativeAgent-$Version.zip"

Write-Host "=== Packaging Release v$Version ===" -ForegroundColor Cyan

# Clean staging
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

# Copy Revit add-in. The solution build places output under bin\x64\Release\net48
# (add-in sets PlatformTarget=x64); a project-only build lands in bin\Release\net48.
# Prefer whichever build output is NEWER so we never deploy a stale DLL.
$addinBin = "$root\apps\revit-2024-addin\bin\$Configuration\net48"
$addinBinX64 = "$root\apps\revit-2024-addin\bin\x64\$Configuration\net48"
if ((Test-Path "$addinBinX64\AutodeskNativeAgent.Revit2024.dll") -and
    (Test-Path "$addinBin\AutodeskNativeAgent.Revit2024.dll")) {
    $m1 = (Get-Item "$addinBin\AutodeskNativeAgent.Revit2024.dll").LastWriteTime
    $m2 = (Get-Item "$addinBinX64\AutodeskNativeAgent.Revit2024.dll").LastWriteTime
    if ($m2 -gt $m1) { $addinBin = $addinBinX64 }
}
elseif (Test-Path "$addinBinX64\AutodeskNativeAgent.Revit2024.dll") {
    $addinBin = $addinBinX64
}
$addinDest = "$staging\revit-2024-addin"
New-Item -ItemType Directory -Path $addinDest -Force | Out-Null
Copy-Item "$addinBin\AutodeskNativeAgent.Revit2024.dll" $addinDest -ErrorAction SilentlyContinue
Copy-Item "$addinBin\AutodeskNativeAgent.Core.dll" $addinDest -ErrorAction SilentlyContinue
Copy-Item "$root\apps\revit-2024-addin\Addin\AutodeskNativeAgent.addin" $addinDest

# Copy MCP server
$mcpSrc = "$root\apps\agent-mcp"
$mcpDest = "$staging\agent-mcp"
New-Item -ItemType Directory -Path $mcpDest -Force | Out-Null
if (Test-Path "$mcpSrc\dist") {
  Copy-Item "$mcpSrc\dist" "$mcpDest\dist" -Recurse
}
Copy-Item "$mcpSrc\package.json" $mcpDest
Copy-Item "$mcpSrc\tsconfig.json" $mcpDest

# Copy scripts
$scriptsDest = "$staging\scripts"
New-Item -ItemType Directory -Path $scriptsDest -Force | Out-Null
Copy-Item "$root\scripts\*.ps1" $scriptsDest

# Copy sample plans
if (Test-Path "$root\samples\plans") {
  $samplesDest = "$staging\samples\plans"
  New-Item -ItemType Directory -Path $samplesDest -Force | Out-Null
  Copy-Item "$root\samples\plans\*.json" $samplesDest
}

# Copy docs
Copy-Item "$root\README.md" $staging -ErrorAction SilentlyContinue

# Zip
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$staging\*" -DestinationPath $zipPath

Write-Host "=== Release packaged: $zipPath ===" -ForegroundColor Green
