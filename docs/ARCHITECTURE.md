# Architecture

## Overview

The Autodesk Native Agent Runtime connects AI agents to Revit 2024 through a three-layer architecture:

```
┌─────────────────────────────────────────────────┐
│  AI Agent (Antigravity, Claude, etc.)            │
│  - Sends MCP tool calls over stdio               │
└──────────────────┬──────────────────────────────┘
                   │ MCP (JSON-RPC over stdio)
┌──────────────────▼──────────────────────────────┐
│  MCP Server (TypeScript / Node.js)              │
│  - Validates input                              │
│  - Sends pipe requests to Revit add-in          │
│  - Maps structured errors to MCP responses      │
└──────────────────┬──────────────────────────────┘
                   │ Named Pipe (length-prefixed JSON)
┌──────────────────▼──────────────────────────────┐
│  Revit 2024 Add-in (.NET Framework 4.8)         │
│  - PipeServer (background listener)              │
│  - MainThreadDispatcher (ExternalEvent)         │
│  - AgentRequestRouter (method dispatch)          │
│  - PlanExecutor (validate → resolve → commit)   │
│  - Operation handlers (wall, door, parameter…)  │
│  - AuditLog (JSONL persistence)                  │
│  - ConfirmationTokenStore (safety)              │
└─────────────────────────────────────────────────┘
```

## Threading Model

Revit's API is apartment-threaded: it can only be called from the thread that owns the Document.

1. **Background thread**: `PipeServer` receives JSON envelopes
2. **Enqueue**: Request is routed to `AgentRequestRouter`, which enqueues a `MainThreadWorkItem`
3. **ExternalEvent**: `MainThreadDispatcher.Raise()` fires an `ExternalEvent`
4. **Revit main thread**: `Execute(UIApplication)` processes the queue
5. **PlanExecutor**: Runs the full pipeline on the main thread
6. **Response**: Written back through the pipe to the MCP server

No Revit API call ever happens on a background thread.

## Plan Execution Pipeline

```
1. Plan Validation
   ├── Schema validation (JSON Schema)
   ├── Operation allowlist (CommandRegistry)
   ├── Dependency graph (cycle detection, missing deps)
   └── Safety envelope (max elements, confirmation)

2. Reference Resolution
   ├── Document fingerprint check
   ├── $result.<opId> chains (ResultReferenceResolver)
   └── Stale reference detection

3. Preview (dry-run)
   ├── Resolve levels, types, hosts
   ├── Estimate affected elements
   └── Surface blockers (type not found, host invalid)

4. Commit (transactional)
   ├── TransactionGroup.Start()
   ├── Topological sort of operations
   ├── Execute each operation:
   │   ├── Resolve element references
   │   ├── Convert units
   │   ├── Call Revit API
   │   ├── Regenerate
   │   └── Verify (read back, compare tolerance)
   ├── Evaluate assertions
   │   └── Fail → RollBack()
   └── TransactionGroup.Assimilate()

5. Result
   ├── OperationResult per operation
   ├── AssertionResult per assertion
   ├── RollbackInfo if rolled back
   └── AgentError[] for any failures
```

## Safety Guarantees

- **Atomic by default**: All operations in a plan succeed or the entire plan rolls back
- **Confirmation tokens**: Single-use, plan-hash-bound, time-limited
- **Document fingerprint**: Commit refuses to run if the document changed since preview
- **Path security**: Export/SaveAs paths are canonicalized and checked for traversal
- **Unit safety**: All lengths carry explicit units; conversion uses `UnitNames.ToFeet()`
- **Audit trail**: Every plan execution is logged as JSONL

## Key Contracts

| Contract | C# Type | JSON Schema |
|----------|---------|-------------|
| Plan | `AgentPlan` | `agent-plan.schema.json` |
| Operation | `PlanOperation` | Per-op schemas |
| Result | `ExecutionResult` | `execution-result.schema.json` |
| Preview | `PreviewReport` | `preview-report.schema.json` |
| Error | `AgentError` | Inline |
| Element Reference | `ElementReference` | `element-reference.schema.json` |
| Pipe Envelope | `PipeProtocol` | `pipe-envelope.schema.json` |

## Project Structure

```
revit-A.I Native/
├── libs/
│   └── agent-core/           # Shared core (net48 + net8.0)
│       ├── Contracts/        # AgentPlan, ExecutionResult, etc.
│       ├── Execution/        # JobQueue, JobStateMachine, PlanHasher
│       ├── Identity/         # DocumentFingerprint
│       ├── Json/             # JsonValue, JsonParser, JsonWriter
│       ├── Pipe/             # PipeClient
│       ├── Policy/           # AuditLog, ConfirmationToken, SecurityValidator
│       └── Validation/       # PlanValidator, JsonSchemaValidator
├── apps/
│   ├── revit-2024-addin/     # Revit add-in (net48)
│   │   ├── Execution/       # PlanExecutor, routers, resolvers
│   │   ├── Operations/      # Wall, door, parameter, etc.
│   │   └── Pipe/            # PipeServer, PipeProtocol
│   └── agent-mcp/           # MCP TypeScript server
│       └── src/             # index.ts, pipe-client.ts
├── tests/
│   └── unit/                # Unit tests (no Revit dependency)
├── scripts/                 # Build, install, release scripts
├── samples/                 # Sample plans
├── docs/                    # Documentation
└── contracts/               # JSON Schema files
```
