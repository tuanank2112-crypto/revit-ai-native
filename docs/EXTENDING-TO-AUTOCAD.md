# Extending to AutoCAD

The Autodesk Native Agent Runtime is architected to be portable to other Autodesk products. The core library (`AutodeskNativeAgent.Core`) contains zero Revit dependencies — all host-specific code lives in the `apps/` layer.

## What's Portable

| Component | Revit-specific? | Porting Effort |
|-----------|----------------|----------------|
| JSON library | No | None |
| Contracts (AgentPlan, ExecutionResult) | No | None |
| JobQueue / JobStateMachine | No | None |
| PipeClient / PipeProtocol | No | None |
| AuditLog / SecurityValidator | No | None |
| ConfirmationToken | No | None |
| PlanHasher / PlanValidator | Partially (registry) | Add AutoCAD operations |
| MCP TypeScript server | No | Change pipe name only |
| TypeResolver / LevelResolver | Yes | Rewrite for AutoCAD |
| Operation handlers | Yes | Rewrite for AutoCAD API |

## Porting Strategy

### 1. Create a new app project
```
apps/autocad-2025-addin/
├── AutocadNativeAgent.csproj
├── AgentAddInApplication.cs
├── Execution/
│   ├── MainThreadDispatcher.cs  (AutoCAD's Application.Idle event)
│   ├── PlanExecutor.cs         (same pipeline, AutoCAD API)
│   └── Operations/             (line.create, circle.create, etc.)
└── Pipe/
    └── PipeServer.cs           (reuse, just change pipe name)
```

### 2. Implement AutoCAD operations
- `line.create` → `Database.AddLine()`
- `circle.create` → `Database.AddCircle()`
- `text.create` → `Database.AddText()`
- `block.insert` → `BlockReference`
- `layer.create` → `LayerTable`

### 3. Register operations in a new CommandRegistry
The registry pattern is the same — just register AutoCAD-specific operations.

### 4. Use the same MCP server
The TypeScript MCP server is host-agnostic. Change the pipe name and it works.

### 5. Unit conversion
AutoCAD's internal unit is configurable. Use the same `UnitNames` abstraction, but add AutoCAD-specific unit systems (e.g., architectural inches).

## Key Differences

| Aspect | Revit 2024 | AutoCAD 2025 |
|--------|-----------|-------------|
| Framework | .NET 4.8 | .NET 8 |
| Threading | ExternalEvent | Application.Idle |
| Transaction | TransactionGroup | TransactionManager |
| Units | Decimal feet | Configurable |
| Element model | Element + ElementId | ObjectId + Handle |
| Family/Type | FamilySymbol | BlockTableRecord |

## Reusable Components

These can be shared as-is:
- `AutodeskNativeAgent.Core.dll` (the entire core library)
- `apps/agent-mcp/` (TypeScript MCP server)
- `scripts/` (build/install scripts — just change paths)
- `contracts/schemas/` (JSON schemas for shared operations)
