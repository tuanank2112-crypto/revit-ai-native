# PROJECT_STATE — Autodesk Native Agent Runtime

> Cập nhật lần cuối: 2026-08-11 · Hidden format: đây là trạng thái thực tế của project, không phải template.

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
- Schemas: `libs/contracts/schemas/**` (element-reference, pipe-envelope, agent-plan, assertion, execution-result, preview-report, project-policy, query, operations/{wall.create,door.insert,element.delete,parameter.set}).

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

## Việc đang làm dở / còn thiếu

- ⛔ **Chưa thể test end-to-end trong Revit** trên máy này (Revit đang đóng) → test manual theo `docs/MANUAL-TEST.md` (10 tests) bắt buộc chạy trên máy có Revit 2024 đang chạy.
- ⛔ **Chưa cài add-in vào Revit** (`scripts/install-revit2024.ps1` chưa chạy; cần restart Revit).
- Chưa có test unit cho pipe framing (framing đã verify bằng so sánh mã 2 bên, chưa tự động).

## Compile/build errors hiện tại

- **Không có.** Toàn solution build 0 error / 0 warning (Release, --no-incremental) tại thời điểm state này.
- `dotnet test` 54/54 pass.

## Các phần còn thiếu

- Cài đặt & xác minh thực tế trong Revit (blocker: cần Revit 2024 mở).
- Audit logs output thực tế kiểm tra sau khi chạy add-in.

## Bước tiếp theo chính xác

1. (Người dùng) Mở Revit 2024, tạo project.
2. Chạy `.\scripts\install-revit2024.ps1 -Configuration Release` (cài add-in + .addin + Core DLL).
3. Restart Revit → add-in tự nạp (pipe `autodesk-native-agent-<user>`).
4. Chạy `.\scripts\verify-installation.ps1`.
5. Chạy `.\scripts\start-mcp.ps1` (hoặc connect MCP client trực tiếp tới `node dist/index.js`).
6. Test theo `docs/MANUAL-TEST.md` TEST 1→11.

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

## Known limitations

- Add-in chỉ hỗ trợ **1 client pipe** tại một thời điểm (NamedPipeServerStream max 1).
- MCP server timeout request 30s; plan commit chạy async (job queue) — status qua `revit_get_job_status`.
- Preview không hỗ trợ `element.move/rotate` mang tính thực thi (resolution only).
- `element.delete` yêu cầu `requireUserConfirmation` (rollbackOnValidationFailure).
- export.pdf dùng `Document.Export(folder, views, options)` — ghi đè file cùng tên theo giây (không có baseName).
- Source hiện KHÔNG có strategy `"selection"` cho element reference — dùng uniqueId/elementId/viaOperationResult.