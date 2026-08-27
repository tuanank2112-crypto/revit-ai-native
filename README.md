# Autodesk Native Agent Runtime for Revit 2024

A production-grade bridge between AI agents (Antigravity, Claude, etc.) and Autodesk Revit 2024, built on the Model Context Protocol (MCP).

## Architecture

```
Antigravity / AI Agent
    ↓ (MCP over stdio)
MCP Server (TypeScript, Node.js)
    ↓ (Named Pipe, JSON envelopes)
Revit 2024 Add-in (.NET Framework 4.8)
    ↓ (ExternalEvent)
Main-thread Dispatcher → PlanExecutor
    ↓ (TransactionGroup)
Revit API (Document, Wall, FamilyInstance, ...)
    ↓ (Verification + Assertions)
Structured Result → MCP Response
```

## Quick Start

### 1. Prerequisites

- **Revit 2024** installed (required for add-in build)
- **.NET SDK** (for building C# projects)
- **Node.js 18+** (for MCP TypeScript server)
- **PowerShell** (for build/install scripts)

### 2. Set REVIT_2024_PATH

If Revit is not in the default location (`C:\Program Files\Autodesk\Revit 2024`), set the environment variable:

```powershell
$env:REVIT_2024_PATH = "D:\REVIT\2024\Revit 2024"
```

### 3. Build

```powershell
.\scripts\build-all.ps1
```

### 4. Install Add-in

```powershell
.\scripts\install-revit2024.ps1
```

### 5. Open Revit 2024

Open or create a Revit project. The add-in starts the named-pipe server automatically.

### 6. Start MCP Server

```powershell
.\scripts\start-mcp.ps1
```

### 7. Configure Antigravity

Add the MCP server to your Antigravity configuration:

```json
{
  "mcpServers": {
    "revit": {
      "command": "node",
      "args": ["e:\\revit-A.I Native\\apps\\agent-mcp\\dist\\index.js"]
    }
  }
}
```

### 8. Get Status

Call `revit_get_status` from your agent. You should see:

```json
{
  "connected": true,
  "activeDocumentTitle": "YourProject.rvt",
  "busy": false
}
```

### 9. Preview a Plan

Send a plan to `revit_preview_plan`:

```json
{
  "schemaVersion": "1.0",
  "requestId": "test-001",
  "description": "Create a 5000mm wall",
  "document": { "strategy": "active_document" },
  "units": "mm",
  "coordinateSystem": "project",
  "executionMode": "preview",
  "operations": [...],
  "safety": { "requireUserConfirmation": true }
}
```

### 10. Approve & Commit

Call `revit_commit_plan` with the same plan. If confirmation is required, approve in Revit.

### 11. Inspect Audit Log

Audit logs are at `%LOCALAPPDATA%\AutodeskNativeAgent\logs` as JSONL files.

## Supported Operations

| Operation | Status |
|-----------|--------|
| `wall.create` | ✅ Supported |
| `wall.update` | ✅ Supported |
| `element.move` | ✅ Supported |
| `element.rotate` | ✅ Supported |
| `element.delete` | ✅ Supported |
| `element.rename` | ✅ Supported |
| `parameter.set` | ✅ Supported |
| `parameter.set_many` | ✅ Supported |
| `door.insert` | ✅ Supported |
| `window.insert` | ✅ Supported |
| `room.create` | ✅ Supported |
| `view.create_plan` | ✅ Supported |
| `sheet.create` | ✅ Supported |
| `sheet.place_view` | ✅ Supported |
| `document.save` | ✅ Supported |
| `document.save_as` | ✅ Supported |
| `export.pdf` | ✅ Supported |
| `export.dwg` | ✅ Supported |

## MCP Tools

| Tool | Description |
|------|-------------|
| `revit_get_status` | Connection status and document info |
| `revit_get_capabilities` | Protocol version, methods, operations |
| `revit_inspect_document` | Active document identity |
| `revit_inspect_selection` | Current selection |
| `revit_query_elements` | Structured element query |
| `revit_validate_plan` | Validate without executing |
| `revit_preview_plan` | Dry-run resolution |
| `revit_commit_plan` | Execute in a transaction |
| `revit_get_job_status` | Job status and result |
| `revit_cancel_job` | Cancel queued/running job |
| `revit_rollback_job` | Rollback completed job |
| `revit_get_audit_log` | Sanitized audit entries |

## Key Design Decisions

1. **No Revit API from background threads** — All Revit API calls go through the ExternalEvent dispatcher
2. **Atomic execution** — Plans run inside a single TransactionGroup; failures roll back
3. **Structured errors** — Every failure carries a stable error code
4. **Unit safety** — All lengths carry explicit units; no hardcoded `/ 304.8`
5. **Type safety** — Type/level resolution is deterministic; no random `FirstElement()`
6. **Path security** — Export/SaveAs paths are canonicalized and checked for traversal
7. **Confirmation tokens** — Destructive operations require human approval

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Security](docs/SECURITY.md)
- [Commands](docs/COMMANDS.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Revit 2024 Development](docs/REVIT-2024-DEVELOPMENT.md)
- [Extending to AutoCAD](docs/EXTENDING-TO-AUTOCAD.md)
- [Manual Test Checklist](docs/MANUAL-TEST.md)

## License

MIT
