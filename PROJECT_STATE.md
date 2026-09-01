# PROJECT_STATE — Autodesk Native Agent Runtime

> Cập nhật lần cuối: 2026-09-01 · Hidden format: đây là trạng thái thực tế của project, không phải template.

## Mục tiêu của project

Bộ runtime "Autodesk Native Agent" cho phép AI agent (Antigravity/Claude...) điều khiển **Revit 2024** qua giao thức named-pipe + MCP (Model Context Protocol):

1. **Add-in Revit 2024** (`apps/revit-2024-addin`): nhận lệnh qua named-pipe, chạy plan an toàn trên main thread (ExternalEvent), trả kết quả.
2. **Core library** (`libs/agent-core`): contracts, JSON, validation, policy, security, plan đã được unittest.
3. **MCP server** (`apps/agent-mcp`): cầu nối AI ↔ add-in, expose các tool `revit_*`.
4. **Scripts + docs**: build, install, package, hướng dẫn test.

## Kiến trúc hiện tại

```
[MCP client / AI agent]
      │  stdio MCP
      ▼
apps/agent-mcp (TypeScript Node, @modelcontextprotocol/sdk@0.6)
      │  named-pipe: \\.\pipe\autodesk-native-agent-<user>
      ▼
apps/revit-2024-addin (C# net48, IExternalApplication)
  ├─ Pipe/PipeProtocol.cs     — envelope (version 1.0, requestId, [4-byte LE len][UTF-8 JSON])
  ├─ Pipe/PipeServer.cs       — listener thread, 1 client, framing
  ├─ Execution/MainThreadDispatcher.cs — ExternalEvent + BlockingCollection (main-thread bridge)
  ├─ Execution/AgentRequestRouter.cs   — protocol methods → read sync / plan async (job queue)
  ├─ Execution/PlanExecutor.cs         — preview/validate/commit, TransactionGroup, rollback
  └─ Execution/Operations/*.cs         — wall.create, door.insert, ... export.pdf
```

