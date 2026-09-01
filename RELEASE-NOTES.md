# RELEASE NOTES — v1.1.0 (2026-09-01)

## Summary

Feature release: **28 operations** (10 Tier-1 mới), JSON Schema validation đầy đủ 28/28 ops,
runtime hardening, và **E2E verified trên Revit 2024 thật** với workflow nhà 2 tầng hoàn chỉnh.

## Verified in this release

- **Build**: 0 errors, 0 warnings (Release)
- **Unit + integration tests**: 73/73 pass (net8.0) — schema coverage 28/28 ops
- **MCP server**: `npm run build` pass
- **E2E in live Revit 2024 (2026-09-01)**:
  - Tier-1 probe: 7/7 ops completed (column/family/beam/slab/roof/section/elevation), 8/8 assertions pass
  - **2-story house workflow** (`artifacts/e2e/house-2story.json`): 18/18 ops completed
    (10 walls, slab, roof, section, door, 4 windows), 18/18 assertions pass, 0 errors
  - Probe cleanup: 7/7 `element.delete` completed, 0 errors (house untouched)
- **Hardening (4 fixes)**:
  1. Confirmation token **single-use enforced** — `MarkUsed()` ngay sau commit (không replay được)
  2. `export.pdf` output directory validated qua `SecurityValidator` — không tự tạo thư mục bất kỳ
  3. `family.load` chỉ nhận `.rfa`/`.fam`, path phải canonical (chặn traversal)
  4. `MainThreadDispatcher` log exception thay vì nuốt lặng lẽ

## New operations (Tier 1)

`level.create`, `grid.create`, `family.load`, `family.instance.create`, `column.create`,
`beam.create`, `slab.create`, `roof.create`, `view.create_section`, `view.create_elevation`

> [!NOTE]
> Implementation quirks đã ghi nhận: `slab.create` dùng `Ceiling.Create` (category "Ceilings")
> vì Slab API absent; `beam.create` dùng 8-arg structural `Wall.Create`; roof overhang có thể
> bị warning nếu footprint đã mở rộng sẵn. Chi tiết: `docs/REVIT-2024-API-CATALOG.md`.

## Package

- `artifacts/AutodeskNativeAgent-1.1.0.zip` (add-in DLL + manifest, MCP dist, scripts, samples, NuGet Core)
- NuGet: `AutodeskNativeAgent.Core` (net48 + net8.0)

---

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
