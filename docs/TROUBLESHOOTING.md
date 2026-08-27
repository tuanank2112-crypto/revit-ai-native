# Troubleshooting

## Common Issues

### REVIT_NOT_CONNECTED
**Cause**: The MCP server cannot reach the Revit add-in.
**Fix**:
1. Ensure Revit 2024 is running
2. Check the add-in is loaded (look for "AutodeskNativeAgent" in Add-Ins → External Tools)
3. Verify the pipe name matches: `\\.\pipe\autodesk-native-agent-<your-username>`
4. Restart the MCP server

### DOCUMENT_CHANGED_SINCE_PREVIEW
**Cause**: The document fingerprint changed between preview and commit.
**Fix**: Re-run the preview against the current document, then commit with the fresh token.

### CONFIRMATION_REQUIRED
**Cause**: The plan's safety envelope requires user confirmation, but no valid token was supplied.
**Fix**: Call `revit_preview_plan` first. The preview returns a confirmation token. Pass it in the commit request.

### CONFIRMATION_TOKEN_EXPIRED
**Cause**: More than 5 minutes passed between preview and commit.
**Fix**: Re-run the preview to get a fresh token.

### TYPE_NOT_FOUND
**Cause**: No family/type matched the selector.
**Fix**: Check the type strategy and family/type names. Use `revit_get_capabilities` to see registered operations.

### AMBIGUOUS_TYPE
**Cause**: Multiple types matched the selector equally.
**Fix**: Provide a more specific selector (exact family + exact type name).

### Build Errors

#### RevitAPI.dll not found
**Fix**: Set `REVIT_2024_PATH` environment variable to your Revit 2024 install directory.

#### MSBuild "opening NUL for ACL write"
**Cause**: Terminal/MSBuild permissions issue.
**Fix**: Run the terminal as Administrator, or use a different terminal (e.g., Developer Command Prompt).

#### npm install fails
**Fix**: Delete `node_modules` and `package-lock.json`, then retry.

### Pipe Connection Issues

#### "Cannot connect to pipe"
1. Verify Revit is open with a document
2. Check the add-in started (look in the Revit journal or output window)
3. The pipe name is user-scoped — ensure the MCP server uses the same username

#### Heartbeat timeout
1. Revit may be showing a modal dialog — close it
2. The add-in may be busy with a long operation — wait
3. Check `revit_get_status` to see `busy` state

### Audit Log Location
Audit logs are at:
```
%LOCALAPPDATA%\AutodeskNativeAgent\logs\
```

### Crash Log
If the add-in fails to start, check:
```
%LOCALAPPDATA%\AutodeskNativeAgent\addin-crash.log
```
