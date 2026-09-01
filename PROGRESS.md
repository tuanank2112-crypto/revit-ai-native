# PROGRESS — Autodesk Native Agent Runtime

> Status: **RELEASE v1.1.0 — E2E PASS trên Revit 2024 thật (probe 7/7 + nhà 2 tầng 18 ops, 26/26 assertions); schema coverage 28/28 ops, 73/73 unit tests, hardening 4 fix (2026-09-01)**.
> Cập nhật cuối: 2026-09-01. Source code là source of truth; PROGRESS.md phản ánh trạng thái hiện tại.

## Phase status

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — Shared Contracts | **DONE** | JsonValue/Parser/Writer, AgentPlan, AgentError, ExternalUnit, Assertion, ExecutionResult, PreviewReport, CommandRegistry, SchemaCatalog, DocumentFingerprint, PlanHasher, PlanValidator, ProjectPolicy, SecurityValidator. Schemas embedded. |
| 2 — Core Runtime | **DONE** | Registry allowlist, validation pipeline, policy, audit log (add-in), job state machine (JobQueue), confirmation tokens, security path validation. |
| 3 — Named Pipe | **DONE** | PipeProtocol + PipeServer (framing 4-byte LE, envelope v1.0, user-scoped name, heartbeat, timeout, error envelope). Client TS tương thích (đã verify mã 2 bên). |
| 4 — Revit Add-in | **DONE** | IExternalApplication, MainThreadDispatcher (ExternalEvent + BlockingCollection), queue, router, active-document context, audit. |
| 5 — Revit Read Operations | **DONE** | document.get_info, selection.get, level.list, view.list, sheet.list, family.list, family_type.list, element.get, element.query, element.get_parameters, element.get_bounding_box, element.get_location, category.list, workset.list. |
| 6 — Revit Write Operations | **DONE** | wall.create, wall.update, door.insert, window.insert, parameter.set, parameter.set_many, element.delete, element.move, element.rotate, element.rename, room.create, sheet.create, sheet.place_view, view.create_plan, document.save, document.save_as, export.pdf, export.dwg. |
| 7 — Safety | **DONE** | Preview, plan hash, document fingerprint, confirmation, TransactionGroup, assertions, rollback, failure preprocessor, stale reference detection, path validation, affected-element limit. |
| 8 — MCP Server | **DONE** | apps/agent-mcp TypeScript, tsc build pass, 12 tools, pipe-client ESM hoàn chỉnh. |
| 9 — Tests | **DONE (73/73)** | JsonTests, SchemaTests (28/28 ops), AgentPlanTests, ValidationTests, ResultTests pass cả Debug & Release. |
| 10 — Build & Installer | **DONE** | Solution, csprojs, build-all/clean/install/uninstall/verify/package-release/start-mcp scripts (UTF-8 BOM), .addin manifest, release zip tạo OK. |
| 11 — Documentation | **DONE** | README (nếu có), ARCHITECTURE, SECURITY, COMMANDS, TROUBLESHOOTING, REVIT-2024-DEVELOPMENT, EXTENDING-TO-AUTOCAD, MANUAL-TEST (10 tests), samples/plans (6 files). |

## Kết quả build/test (2026-09-01 — số liệu verification mới nhất)

- `dotnet build` solution Release: **0 error, 0 warning** (gồm 4 fix hardening: token single-use, export.pdf SecurityValidator, family.load path check, dispatcher exception log).
- `dotnet test` (net8.0): **73/73 pass** (Release).
- `npm run build` (apps/agent-mcp): **pass**.
- `.\scripts\package-release.ps1 -Version 1.1.0`: **tạo `artifacts\AutodeskNativeAgent-1.1.0.zip` OK** (add-in DLL + manifest, MCP dist, scripts, samples, NuGet Core nupkg).
- **E2E Tier-1 trên Revit thật (Project1)**: probe 7 ops (column/family/beam/slab/roof/section/elevation) — 7/7 completed, 8/8 assertions pass.
- **E2E nhà 2 tầng (house-2story.json)**: 10 wall + door + 4 window + slab + roof + section = 18/18 ops completed, 18/18 assertions pass, 0 errors.
- **Git repo đã init** (2026-08-27) — commit đầu tiên chứa toàn bộ source.

## Kết quả build/test (2026-08-11 - lần đầu)

