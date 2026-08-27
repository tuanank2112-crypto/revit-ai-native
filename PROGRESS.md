# PROGRESS — Autodesk Native Agent Runtime

> Status: **BUILD PASS — DONE (chờ verify thực tế trong Revit)**.
> Cập nhật cuối: 2026-08-27. Source code là source of truth; PROGRESS.md phản ánh trạng thái hiện tại.

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
| 9 — Tests | **DONE (54/54)** | JsonTests, SchemaTests, AgentPlanTests, ValidationTests, ResultTests pass cả Debug & Release. |
| 10 — Build & Installer | **DONE** | Solution, csprojs, build-all/clean/install/uninstall/verify/package-release/start-mcp scripts (UTF-8 BOM), .addin manifest, release zip tạo OK. |
| 11 — Documentation | **DONE** | README (nếu có), ARCHITECTURE, SECURITY, COMMANDS, TROUBLESHOOTING, REVIT-2024-DEVELOPMENT, EXTENDING-TO-AUTOCAD, MANUAL-TEST (10 tests), samples/plans (6 files). |

## Kết quả build/test (2026-08-27 - re-verified)

- `dotnet build` solution Debug/Release: **0 error, 0 warning** (đã fix xUnit2013 ở AgentPlanTests.cs:47 → Assert.Single).
- `dotnet test` (net8.0): **54/54 pass** (Debug lẫn Release).
- `npm run build` (apps/agent-mcp): **pass**.
- `.\\scripts\\build-all.ps1 -Configuration Release`: **pass**.
- `.\\scripts\\package-release.ps1 -Version 1.0.0`: **tạo `artifacts\\AutodeskNativeAgent-1.0.0.zip` OK**.
- **Git repo đã init** (2026-08-27) — commit đầu tiên chứa toàn bộ source.

## Kết quả build/test (2026-08-11 - lần đầu)

- `dotnet build` solution Debug/Release: **0 error, 0 warning** (sau khi sửa ElementId.Value, WorksetId.IntegerValue, XML cref, usings).
- `dotnet test` (net8.0): **54/54 pass** (Debug lẫn Release).
- `npm run build` (apps/agent-mcp): **pass**.
- `.\scripts\build-all.ps1 -Configuration Release`: **pass**.
- `.\scripts\package-release.ps1 -Version 1.0.0`: **tạo `artifacts\AutodeskNativeAgent-1.0.0.zip` OK**.

## Các fix quan trọng trong phiên này (từ 54/54 pass đến build 0/0)

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

## Chưa làm được (blocker khách quan)

- **E2E trong Revit**: máy không có Revit 2024 đang chạy → chưa cài add-in, chưa chạy 11 manual tests.
- Cần người dùng: mở Revit 2024 → chạy install → verify → start MCP → test theo MANUAL-TEST.md.
- **Chưa có integration test** cho pipe framing (chỉ có unit test contract logic).
- **Chưa có CI/CD** pipeline.

## Next steps

1. ✅ (xong) Fix xUnit2013 warning + `git init` + commit đầu tiên (2026-08-27).
2. Thêm integration test cho pipe framing + envelope round-trip.
3. Tạo GitHub Actions CI/CD (build, test, MCP build, release).
4. Người dùng: `.\\scripts\\install-revit2024.ps1 -Configuration Release`
5. Mở/restart Revit 2024 (add-in tự nạp).
6. `.\\scripts\\verify-installation.ps1`
7. `.\\scripts\\start-mcp.ps1` hoặc cấu hình MCP client `node dist/index.js`.
8. Chạy `docs\\MANUAL-TEST.md` TEST 1–11, sửa nếu có lỗi runtime.
