# BUILD_QUEUE — commands to run once source is complete

> Single ordered list. Nothing here is run while the terminal is broken
> (`opening NUL for ACL write`). Run top-to-bottom after the final source pass.

## 1. Restore

```powershell
dotnet restore .\AutodeskNativeAgent.sln
```

## 2. Build shared core — net48

```powershell
dotnet build .\libs\agent-core\AutodeskNativeAgent.Core.csproj -c Debug -f net48
```

## 3. Build shared core — net8.0

```powershell
dotnet build .\libs\agent-core\AutodeskNativeAgent.Core.csproj -c Debug -f net8.0
```

## 4. Build Revit add-in (requires Revit 2024 DLLs)

```powershell
dotnet build .\apps\revit-2024-addin\AutodeskNativeAgent.Revit2024.csproj -c Debug
# or with an explicit API dir:
dotnet build .\apps\revit-2024-addin\AutodeskNativeAgent.Revit2024.csproj -c Debug -p:RevitApiDir="C:\Program Files\Autodesk\Revit 2024"
```

## 5. TypeScript / MCP server build

```powershell
npm install
npm run build
```

## 6. Unit tests

```powershell
dotnet test .\tests\unit\AutodeskNativeAgent.Core.Tests\AutodeskNativeAgent.Core.Tests.csproj -c Debug
```

## 7. Package release

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## 8. Install verification (Revit 2024 running)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-revit2024.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\verify-installation.ps1
```

## 9. Uninstall (cleanup)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-revit2024.ps1
```

## Error handling order

1. Fix all `libs/agent-core` (net48+net8.0) errors first — the shared project feeds both.
2. Fix test project errors next.
3. Fix add-in errors last (Revit-API-dependent, may require `-p:RevitApiDir`).
4. Fix TypeScript errors after C# compiles.
5. Re-run until zero source-code errors; only then consider the build verified.
