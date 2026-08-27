# verify-installation.ps1 - Verifies the add-in is installed correctly
[CmdletBinding()]
param()

$addinTarget = "$env:APPDATA\Autodesk\Revit\Addins\2024"
$manifestPath = "$addinTarget\AutodeskNativeAgent.addin"
Write-Host "=== Verifying Installation ===" -ForegroundColor Cyan

# Validate AddInId / ClientId in the installed manifest (must be GUIDs, no placeholders).
$guidOk = $true
$clientIdOk = $true
if (Test-Path $manifestPath) {
  $xml = [xml](Get-Content $manifestPath -Raw)
  $addInId = $xml.RevitAddIns.AddIn.AddInId
  $clientId = $xml.RevitAddIns.AddIn.ClientId
  if (-not $addInId -or -not [Guid]::TryParse($addInId, [ref]$null)) {
    $guidOk = $false
  }
  if (-not $clientId -or -not [Guid]::TryParse($clientId, [ref]$null)) {
    $clientIdOk = $false
  }
}

$checks = @(
  @{ Name = "Addins folder exists"; Path = $addinTarget; Type = "Directory" },
  @{ Name = "Manifest file"; Path = $manifestPath; Type = "File" },
  @{ Name = "AddInId is a valid GUID"; Path = $manifestPath; Type = "Custom"; CustomOk = $guidOk },
  @{ Name = "ClientId is a valid GUID"; Path = $manifestPath; Type = "Custom"; CustomOk = $clientIdOk },
  @{ Name = "Revit add-in DLL"; Path = "$addinTarget\AutodeskNativeAgent.Revit2024.dll"; Type = "File" },
  @{ Name = "Core DLL"; Path = "$addinTarget\AutodeskNativeAgent.Core.dll"; Type = "File" }
)

$allOk = $true
foreach ($check in $checks) {
  $ok = $false
  if ($check.Type -eq "Directory") { $ok = Test-Path $check.Path }
  elseif ($check.Type -eq "File") { $ok = Test-Path $check.Path }
  elseif ($check.Type -eq "Custom") { $ok = $check.CustomOk }

  if ($ok) {
    Write-Host "  [OK]   $($check.Name)" -ForegroundColor Green
  } else {
    Write-Host "  [FAIL] $($check.Name)" -ForegroundColor Red
    $allOk = $false
  }
}

Write-Host ""
if ($allOk) {
  Write-Host "=== All checks passed ===" -ForegroundColor Green
} else {
  Write-Host "=== Some checks failed - run install-revit2024.ps1 ===" -ForegroundColor Yellow
  exit 1
}
