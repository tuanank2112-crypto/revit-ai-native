# Security Model

## Named Pipe Security

- **Local only**: The pipe is created on `\\.\pipe\autodesk-native-agent-<username>`
- **Current user scope**: Pipe name includes the sanitized Windows username to prevent cross-user collision
- **Message size limit**: 1 MiB max per frame (`MaxMessageBytes`)
- **Timeout**: Default 30s request timeout, 15s heartbeat
- **Protocol version**: Enforced at handshake; mismatches are rejected
- **Request correlation**: Every response carries the original `requestId`
- **No secret logging**: Audit entries are sanitized (paths redacted, truncated to 2000 chars)

## Path Security

### Export Paths
- `SecurityValidator.ValidateExportPath()` canonicalizes the path using `Path.GetFullPath()`
- Checks against configured export root directories
- Rejects:
  - Path traversal (`..` segments)
  - Device paths (`\\?\`, `CON`, `PRN`, `NUL`, `COM1-9`, `LPT1-9`)
  - UNC paths outside allowed roots

### Save-As Paths
- `SecurityValidator.ValidateSaveAsPath()` canonicalizes and validates
- Enforces `.rvt` extension
- Checks destination directory exists
- Overwrite policy: `FILE_ALREADY_EXISTS` if file exists and `overwrite=false`

## Confirmation Tokens

- **Single-use**: Each token can only be accepted once
- **Plan-hash-bound**: Token carries the `planHash`; committing a different plan with the same token is refused
- **Time-limited**: Default 5-minute expiry
- **States**: `pending → accepted → used` or `pending → rejected` / `pending → expired`
- **Error codes**:
  - `CONFIRMATION_TOKEN_INVALID` — value doesn't match
  - `CONFIRMATION_TOKEN_EXPIRED` — past expiry time
  - `CONFIRMATION_TOKEN_ALREADY_USED` — already consumed

## Document Safety

Before commit, `PlanExecutor` verifies:
- Document is not read-only (`DOCUMENT_READ_ONLY`)
- Document fingerprint matches preview (`DOCUMENT_CHANGED_SINCE_PREVIEW`)
- Document exists (`NO_ACTIVE_DOCUMENT`)

## Execution Safety

- **Atomic execution**: Plans run inside a `TransactionGroup` with `IsFailureConsistent = true`
- **Rollback on failure**: Any operation failure or assertion failure rolls back the entire group
- **Maximum affected elements**: Plans declare `safety.maximumElementsAffected`; the validator enforces it
- **No arbitrary code execution**: No `eval`, no dynamic DLL loading, no reflection-based dispatch
- **Operation allowlist**: Only `CommandRegistry`-registered operations can run

## Audit Log

- **Location**: `%LOCALAPPDATA%\AutodeskNativeAgent\logs\audit.jsonl`
- **Format**: JSON Lines, one entry per line
- **Sanitization**: Paths redacted, values truncated to 2000 chars
- **Content**: Timestamp, actor, action, severity, message, correlationId, planHash, jobId, metadata
- **No secrets**: Token values are never included in audit entries
