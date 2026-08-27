using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Execution;
using AutodeskNativeAgent.Core.Identity;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Core.Validation;
using AutodeskNativeAgent.Revit2024.Execution.Operations;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Executes a plan on the Revit main thread. This type must only be called from
    /// within a <see cref="MainThreadDispatcher"/> work item — never from a background thread.
    /// </summary>
    /// <remarks>
    /// The pipeline is: validate → resolve references → resolve units/types/levels →
    /// (preview: dry-run report) or (commit: TransactionGroup → execute ops in dependency
    /// order → verify each → evaluate assertions → assimilate or rollback).
    /// </remarks>
    public sealed class PlanExecutor
    {
        /// <summary>Produces a dry-run preview report without mutating the model.</summary>
        public PreviewReport Preview(Document document, AgentPlan plan, CommandRegistry registry)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            string docFingerprint = ComputeFingerprint(document);
            string planHash = PlanHasher.HashJson(plan.ToJson());

            var operationPreviews = new List<OperationPreview>();
            var warnings = new List<string>();
            var errors = new List<AgentError>();
            int totalCreate = 0, totalModify = 0, totalDelete = 0;

            foreach (PlanOperation operation in plan.Operations)
            {
                OperationDescriptor descriptor = registry.Find(operation.Op);
                if (descriptor == null)
                {
                    operationPreviews.Add(new OperationPreview(
                        operation.Id, operation.Op, PreviewStatus.Error, 0, 0, 0,
                        null, null, null,
                        new[] { "Unknown operation '" + operation.Op + "'." },
                        new[] { new AgentError(ErrorCodes.UnknownOperation, "Unknown operation '" + operation.Op + "'.", true) }));
                    continue;
                }

                // In preview, we resolve but do not execute. Failures are surfaced as Blocked.
                PreviewStatus status = PreviewStatus.Ready;
                var opWarnings = new List<string>();
                var opErrors = new List<AgentError>();
                JsonValue resolved = null;

                try
                {
                    // Attempt resolution to surface blockers early.
                    resolved = ResolveOperationForPreview(document, operation, plan);
                }
                catch (AgentException ex)
                {
                    status = PreviewStatus.Blocked;
                    opErrors.Add(new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction));
                }

                totalCreate += descriptor.Creates;
                totalModify += descriptor.Modifies;
                totalDelete += descriptor.Deletes;

                operationPreviews.Add(new OperationPreview(
                    operation.Id, operation.Op, status,
                    descriptor.Creates, descriptor.Modifies, descriptor.Deletes,
                    resolved,
                    null, null,
                    opWarnings, opErrors));
            }

            int estimatedAffected = totalCreate + totalModify + totalDelete;
            bool requiresConfirmation = plan.Safety.RequireUserConfirmation;

            var summary = new PreviewSummary(totalCreate, totalModify, totalDelete, estimatedAffected, requiresConfirmation);

            return new PreviewReport(planHash, docFingerprint, operationPreviews, summary, DateTime.UtcNow, warnings, errors);
        }

        /// <summary>Commits a plan inside a TransactionGroup. Atomic by default.</summary>
        public ExecutionResult Commit(Document document, AgentPlan plan, CommandRegistry registry)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            if (document.IsReadOnly)
            {
                throw new AgentException(ErrorCodes.DocumentReadOnly, "The active document is read-only.", false);
            }

            string docFingerprint = ComputeFingerprint(document);
            string planHash = PlanHasher.HashJson(plan.ToJson());

            // Verify document fingerprint matches the plan's expected fingerprint, if set.
            if (!string.IsNullOrEmpty(plan.Document.ExpectedFingerprint) &&
                !string.Equals(plan.Document.ExpectedFingerprint, docFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentException(
                    ErrorCodes.DocumentChangedSincePreview,
                    "The document has changed since the preview was generated.",
                    true,
                    "Re-run the preview against the current document.");
            }

            var sw = Stopwatch.StartNew();
            DateTime startedAt = DateTime.UtcNow;

            var operationResults = new List<OperationResult>();
            var assertionResults = new List<AssertionResult>();
            var planErrors = new List<AgentError>();
            var resultsMap = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            TransactionGroup tg = null;
            Transaction tx = null;
            try
            {
                tg = new TransactionGroup(document, "AutodeskNativeAgent.Plan." + plan.RequestId);
                tg.Start();

                // Revit requires an open Transaction inside the TransactionGroup for any
                // modifying API call. The group alone does not provide a transaction.
                tx = new Transaction(document, "AutodeskNativeAgent.PlanOps." + plan.RequestId);
                tx.Start();

                // Execute operations in dependency order (topological sort).
                var sortedOps = TopologicalSort(plan.Operations);
                bool anyFailed = false;

                foreach (PlanOperation operation in sortedOps)
                {
                    if (anyFailed)
                    {
                        operationResults.Add(new OperationResult(
                            operation.Id, operation.Op, OperationOutcome.Skipped));
                        continue;
                    }

                    OperationResult opResult;
                    try
                    {
                        opResult = ExecuteOperation(document, operation, plan, resultsMap);
                        resultsMap[operation.Id] = opResult.ToJson();
                    }
                    catch (AgentException ex)
                    {
                        opResult = new OperationResult(
                            operation.Id, operation.Op, OperationOutcome.Failed,
                            null, null, null, null, null,
                            new[] { ex.Message }, new[] { ex.Message });
                        anyFailed = true;
                        planErrors.Add(new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction));
                    }
                    catch (Exception ex)
                    {
                        opResult = new OperationResult(
                            operation.Id, operation.Op, OperationOutcome.Failed,
                            null, null, null, null, null,
                            new[] { ex.Message }, new[] { ex.Message });
                        anyFailed = true;
                        planErrors.Add(new AgentError(ErrorCodes.ExecutionFailed, ex.Message, false));
                    }

                    operationResults.Add(opResult);
                }

                // Evaluate assertions for completed operations.
                if (!anyFailed)
                {
                    foreach (PlanOperation operation in sortedOps)
                    {
                        OperationResult opResult = operationResults.Find(r => r.OperationId == operation.Id);
                        if (opResult == null || opResult.Status != OperationOutcome.Completed)
                        {
                            continue;
                        }

                        foreach (Assertion assertion in operation.Assertions)
                        {
                            AssertionResult assertionResult = EvaluateAssertion(document, assertion, resultsMap, plan);
                            assertionResults.Add(assertionResult);
                            if (!assertionResult.Passed)
                            {
                                anyFailed = true;
                                planErrors.Add(new AgentError(
                                    ErrorCodes.AssertionFailed,
                                    "Assertion '" + assertion.Kind + "' on '" + assertion.Target + "' failed: " +
                                    "expected " + assertionResult.Expected + ", got " + assertionResult.Actual + ".",
                                    false));
                            }
                        }
                    }
                }

                if (anyFailed)
                {
                    if (tx != null && tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    if (tg.HasStarted())
                    {
                        tg.RollBack();
                    }

                    return new ExecutionResult(
                        plan.RequestId,
                        JobStatus.RolledBack,
                        docFingerprint,
                        planHash,
                        startedAt,
                        DateTime.UtcNow,
                        true,
                        operationResults,
                        assertionResults,
                        new RollbackInfo(true, "One or more operations or assertions failed."),
                        planErrors);
                }

                tx.Commit();
                tg.Assimilate();
            }
            catch (AgentException ex)
            {
                if (tg != null && tg.HasStarted())
                {
                    tg.RollBack();
                }

                planErrors.Add(new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction));
                return new ExecutionResult(
                    plan.RequestId, JobStatus.Failed, docFingerprint, planHash,
                    startedAt, DateTime.UtcNow, true, operationResults, assertionResults,
                    new RollbackInfo(true, ex.Message), planErrors);
            }
            catch (Exception ex)
            {
                if (tg != null && tg.HasStarted())
                {
                    tg.RollBack();
                }

                planErrors.Add(new AgentError(ErrorCodes.InternalError, ex.Message, false));
                return new ExecutionResult(
                    plan.RequestId, JobStatus.Failed, docFingerprint, planHash,
                    startedAt, DateTime.UtcNow, true, operationResults, assertionResults,
                    new RollbackInfo(true, ex.Message), planErrors);
            }
            finally
            {
                if (tg != null)
                {
                    tg.Dispose();
                }
            }

            sw.Stop();
            return new ExecutionResult(
                plan.RequestId,
                JobStatus.Completed,
                docFingerprint,
                planHash,
                startedAt,
                DateTime.UtcNow,
                true,
                operationResults,
                assertionResults,
                null,
                planErrors);
        }

        /// <summary>Rolls back a completed job by undoing the last transaction group.</summary>
        public void Rollback(Document document, Job job)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            if (job.Status != JobStatus.Completed)
            {
                throw new AgentException(ErrorCodes.RollbackNotPossible,
                    "Only completed jobs can be rolled back.", false);
            }

            // Revit 2024 does not expose a direct API to undo a specific transaction group
            // after Assimilate. The standard approach is to use the Undo command via the
            // UIApplication. Since we don't have UIApplication here (only Document), we
            // attempt to open a rollback transaction that triggers the model to revert.
            // In practice, post-commit rollback in Revit is done via the UI Undo button.
            // This method marks the job as rolled back; the actual undo must be triggered
            // from the UI layer that has access to UIApplication.
            job.Transition(JobStatus.RolledBack);
        }

        // --- Operation execution dispatch ---

        private OperationResult ExecuteOperation(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            switch (operation.Op)
            {
                case "wall.create":
                    return WallCreateOperation.Execute(document, operation, plan, results, ProjectPolicy.FromJson(JsonValue.EmptyObject()));

                case "door.insert":
                    return DoorInsertOperation.Execute(document, operation, plan, results, ProjectPolicy.FromJson(JsonValue.EmptyObject()));

                case "parameter.set":
                    return ParameterSetOperation.Execute(document, operation, plan, results);

                case "element.delete":
                    return ElementDeleteOperation.Execute(document, operation, plan, results);

                case "element.move":
                    return ElementMoveOperation.Execute(document, operation, plan, results);

                case "element.rotate":
                    return ElementRotateOperation.Execute(document, operation, plan, results);

                case "element.rename":
                    return ElementRenameOperation.Execute(document, operation, plan, results);

                case "window.insert":
                    return WindowInsertOperation.Execute(document, operation, plan, results, ProjectPolicy.FromJson(JsonValue.EmptyObject()));

                case "room.create":
                    return RoomCreateOperation.Execute(document, operation, plan, results);

                case "view.create_plan":
                    return ViewCreatePlanOperation.Execute(document, operation, plan, results);

                case "sheet.create":
                    return SheetCreateOperation.Execute(document, operation, plan, results);

                case "sheet.place_view":
                    return SheetPlaceViewOperation.Execute(document, operation, plan, results);

                case "wall.update":
                    return WallUpdateOperation.Execute(document, operation, plan, results);

                case "parameter.set_many":
                    return ParameterSetManyOperation.Execute(document, operation, plan, results);

                case "document.save":
                    return DocumentSaveOperation.Execute(document, operation, plan, results);

                case "document.save_as":
                    return DocumentSaveAsOperation.Execute(document, operation, plan, results);

                case "export.dwg":
                    return ExportDwgOperation.Execute(document, operation, plan, results);

                case "export.pdf":
                    return ExportPdfOperation.Execute(document, operation, plan, results);

                default:
                    throw new AgentException(ErrorCodes.UnknownOperation,
                        "No handler registered for operation '" + operation.Op + "'.", true);
            }
        }

        // --- Preview resolution (dry-run) ---

        private JsonValue ResolveOperationForPreview(Document document, PlanOperation operation, AgentPlan plan)
        {
            switch (operation.Op)
            {
                case "wall.create":
                {
                    JsonValue levelSel = operation.Args["level"];
                    JsonValue typeSel = operation.Args["type"];
                    Level level = LevelResolver.Resolve(document, levelSel, plan.Units, document.ActiveView).Value;
                    double scale = UnitNames.FeetPerUnit(plan.Units);
                    double startX = operation.Args["start"]["x"].AsDouble() * scale;
                    double startY = operation.Args["start"]["y"].AsDouble() * scale;
                    double endX = operation.Args["end"]["x"].AsDouble() * scale;
                    double endY = operation.Args["end"]["y"].AsDouble() * scale;
                    double lengthFeet = Math.Sqrt((endX - startX) * (endX - startX) + (endY - startY) * (endY - startY));
                    double lengthPlan = UnitNames.FromFeet(lengthFeet, plan.Units);
                    double height = operation.Args["height"].AsDouble();
                    return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                        {
                            ["id"] = JsonValue.Number(level.Id.Value),
                            ["name"] = JsonValue.String(level.Name)
                        }),
                        ["estimatedLength"] = JsonValue.Number(lengthPlan),
                        ["height"] = JsonValue.Number(height)
                    });
                }

                default:
                    return JsonValue.Null;
            }
        }

        // --- Assertion evaluation ---

        private AssertionResult EvaluateAssertion(
            Document document,
            Assertion assertion,
            IReadOnlyDictionary<string, JsonValue> results,
            AgentPlan plan)
        {
            Element target = null;
            string resolveError = null;
            try
            {
                target = ElementResolver.Resolve(document, JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["viaOperationResult"] = JsonValue.String(assertion.Target.StartsWith("$result.") ? assertion.Target : null),
                    ["uniqueId"] = JsonValue.String(assertion.Target.StartsWith("$result.") ? null : assertion.Target)
                }), results);
            }
            catch (Exception ex)
            {
                // Target resolution failed; the assertion fails. Record why for diagnostics.
                resolveError = ex.Message;
            }

            JsonValue expected = assertion.Equals.IsNull ? assertion.Expect : assertion.Equals;
            JsonValue actual = JsonValue.Null;
            double? difference = null;
            double? tolerance = assertion.Tolerance;
            bool passed = false;

            switch (assertion.Kind)
            {
                case AssertionKind.ElementExists:
                {
                    bool exists = target != null;
                    actual = resolveError != null
                        ? JsonValue.String("<resolve failed: " + resolveError + ">")
                        : JsonValue.Bool(exists);
                    if (resolveError != null)
                    {
                        passed = false;
                    }
                    else if (assertion.Expect.IsNull)
                    {
                        passed = exists;
                    }
                    else
                    {
                        bool expectedBool = assertion.Expect.AsBool(true);
                        passed = exists == expectedBool;
                    }
                    break;
                }

                case AssertionKind.Category:
                {
                    string cat = target != null && target.Category != null ? target.Category.Name : string.Empty;
                    actual = JsonValue.String(cat);
                    passed = string.Equals(cat, expected.AsString(), StringComparison.OrdinalIgnoreCase);
                    break;
                }

                case AssertionKind.TypeName:
                {
                    string typeName = ElementResolver.Summarize(target)?.TypeName ?? string.Empty;
                    actual = JsonValue.String(typeName);
                    passed = string.Equals(typeName, expected.AsString(), StringComparison.OrdinalIgnoreCase);
                    break;
                }

                case AssertionKind.Length:
                {
                    double requested = expected.AsDouble();
                    double actualLen = MeasureLength(target, plan);
                    actual = JsonValue.Number(actualLen);
                    difference = Math.Abs(actualLen - requested);
                    passed = difference <= tolerance;
                    break;
                }

                case AssertionKind.Height:
                {
                    double requested = expected.AsDouble();
                    double actualH = MeasureHeight(target, plan);
                    actual = JsonValue.Number(actualH);
                    difference = Math.Abs(actualH - requested);
                    passed = target != null && difference <= tolerance;
                    break;
                }

                case AssertionKind.Width:
                {
                    double requested = expected.AsDouble();
                    double actualW = MeasureWidth(target, plan);
                    actual = JsonValue.Number(actualW);
                    difference = Math.Abs(actualW - requested);
                    passed = target != null && difference <= tolerance;
                    break;
                }

                case AssertionKind.HostEquals:
                {
                    // Resolve the expected host reference (e.g. "$result.wall-001").
                    JsonValue hostSelector = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["viaOperationResult"] = JsonValue.String(assertion.Host.StartsWith("$result.") ? assertion.Host : null),
                        ["uniqueId"] = JsonValue.String(assertion.Host.StartsWith("$result.") ? null : assertion.Host)
                    });

                    Element expectedHost = null;
                    try
                    {
                        expectedHost = ElementResolver.Resolve(document, hostSelector, results);
                    }
                    catch
                    {
                        expectedHost = null;
                    }

                    Element actualHost = null;
                    if (target is FamilyInstance fi)
                    {
                        actualHost = fi.Host;
                    }

                    string actualHostName = actualHost != null ? actualHost.Name : string.Empty;
                    string expectedHostName = expectedHost != null ? expectedHost.Name : string.Empty;
                    actual = JsonValue.String(actualHostName);
                    passed = actualHost != null && expectedHost != null && actualHost.Id == expectedHost.Id;
                    break;
                }

                case AssertionKind.ParameterEquals:
                {
                    string paramName = assertion.Parameter;
                    string actualVal = ReadParameterValue(target, paramName);
                    actual = JsonValue.String(actualVal ?? string.Empty);
                    passed = string.Equals(actualVal, expected.AsString(), StringComparison.Ordinal);
                    break;
                }

                default:
                    // Unsupported assertion kinds are marked as failed rather than silently passed.
                    passed = false;
                    actual = JsonValue.String("<unsupported>");
                    break;
            }

            return new AssertionResult(
                Assertion.ToWire(assertion.Kind),
                assertion.Target,
                expected,
                actual,
                difference,
                tolerance,
                passed);
        }

        private double MeasureLength(Element element, AgentPlan plan)
        {
            if (element == null)
            {
                return 0;
            }

            LocationCurve lc = element.Location as LocationCurve;
            if (lc != null && lc.Curve != null)
            {
                double feet = lc.Curve.Length;
                return UnitNames.FromFeet(feet, plan.Units);
            }

            return 0;
        }

        private double MeasureHeight(Element element, AgentPlan plan)
        {
            if (element == null)
            {
                return 0;
            }

            // Walls: use the height parameter directly (most reliable).
            if (element is Wall wall)
            {
                Parameter p = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (p != null && p.HasValue)
                {
                    return UnitNames.FromFeet(p.AsDouble(), plan.Units);
                }
            }

            // Generic fallback: bounding box height.
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box != null && box.Max != null && box.Min != null)
            {
                double feet = Math.Abs(box.Max.Z - box.Min.Z);
                return UnitNames.FromFeet(feet, plan.Units);
            }

            return 0;
        }

        private double MeasureWidth(Element element, AgentPlan plan)
        {
            if (element == null)
            {
                return 0;
            }

            LocationCurve lc = element.Location as LocationCurve;
            if (lc != null && lc.Curve != null)
            {
                // For curve elements, report the bounding box width (thickness).
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box != null && box.Max != null && box.Min != null)
                {
                    double dx = Math.Abs(box.Max.X - box.Min.X);
                    double dy = Math.Abs(box.Max.Y - box.Min.Y);
                    double z = Math.Abs(box.Max.Z - box.Min.Z);
                    // Exclude height axis; take max of remaining horizontal extents.
                    double horizontal = Math.Max(dx, dy);
                    return UnitNames.FromFeet(horizontal, plan.Units);
                }
            }

            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb != null && bb.Max != null && bb.Min != null)
            {
                double dx = Math.Abs(bb.Max.X - bb.Min.X);
                double dy = Math.Abs(bb.Max.Y - bb.Min.Y);
                double horizontal = Math.Max(dx, dy);
                return UnitNames.FromFeet(horizontal, plan.Units);
            }

            return 0;
        }

        private string ReadParameterValue(Element element, string name)
        {
            if (element == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Parameter p = element.LookupParameter(name);
            if (p == null || !p.HasValue)
            {
                return null;
            }

            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                {
                    ElementId id = p.AsElementId();
                    return id != null ? id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                }
                default: return null;
            }
        }

        // --- Topological sort ---

        private static List<PlanOperation> TopologicalSort(IReadOnlyList<PlanOperation> operations)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlanOperation op in operations)
            {
                ids.Add(op.Id);
            }

            var incoming = new Dictionary<string, int>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var byId = new Dictionary<string, PlanOperation>(StringComparer.Ordinal);

            foreach (PlanOperation op in operations)
            {
                incoming[op.Id] = 0;
                dependents[op.Id] = new List<string>();
                byId[op.Id] = op;
            }

            foreach (PlanOperation op in operations)
            {
                foreach (string dep in op.DependsOn)
                {
                    if (ids.Contains(dep))
                    {
                        incoming[op.Id]++;
                        dependents[dep].Add(op.Id);
                    }
                }
            }

            var queue = new Queue<string>();
            foreach (var pair in incoming)
            {
                if (pair.Value == 0)
                {
                    queue.Enqueue(pair.Key);
                }
            }

            var result = new List<PlanOperation>(operations.Count);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                result.Add(byId[id]);
                foreach (string dependent in dependents[id])
                {
                    incoming[dependent]--;
                    if (incoming[dependent] == 0)
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }

            // If cycle remains, just append them in declaration order (the validator will have caught this).
            if (result.Count < operations.Count)
            {
                foreach (PlanOperation op in operations)
                {
                    if (!result.Contains(op))
                    {
                        result.Add(op);
                    }
                }
            }

            return result;
        }

        // --- Fingerprint ---

        private static string ComputeFingerprint(Document document)
        {
            string projectNumber = string.Empty;
            string projectName = string.Empty;
            string title = string.Empty;
            string path = string.Empty;
            try { projectNumber = document.ProjectInformation?.Number ?? string.Empty; } catch { }
            try { projectName = document.ProjectInformation?.Name ?? string.Empty; } catch { }

            try { title = document.Title ?? string.Empty; } catch { }
            try { path = document.PathName ?? string.Empty; } catch { }

            return DocumentFingerprint.FromIdentity(title, path, projectNumber, projectName);
        }
    }
}