- Tên pipe user-scoped: `autodesk-native-agent-` + username lowercase alnum (trùng 2 bên C#/TS).
- Revit API chỉ gọi trên main thread qua `MainThreadDispatcher` (ExternalEvent idle pulse).

## Các module/file chính đã hoàn thành

### Core (`libs/agent-core`, target net48 + net8.0)
- `Json/JsonValue.cs`, `JsonParser.cs`, `JsonWriter.cs` — JSON DOM canonical + serialize.
- `Contracts/AgentPlan.cs`, `AgentError.cs`, `ExternalUnit.cs`, `Assertion.cs`, `ExecutionResult.cs` (gồm `ElementSummary`, `JobStatus`), `PreviewReport.cs`.
- `Contracts/CommandRegistry.cs` + `OperationDescriptor` — allowlist ~20 ops, affected-element accounting.
- `Contracts/SchemaCatalog.cs` — load embedded JSON Schema (logical name dùng **backslash** `operations\...`).
- `Validation/PlanValidator.cs`, `Execution/ResultReferenceResolver.cs`, `Policy/ProjectPolicy.cs`, `Security/SecurityValidator.cs`, `Execution/PlanHasher.cs`, `Execution/DocumentFingerprint.cs`.
- Schemas: `libs/contracts/schemas/**` (element-reference, pipe-envelope, agent-plan, assertion, execution-result, preview-report, project-policy, query, operations/* — **đủ 28/28 operations**).

### Add-in Revit 2024 (`apps/revit-2024-addin`, net48, RevitAPI 2024 @ D:\REVIT\2024)
- `AgentAddInApplication.cs` — IExternalApplication, OnStartup khởi tạo dispatcher/jobQueue/auditLog/router/pipecServer.
- `Execution/AgentRequestRouter.cs` — hello/status/capabilities/document.*/selection.get/element.query/plan.{validate,preview,commit,confirm}/job.*/audit.log.
- `Execution/Operations/`:
  - wall.create, wall.update, door.insert, window.insert, parameter.set, parameter.set_many, element.delete, element.move, element.rotate, element.rename, room.create, sheet.create, sheet.place_view, view.create_plan, document.save, document.save_as, export.pdf, export.dwg.
- `Execution/ElementResolver.cs`, `TypeResolver.cs`, `ElementQueryEngine.cs`, `UnitNames.cs`, `LevelResolver.cs`, `AgentException.cs`, `JobQueue.cs`, `AuditLog.cs`, `ConfirmationTokenStore.cs`.

### MCP server (`apps/agent-mcp`, TypeScript ESM)
- `src/index.ts` — 12 tools: revit_get_status, get_capabilities, inspect_document, inspect_selection, query_elements, validate_plan, preview_plan, commit_plan, get_job_status, cancel_job, rollback_job, get_audit_log.
- `src/pipe-client.ts` — PipeClient: connect (net.createConnection path), request correlation, heartbeat 15s, reader buffer 4-byte LE length, timeout 30s.

## Contract/API quan trọng

- **Envelope (pipe)**: `{ protocolVersion, requestId, type: "request|response|heartbeat", method?, payload?, data?, error?, success?, timestampUtc, correlationId? }`.
- **Plan**: `{ schemaVersion, requestId, description, document, units, coordinateSystem, executionMode, operations[], safety{} }`; op: `{ id, op, args, dependsOn[], assertions[] }`.
- **Reference target**: `{ viaOperationResult: "$result.<id>" | operationResult | uniqueId | elementId }` (KHÔNG có strategy "selection" trong ElementResolver).
- **JobStatus**: WaitingForRevit / AwaitingConfirmation / Running / Completed / Failed / Cancelled / RolledBack.

## Quyết định kỹ thuật đã chốt

1. **Revit 2024 + net48**: giữ nguyên (theo AGENT_RULES), RevitAPI tại `D:\REVIT\2024\Revit 2024` (non-default path) → add-in csproj tham chiếu absolute path.
2. **Revit API mới**: dùng `SpecTypeId.Length/Area` + `Units.GetFormatOptions(ForgeTypeId)`, `Element.LookupParameter(name)`, `ElementId(long)`/`.Value`, `Document.Create.NewRoom(Level, UV)` (KHÔNG có NewRoom(Level, Phase)), PDF `Export(folder, IList<ElementId>, PDFExportOptions)` 3 tham số, `SaveAsOptions.MaximumBackups`.
3. **string.Contains(comparison)** không tồn tại trên net48 → dùng `IndexOf(..., StringComparison) >= 0`.
4. **Enum.TryParse** dùng overload ignoreCase + `Enum.IsDefined`.
5. **Parameter.Set(bool)** không tồn tại → `Set(bool ? 1 : 0)`.
6. **TransactionGroup** không có `IsFailureConsistent`.
7. **Pipe name** user-scoped, trùng C#/TS.
8. **MCP server** chạy stdio; pipe client ESM dùng `import { randomFillSync }` (không dùng require).
9. Scripts PowerShell phải có **UTF-8 BOM** (PS 5.1 đọc sai em dash `—` nếu không BOM).
10. `ElementId.IntegerValue` deprecated Revit 2024 → `.Value` (long); riêng `WorksetId` CHỈ có `IntegerValue`.

## Trạng thái hiện tại (2026-09-01, sau phase 5–6 — release v1.1.0)

- ✅ **Build solution 0 error / 0 warning** (Release). **73/73 tests pass**. MCP `npm run build` OK.
- ✅ **JSON Schema validation đầy đủ 28/28 ops** (10 schema lần 1 + 14 schema lần 2, register trong CommandRegistry).
- ✅ **8 op mới Tier-1 đã implement + register + dispatch (tổng 28 ops)**: family.load, family.instance.create, column.create, beam.create, slab.create, roof.create, view.create_section, view.create_elevation.
- ✅ **Add-in bản 28 ops ĐÃ INSTALL + E2E THẬT trên Revit 2024 (Project1, 2026-09-01)**:
  - Probe Tier-1: 7/7 ops completed, 8/8 assertions pass (beam length 4000mm diff=0).
  - **Nhà 2 tầng** (`artifacts/e2e/house-2story.json`): 18/18 ops completed (10 wall + slab + roof + section + door + 4 window), 18/18 assertions pass, 0 errors. elementId: walls 1594010–1594019, slab 1594022, roof 1594029, section 1594045, door 1594053, windows 1594061/73/78/83.
  - Cleanup probe: 7/7 element.delete completed, 0 errors — model chỉ còn 18 element nhà.
- ✅ **Hardening phase 5 (4 fix)**: token single-use (MarkUsed sau commit), export.pdf SecurityValidator + không auto-create dir, family.load .rfa/.fam + canonical path, dispatcher log exception thay vì nuốt.
- ✅ **Phase 6**: MCP build pass; release zip v1.1.0 (`artifacts/AutodeskNativeAgent-1.1.0.zip`) + NuGet Core nupkg; ci.yml + release.yml sẵn sàng.
- ⚠️ **Implementation quirks đã ghi nhận**: slab.create → Ceiling.Create (category "Ceilings"); beam.create → structural Wall.Create; roof overhang warning khi footprint đã mở rộng sẵn (mái vẫn đủ overhang thực tế); door resolved.level trả rỗng nhưng tạo đúng.
- ⚠️ **Confirm flow**: token dùng đúng 1 lần; sau confirm job resume chạy theo Revit idle pulse (có thể trễ vài chục giây) — poll ≥2–3 phút, confirm duy nhất 1 lần.
- E2E 12/12 (session trước, 20 ops) vẫn hợp lệ cho Tier 0.

## Bước tiếp theo chính xác

1. (User quyết định) **Commit + push lên GitHub** — 25+ file thay đổi chưa commit; subagent không tự commit main.
2. (User quyết định) **Tag v1.1.0** → release.yml tự build release artifact.
3. (User quyết định) **Publish NuGet** `AutodeskNativeAgent.Core` lên GitHub Packages/feed.
4. **Backlog Tier-2** (roadmap đã có): group.create/ungroup, view.create_3d, railing.create, buildingpad.create, MEP…
5. **Backlog Tier-5**: Revit 2025, AutoCAD host, undo/redo ops, streaming progress.

## Lệnh build/test cần chạy

```powershell
# Full build (Release)
.\scripts\build-all.ps1 -Configuration Release

# Build từng phần
dotnet build "libs\agent-core\AutodeskNativeAgent.Core.csproj" -c Release
dotnet build "apps\revit-2024-addin\AutodeskNativeAgent.Revit2024.csproj" -c Release
cd apps\agent-mcp; npm install; npm run build

# Tests
dotnet test "tests\unit\AutodeskNativeAgent.Core.Tests\AutodeskNativeAgent.Core.Tests.csproj" -c Release

# Package
.\scripts\package-release.ps1 -Version 1.0.0
```

## Known limitations (cập nhật 2026-08-28)

- Add-in hỗ trợ **tối đa 4 client pipe** đồng thời; mỗi client 1 thread.
- MCP server timeout request 30s; plan commit chạy async (job queue).
- `element.delete` yêu cầu `requireUserConfirmation`.
- `Wall.Width` + `WallType.Width` **READ-ONLY** — đổi bề dày phải qua WallType.
- `Stairs.Create` **ABSENT** — cầu thang native không tạo được parent → fallback family instance.
- `ReferencePlane.Create` ABSENT → `NewExtrusionRoof` không dùng được (mái chỉ footprint).
- `CurveLoop` không có 1-arg ctor; `CurveArray`/`ModelCurveArray` phải `new`+`Append`.
- `StructuralType` chỉ 3 giá trị (NonStructural/Column/Beam); `OST_Beams` ABSENT.
- Source KHÔNG có strategy `"selection"` cho element reference.