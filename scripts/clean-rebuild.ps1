# Full clean + rebuild of the add-in with a fresh compiler
$ErrorActionPreference = "Continue"
$root = "e:\revit-A.I Native"

Write-Host "=== Killing build server processes ==="
Get-Process -Name VBCSCompiler, MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "=== Deleting obj + bin (addin + core) ==="
Remove-Item -Recurse -Force "$root\apps\revit-2024-addin\obj", "$root\apps\revit-2024-addin\bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$root\libs\agent-core\obj", "$root\libs\agent-core\bin" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "=== Clean rebuild (no shared compilation) ==="
Set-Location $root
dotnet build "$root\apps\revit-2024-addin\AutodeskNativeAgent.Revit2024.csproj" `
  -c Release -p:Platform=x64 -nodeReuse:false -p:UseSharedCompilation=false `
  2>&1 | Tee-Object -FilePath "C:\Users\lenku\.gemini\antigravity-ide\brain\2b11c4fc-2ab6-4f38-af2b-3deee1b8b73e\scratch\rebuild_clean.log" |
  Select-String -Pattern "error CS|warning CS|Build succeeded|Build FAILED|-> "

Write-Host "=== EXIT CODE: $LASTEXITCODE ==="
