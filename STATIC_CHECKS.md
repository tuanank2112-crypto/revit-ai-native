# STATIC_CHECKS — cross-file verification log

> Records static cross-checks performed while the terminal is unavailable, plus
> APIs that need live Revit DLL verification later.

## Cross-checks performed

| # | Check | Result |
|---|-------|--------|
| 1 | JsonValue / JsonParser / JsonWriter consistency | JsonParser throws `JsonException` with Position; JsonValue.FormatNumber throws same; both in `AutodeskNativeAgent.Core.Json`. No System.Text.Json dependency. |
| 2 | JsonValue API surface used by contracts | `AsString`, `AsDouble`, `AsInt`, `AsLong`, `AsBool`, `TryGetDouble`, `TryGetNonEmptyString`, `IsNull`, `IsObject`, `IsArray`, `Count`, `Items`, `Members`, `this[string]`, `this[int]`, `Has`, `Kind` — all present in JsonValue.cs. |
| 3 | SchemaCatalog embedded-resource names | csproj embeds `..\..\contracts\schemas\**\*.schema.json` with LogicalName `AutodeskNativeAgent.Core.Schemas.%(RecursiveDir)%(Filename)%(Extension)`. Operation schemas live at `operations/<op>.schema.json` → resource `AutodeskNativeAgent.Core.Schemas.operations.<op>.schema.json`. **MISMATCH**: `SchemaCatalog.LoadOperationSchema` builds `AutodeskNativeAgent.Core.Schemas.<op>.schema.json` (missing `operations.` segment). **FIXED via LoadOperationSchema using "operations." prefix** (see code). |
| 4 | CommandRegistry argument schemas | wall.create/door.insert/parameter.set/element.delete schema files exist under `schemas/operations/`; registry loads via SchemaCatalog.LoadOperationSchema — aligned after check #3. |
| 5 | door.insert `$ref` cross-file resolution | door.insert references `wall.create.schema.json#/$defs/levelSelector` and `...typeSelector`; RefResolver loads external file by basename `wall.create.schema.json` → resource `AutodeskNativeAgent.Core.Schemas.wall.create.schema.json` — **MISMATCH**: same operations/ segment issue. **Fixed in SchemaCatalog.LoadResolved** (basename lookup also tries `operations.` prefix). |
| 6 | parameter.set / element.delete `$ref ../element-reference.schema.json` | RefResolver strips path segments → basename `element-reference.schema.json` → root-level resource. OK. |
| 7 | AgentPlan round-trip | ToJson writes `coordinateSystem` via `CoordinateSystemToWire`; FromJson parses via `TryParseCoordinateSystem` — symmetric. |
| 8 | JobStatus wire tokens | ExecutionResult.StatusToWire matches execution-result.schema.json enum exactly (queued…timed_out). |
| 9 | PreviewStatus wire tokens | PreviewReport.StatusToWire matches preview-report.schema.json (ready/blocked/skipped/error). |
| 10 | OperationOutcome wire tokens | completed/failed/skipped/rolled_back match schema. |
| 11 | AgentError wire format | code/message/recoverable/details/suggestedAction matches pipe-envelope.schema.json `agentError`. |
| 12 | element-reference schema vs ElementResolver | Schema requires uniqueId/elementId/viaOperationResult (anyOf). ElementResolver reads `viaOperationResult` first, then `operationResult`, then uniqueId, then elementId. Schema does NOT list `operationResult` — door.insert schema uses `operationResult` in host object. ElementResolver handles both. OK. |
| 13 | Revit API thread-safety | LevelResolver/TypeResolver/ElementResolver/operations are pure Revit-API code — must only run on main thread via dispatcher. Executor enforces. |
| 14 | net48 API surface | No `record`, no `required`, no `init`, no `System.Text.Json`, no `Span<T>` in shared code. `string.CompareOrdinal(text, index, literal, 0, len)` is net48-safe. |
| 15 | TypeResolver `symbol.Name.Contains(name, OrdinalIgnoreCase)` | `string.Contains(string, StringComparison)` is net48-safe (added .NET Framework 4.0). OK. |

## APIs requiring live Revit DLL verification

| API | Risk | Mitigation |
|-----|------|------------|
| `Wall.Create(Document, Curve, ElementId, bool)` | signature check | Standard Revit 2024 API — verify at build with REVIT_2024_PATH. |
| `FamilyInstanceFilter(Document, ElementId)` | ctor availability | Revit 2018+ API — verify at build. |
| `LocationCurve.Curve.Evaluate(double, bool)` | OK | standard. |
| `Document.Create.NewFamilyInstance(XYZ, FamilySymbol, Element, StructuralType)` | OK | standard. |
| `View.CropBox.Transform` | property exists on View | verify at build. |
| `ProjectLocation.GetTransform()` | returns Transform | verify at build. |
| `Parameter.Set(double)` for length params | OK | standard. |
| `element.flipFacing()/flipHand()` | FamilyInstance methods | verify at build. |

## High-risk compile points

- SchemaCatalog resource-name alignment (operations/ directory) — must keep in sync with csproj embedding rule.
- RefResolver cross-file load of `wall.create.schema.json` when used from `door.insert.schema.json`.
- Add-in csproj: Revit DLLs referenced with `Private=false` — good (no copy to output).
- `AutodeskNativeAgent.Core.csproj` multi-targets net48;net8.0 — watch for any net8-only API sneaking in.

## Dependencies not yet verifiable

- NuGet restore (Microsoft.NET.Test.Sdk 17.9.0, xunit 2.7.1, xunit.runner.visualstudio 2.5.8).
- Node/npm availability for apps/agent-mcp.
- Revit 2024 install path (`REVIT_2024_PATH` or default `C:\Program Files\Autodesk\Revit 2024`).
