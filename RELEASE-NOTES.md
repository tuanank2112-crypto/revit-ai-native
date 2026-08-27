# RELEASE NOTES — v1.0.0 (2026-08-27)

## Summary

First stable release of the **Autodesk Native Agent Runtime** for Revit 2024 — an MCP-based bridge letting AI agents (Antigravity, Claude, …) safely create/modify Revit models through a validated, auditable, confirmable plan-execution pipeline.

## Verified in this release

- **Build**: 0 errors, 0 warnings (Release, `--no-incremental`)
- **Unit + integration tests**: 59/59 pass (net8.0)
- **E2E in live Revit 2024**: **12/12 pass** over the named pipe (status, document info, plan validate/preview/commit with confirmation, assertions, element move via `$result.*` references, rollback on assertion failure, fingerprint guard, PDF export, audit log)

## Contents

| Component | Path | Notes |
|-----------|------|-------|
| Core library | `libs/agent-core` | net48 + net8.0; contracts, JSON DOM, validation, security, policy, job state machine, pipe protocol. Also shipped as NuGet package `AutodeskNativeAgent.Core 1.0.0`. |
| Revit 2024 add-in | `apps/revit-2024-addin` | IExternalApplication; 18 write ops + 14 read ops; main-thread dispatcher; pipe server (4 concurrent clients); file-based audit log. |
| MCP server | `apps/agent-mcp` | TypeScript ESM; 12 tools over stdio; pipe client with heartbeat + auto-reconnect. |
| Scripts | `scripts/` | build / install / uninstall / verify / package-release / start-mcp / clean (all UTF-8 BOM for PS 5.1). |
| Samples | `samples/plans/` | 6 example plans (wall+door, move, parameter set, delete, …). |

## Installation (Revit 2024)

```powershell
.\scripts\install-revit2024.ps1 -Configuration Release   # Revit must be CLOSED
# Open Revit 2024 -> File > New (add-in auto-loads via LoadingPolicy AlwaysLoad)
.\scripts\start-mcp.ps1                                   # or point any MCP client at node dist/index.js
```

## Key runtime behaviour

- **User-scoped pipe**: `\\.\pipe\autodesk-native-agent-<username>` (4-byte LE length + UTF-8 JSON frames, 1 MB cap).
- **Safety pipeline**: schema validation → security path check → policy/affected-element limit → preview → plan hash → document fingerprint → user confirmation (for destructive ops) → TransactionGroup → per-op assertions → automatic rollback on failure.
- **Audit trail**: in-memory ring (5 000 entries, queryable via `revit_get_audit_log`) **+ append-only JSON Lines at `%LOCALAPPDATA%\AutodeskNativeAgent\logs\audit.jsonl`** (survives Revit shutdown).
- **Async jobs**: commits run on the Revit main thread through an `ExternalEvent`-driven dispatcher; poll `revit_get_job_status`.

## Known limitations

- Single Revit version targeted (2024, net48); multi-version support is TIER 5.
- Preview is resolution-only for `element.move` / `element.rotate` (no simulated result).
- `element.delete` always requires user confirmation.
- `export.pdf` names output per sheet (no baseName override).
- Element references support `uniqueId` / `elementId` / `viaOperationResult` (`$result.<id>`); no live "selection" strategy.

## Post-release TODO

- Push repo to GitHub → enable branch protection + CI (workflows already in `.github/workflows/`).
- Publish `AutodeskNativeAgent.Core.nupkg` to GitHub Packages or a private feed.
- TIER 5 extensions: Revit 2025, AutoCAD host, undo/redo ops, streaming progress, advanced element search.