- `dotnet build` solution Debug/Release: **0 error, 0 warning** (sau khi sửa ElementId.Value, WorksetId.IntegerValue, XML cref, usings).
- `dotnet test` (net8.0): **73/73 pass** (số liệu verification mới nhất; lần verify đầu 2026-08-11 là 54/54).
- `npm run build` (apps/agent-mcp): **pass**.
- `.\scripts\build-all.ps1 -Configuration Release`: **pass**.
- `.\scripts\package-release.ps1 -Version 1.0.0`: **tạo `artifacts\AutodeskNativeAgent-1.0.0.zip` OK**.

## Các fix quan trọng trong phiên này (từ bộ test đầu tiên đến build 0/0)

1. SchemaCatalog: logical resource name `operations\...` (backslash) thay vì `operations.`.
2. ExportPdf/ExportDwg: enums Revit 2024 (ACADVersion, ExportPaperFormat, ColorDepthType), overload PDF 3 tham số.
3. WallUpdate/WindowInsert/RoomCreate/SaveAs: BuiltInParameter đúng, NewRoom(Level, UV), MaximumBackups.
4. get_Parameter(string) → LookupParameter; Contains(comp) → IndexOf; UnitType → SpecTypeId/GetDataType.
5. UIControlledApplication không có ActiveUIDocument → lưu title/path khi ExecutePlan chạy.
6. TransactionGroup bỏ IsFailureConsistent; JsonValue.IsBool → IsBoolean; BlockingCollection bỏ capacity.
7. ElementId.IntegerValue (deprecated) → .Value; WorksetId giữ IntegerValue.
8. MCP TS: bỏ require('crypto') trong ESM → import randomFillSync; args null-safe.
9. PowerShell scripts: thêm UTF-8 BOM (em dash bị đọc sai trên PS 5.1).
10. Thêm samples/plans/sample-move-element.json (thiếu so với MANUAL-TEST TEST 5).

## E2E trong Revit 2024 (2026-08-27) — ✅ XONG

- Add-in tự load (`LoadingPolicy=AlwaysLoad` trong manifest, commit `6bc2b33`).
- **12/12 tests pass** qua named pipe → add-in → main-thread dispatcher → PlanExecutor → Revit API:
  status, document.get_info, plan.validate, plan.preview, plan.commit → awaiting_confirmation,
  plan.confirm, commit completed + assertions (wall+door), element.move +1000mm (qua `$result.*` ref),
  rollback on assertion failure, fingerprint guard, export.pdf, audit.log.
- Fixes runtime đã làm trong quá trình E2E (chi tiết trong `git log`):
  PipeClient deadlock, PipeServer dispose stream sớm, missing `revit_confirm_plan`, status cached document,
  confirmation-resume state transition, token double-Accept, `JsonValue.String(null)` crash với `$result.*`.
- Audit log giờ **persistence ra file**: `%LOCALAPPDATA%\AutodeskNativeAgent\logs\audit.jsonl` (JSON Lines, append-only).
- Core library đã pack được **NuGet package** `AutodeskNativeAgent.Core 1.0.0` (net48 + net8.0); `package-release.ps1` pack + include vào release zip.

## Chưa làm được local (cần GitHub / decision)

- **Branch protection + CI chạy thật**: workflows `ci.yml`/`release.yml` đã có nhưng repo chưa có remote GitHub.
- **NuGet feed**: publish `AutodeskNativeAgent.Core.nupkg` lên GitHub Packages / feed private.
- **TIER 5 (feature extensions, kế hoạch dài hạn)**: Revit 2025, AutoCAD, undo/redo ops, streaming results, advanced element search.

## Next steps

1. ✅ (2026-08-27) Fix xUnit2013 + git init + integration tests + CI workflows.
2. ✅ (2026-08-27) E2E 12/12 pass trong Revit 2024 thật + audit log file-based + NuGet pack + tag v1.0.0.
3. Push repo lên GitHub → bật branch protection, CI chạy tự động.
4. Publish `AutodeskNativeAgent.Core.nupkg` lên GitHub Packages (hoặc feed private).
5. (Tuỳ chọn) Chạy lại E2E sau khi cài bản có audit-log-file: đóng Revit → `install-revit2024.ps1` → mở lại.
6. TIER 5 theo kế hoạch dài hạn (Revit 2025, AutoCAD, …).
