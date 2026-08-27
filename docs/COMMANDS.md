# Commands Reference

## MCP Tools

### revit_get_status
Returns connection status, active document title/path, and busy state.

**Input**: `{}`

**Response**:
```json
{
  "connected": true,
  "busy": false,
  "activeDocumentTitle": "MyProject.rvt",
  "activeDocumentPath": "...",
  "queueDepth": 0
}
```

### revit_get_capabilities
Returns protocol version, supported methods, and registered operations.

**Input**: `{}`

**Response**:
```json
{
  "protocolVersion": "1.0",
  "methods": [...],
  "operations": [...]
}
```

### revit_inspect_document
Returns identity of the active document.

**Response**:
```json
{
  "title": "MyProject.rvt",
  "path": "...",
  "projectNumber": "...",
  "projectName": "...",
  "isReadOnly": false,
  "isWorkshared": false,
  "fingerprint": "sha256:..."
}
```

### revit_inspect_selection
Returns the current Revit selection as element summaries.

### revit_query_elements
Runs a structured element query.

**Input**:
```json
{
  "categories": ["Walls"],
  "where": {
    "all": [
      { "field": "name", "operator": "contains", "value": "Interior" }
    ]
  },
  "limit": 50
}
```

**Operators**: `equals`, `not_equals`, `contains`, `starts_with`, `ends_with`, `greater_than`, `greater_or_equal`, `less_than`, `less_or_equal`, `is_empty`, `is_not_empty`, `in`

**Field paths**: `name`, `category`, `type.name`, `level.name`, `parameter.<paramName>`

### revit_validate_plan
Validates a plan structurally. Does not execute.

### revit_preview_plan
Dry-runs a plan. Returns resolved types/levels, estimated affected elements, warnings.

### revit_commit_plan
Executes a plan inside a transaction group. Returns a job id.

### revit_get_job_status
Returns the current job status and latest execution result.

### revit_cancel_job
Cancels a queued or running job (if in a cancellable state).

### revit_rollback_job
Rolls back a completed job.

### revit_get_audit_log
Returns sanitized audit log entries.

## Write Operations

### wall.create
Creates a straight wall.

**Args**:
```json
{
  "start": { "x": 0, "y": 0, "z": 0 },
  "end": { "x": 5000, "y": 0, "z": 0 },
  "height": 3000,
  "level": { "strategy": "active_view_level" },
  "type": { "strategy": "project_default_or_fail" },
  "locationLine": "wall_centerline"
}
```

**Level strategies**: `active_view_level`, `explicit_unique_id`, `by_name`, `first`

**Type strategies**: `explicit_unique_id`, `exact_name`, `project_policy`, `project_default_or_fail`, `preferred_or_dimensions`, `most_used_in_project`

### door.insert
Inserts a door hosted by a wall.

### parameter.set
Sets a typed parameter on an element.

### element.delete
Deletes an element. Requires confirmation.

### element.move
Translates an element by a vector.

### element.rotate
Rotates an element around an axis.

## Error Codes

| Code | Description |
|------|-------------|
| `REVIT_NOT_CONNECTED` | Add-in not reachable |
| `REQUEST_TIMEOUT` | Request timed out |
| `NO_ACTIVE_DOCUMENT` | No document open |
| `DOCUMENT_READ_ONLY` | Document is read-only |
| `DOCUMENT_CHANGED_SINCE_PREVIEW` | Fingerprint mismatch |
| `SCHEMA_VALIDATION_FAILED` | Plan structure invalid |
| `UNKNOWN_OPERATION` | Operation not in registry |
| `TYPE_NOT_FOUND` | No type matched |
| `AMBIGUOUS_TYPE` | Multiple types matched |
| `LEVEL_NOT_FOUND` | No level matched |
| `CONFIRMATION_REQUIRED` | Token needed |
| `CONFIRMATION_TOKEN_EXPIRED` | Token expired |
| `EXECUTION_FAILED` | Operation failed |
| `ASSERTION_FAILED` | Post-execution check failed |
| `ROLLBACK_NOT_POSSIBLE` | Job not rollbackable |
| `PATH_NOT_ALLOWED` | Path security violation |
| `FILE_ALREADY_EXISTS` | Overwrite not allowed |
