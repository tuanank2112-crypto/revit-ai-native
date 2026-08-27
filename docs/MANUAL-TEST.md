# Manual Revit 2024 Test Checklist

Run these tests in order after installing the add-in and starting the MCP server.

## Setup
1. Build the solution: `.\scripts\build-all.ps1`
2. Install the add-in: `.\scripts\install-revit2024.ps1`
3. Open Revit 2024 and create/open a project
4. Start the MCP server: `.\scripts\start-mcp.ps1`
5. Connect Antigravity (or any MCP client) to the server

---

## TEST 1: MCP Connection & Status
- **Call**: `revit_get_status`
- **Expected**: `connected: true`, `activeDocumentTitle` matches the open document
- **Pass criteria**: Response arrives within 5 seconds with correct document title

## TEST 2: Inspect Document
- **Call**: `revit_inspect_document`
- **Expected**: Title, path, project number, fingerprint, read-only/workshared flags
- **Pass criteria**: All fields present; fingerprint is non-empty

## TEST 3: Create Wall (5000 × 3000 mm)
- **Call**: `revit_preview_plan` with `samples/plans/sample-wall-door.json` (preview only)
- **Expected**: Preview report shows `willCreate: 2`, resolved level and type
- **Then call**: `revit_commit_plan` with the same plan
- **Expected**: `status: "completed"`, wall created with assertions passing
- **Verify in Revit**: Wall length = 4999–5001 mm, height = 2999–3001 mm
- **Pass criteria**: All assertions pass; wall visible in the model

## TEST 4: Insert Door (900 × 2200 mm @ 1200 mm)
- Included in TEST 3 plan (door-001 operation depends on wall-001)
- **Expected**: Door placed on the wall at 1200 mm from start
- **Verify in Revit**: Door is hosted by the wall, correct family/type loaded
- **Pass criteria**: `host_equals` assertion passes

## TEST 5: Move Element 1000 mm
- Call `revit_commit_plan` with `samples/plans/sample-move-element.json` (replace uniqueId with the wall's uniqueId)
- Expected: Wall position changes by 1000 mm in X
- Verify: Element moved; no errors; assertion-less (resolution only)

## TEST 6: Set Parameter
- Select an element
- **Call**: `revit_commit_plan` with `samples/plans/sample-parameter-set.json` (replace uniqueId)
- **Expected**: Parameter value updated
- **Verify**: Read parameter back; value matches

## TEST 7: Delete Without Confirmation → Fail
- **Call**: `revit_commit_plan` with a plan containing `element.delete` and `requireUserConfirmation: true`
- Do NOT confirm
- **Expected**: `CONFIRMATION_REQUIRED` error or `awaiting_confirmation` status
- **Pass criteria**: No element deleted

## TEST 8: Rollback via Intentionally Failed Assertion
- **Call**: `revit_commit_plan` with `samples/plans/sample-rollback-fail.json`
- **Expected**: Wall created, then assertion fails (length != 9999), rollback occurs
- **Pass criteria**: `status: "rolled_back"`, no wall in model

## TEST 9: Document Changed Since Preview
1. Call `revit_preview_plan` with the wall+door plan against Document A
2. Switch to a different document (Document B) in Revit
3. Call `revit_commit_plan` with the same plan
- **Expected**: `DOCUMENT_CHANGED_SINCE_PREVIEW` error
- **Pass criteria**: No changes made to Document B

## TEST 10: PDF Export
- **Call**: `revit_commit_plan` with `samples/plans/sample-export-pdf.json` (replace view uniqueId)
- **Expected**: PDF file created in the output directory
- **Verify**: File exists and is a valid PDF

## TEST 11: DWG Export
- Same as TEST 10 but with an `export.dwg` operation
- **Expected**: DWG file created
- **Verify**: File exists and opens in a DWG viewer

---

## Cleanup
- Run `.\scripts\uninstall-revit2024.ps1` to remove the add-in
- Audit logs are at: `%LOCALAPPDATA%\AutodeskNativeAgent\logs`
