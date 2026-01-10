# Agentic Debugger VSIX - Build and Runtime Fixes

**Date:** 2026-01-10
**Issue:** Extension would not build in Release mode and HTTP bridge was non-responsive after installation
**Status:** ✅ RESOLVED

---

## Problem Summary

### Initial Issue
User reported: `Extension 'AgenticDebuggerVsix2.0f7df0bd-01a5-4c82-8b2c-319caea4b642' could not be found. Please make sure the extension has been installed.`

**Key Detail:** Error only occurred in **Release** configuration, **Debug** worked fine.

### Root Causes Identified

1. **Build Error (Release mode only):** Missing `<DeployExtension>false</DeployExtension>` property
2. **Runtime Deadlock:** HTTP requests hung forever due to UI thread blocking
3. **Permission Check Failures:** Background thread exceptions when checking permissions

---

## Detailed Analysis

### Issue 1: Release Build Failure

**Problem:**
```xml
<!-- Debug configuration (worked) -->
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
  <DeployExtension>false</DeployExtension>  ✅ Present
</PropertyGroup>

<!-- Release configuration (failed) -->
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <!-- Missing DeployExtension property -->  ❌ Missing
</PropertyGroup>
```

**Why This Failed:**
When `DeployExtension` is not explicitly set to `false`, Visual Studio attempts to deploy/verify the extension during build. It looks for an installed extension with GUID `0f7df0bd-01a5-4c82-8b2c-319caea4b642` (from the VSIX manifest), but since we're building it (not installing it), the validation fails.

**Fix:**
```xml
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <DeployExtension>false</DeployExtension>  ✅ Added
</PropertyGroup>
```

---

### Issue 2: Assembly Reference Problems

**Problem:**
```xml
<PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.0.32112.339"
                  ExcludeAssets="runtime" />  ❌ Blocked assemblies
```

The `ExcludeAssets="runtime"` prevented VS SDK assemblies from being included, causing hundreds of compilation errors like:
- `CS0246: The type or namespace name 'EnvDTE' could not be found`
- `CS0246: The type or namespace name 'AsyncPackage' could not be found`

**Why This Exists:**
Old-style .NET Framework VSIX projects sometimes exclude runtime assets to avoid deployment conflicts, but this breaks command-line builds.

**Fix:**
1. Removed `ExcludeAssets="runtime"`
2. Updated SDK version: `17.0.32112.339` → `17.11.40252`
3. Added explicit assembly references for VS SDK components
4. Changed to `IncludeAssets="compile;build"` for better control

---

### Issue 3: Duplicate Package Classes

**Problem:**
Project had two Package classes with different GUIDs:
- `AgenticDebuggerPackage.cs` (namespace: `AgenticDebuggerVsix`) - Real implementation
- `AgenticDebuggerVsix2Package.cs` (namespace: `AgenticDebuggerVsix2`) - Empty stub/template

**Fix:**
Deleted `AgenticDebuggerVsix2Package.cs` stub file and removed from `.csproj`.

---

### Issue 4: HTTP Bridge Deadlock (Critical Runtime Bug)

**Problem:**
After successful installation, HTTP server started but **all requests hung indefinitely**:

```bash
curl http://localhost:27183/status
# Connected successfully but no response (timeout after 30+ seconds)
```

**Root Cause:**
`/status` endpoint attempted to synchronously block a background thread waiting for Visual Studio's UI thread:

```csharp
// BROKEN CODE (in HttpBridge.cs line 481)
if (method == "GET" && path == "/status")
{
    var mode = "Unknown";
    try
    {
        ThreadHelper.JoinableTaskFactory.Run(async () => {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var debugMode = _dte.Debugger.CurrentMode;
            // ...
        });
    }
    catch { }
    // ...
}
```

**Why This Deadlocks:**
1. HTTP request comes in on background thread
2. Code calls `JoinableTaskFactory.Run()` which **synchronously blocks** waiting for UI thread
3. If UI thread is busy (or in some threading states), the wait never completes
4. HTTP request hangs forever
5. Client times out

**Fix:**
Use cached snapshot instead of querying UI thread:

```csharp
// FIXED CODE
if (method == "GET" && path == "/status")
{
    // Use cached snapshot mode instead of blocking on UI thread
    var mode = _lastSnapshot?.Mode ?? "Unknown";
    // ...
}
```

**Evidence:**
```
* Connected to localhost (::1) port 27183  ✅ Connection established
* Request completely sent off               ✅ Request sent
<30+ seconds of waiting>                    ❌ No response
* Operation timed out                       ❌ Timeout
```

---

### Issue 5: Permission Check Background Thread Exception

**Problem:**
Even after fixing Issue 4, requests still failed because permission checking threw exceptions.

**Root Cause:**
Every HTTP request called:

```csharp
GetCurrentPermissions()
  → _agenticPackage.GetPermissions()
    → ThreadHelper.ThrowIfNotOnUIThread();  ❌ THROWS on background thread!
```

**Call Stack:**
1. HTTP handler (background thread) receives request
2. `HandleRequest()` calls `IsPermissionGranted()`
3. `IsPermissionGranted()` calls `GetCurrentPermissions()`
4. `GetCurrentPermissions()` calls `_agenticPackage.GetPermissions()`
5. `GetPermissions()` calls `ThreadHelper.ThrowIfNotOnUIThread()`
6. **Exception thrown** because we're on background thread
7. Permission check fails, request rejected

**Fix:**
Implement permission caching with periodic refresh from UI thread:

```csharp
// Cache permissions (updated from UI thread periodically)
private PermissionsModel _permissions;

private PermissionsModel GetCurrentPermissions()
{
    // Always use cached permissions - safe from any thread
    return _permissions;
}

private void RefreshPermissionsCache()
{
    ThreadHelper.ThrowIfNotOnUIThread();  // Only called from UI thread
    if (_agenticPackage != null)
    {
        try
        {
            _permissions = _agenticPackage.GetPermissions();
        }
        catch
        {
            // Keep existing cached permissions on error
        }
    }
}
```

**Refresh Strategy:**
- Load permissions on startup (line 123)
- Refresh every 5 seconds from registry cleanup loop (line 302)
- Changes in Tools→Options take effect within 5 seconds

---

## Files Modified

### AgenticDebuggerVsix2.csproj
- ✅ Added `<DeployExtension>false</DeployExtension>` to Release configuration
- ✅ Removed `ExcludeAssets="runtime"` from VS SDK package
- ✅ Updated VS SDK version to 17.11.40252
- ✅ Added explicit VS SDK assembly references
- ✅ Removed duplicate `AgenticDebuggerVsix2Package.cs` from compilation

### HttpBridge.cs
- ✅ Fixed `/status` endpoint UI thread deadlock (line 479)
- ✅ Implemented permission caching (lines 72-95)
- ✅ Added `RefreshPermissionsCache()` method
- ✅ Call `RefreshPermissionsCache()` on startup (line 123)
- ✅ Call `RefreshPermissionsCache()` in cleanup loop (line 302)

### AgenticDebuggerVsix2Package.cs (Deleted)
- ✅ Removed duplicate stub package class

---

## Testing Verification

### Before Fixes
```bash
# Build
❌ Release mode: "Extension could not be found" error
✅ Debug mode: Builds successfully

# Runtime (after manual Debug build install)
❌ HTTP requests hang forever (30+ second timeout)
❌ No responses from any endpoint
```

### After Fixes
```bash
# Build
✅ Release mode: Builds successfully
✅ Debug mode: Still works

# Runtime
✅ HTTP server responds immediately
✅ /status endpoint: < 100ms response time
✅ /state endpoint: Works
✅ /projects endpoint: Works
✅ Connection verified at localhost:27183
```

**Successful Response:**
```json
{
  "version": "1.3",
  "extensionName": "Agentic Debugger Bridge",
  "currentMode": "Design",
  "isPrimary": true,
  "port": 27183,
  "permissions": {
    "codeAnalysis": true,
    "observability": true,
    "debugControl": true,
    "buildSystem": true,
    "breakpoints": true,
    "configuration": true
  }
}
```

---

## Key Learnings

### 1. Configuration-Specific Build Settings Matter
Properties like `DeployExtension` must be set for **all** configurations. Debug working ≠ Release working.

### 2. VSIX Projects Don't Work Well with CLI Builds
Old-style .NET Framework VSIX projects require Visual Studio IDE for proper builds. The `ExcludeAssets="runtime"` approach breaks `dotnet build`.

### 3. Never Block Background Threads on UI Thread
In Visual Studio extensions:
- ❌ NEVER call `ThreadHelper.JoinableTaskFactory.Run()` from background threads
- ❌ NEVER use `Invoke()` or `BeginInvoke()` synchronously from background threads
- ✅ Always use caching + periodic UI thread updates
- ✅ Or make endpoints fully async with `async/await`

### 4. ThreadHelper.ThrowIfNotOnUIThread() is Aggressive
This method **throws exceptions** rather than returning false. Never call from background threads, even indirectly through helper methods.

### 5. Testing Both Build Configurations is Critical
Always test **both Debug and Release** configurations, especially for VSIX projects where they can behave very differently.

### 6. Permission Caching Pattern
For extensions with background services:
```
UI Thread (Startup) → Load Permissions → Cache
UI Thread (Periodic) → Refresh Cache every N seconds
Background Thread (HTTP Handler) → Read Cache (no blocking)
```

---

## Diagnostic Commands Used

### Check Port Binding
```bash
netstat -ano | grep 27183
```

### Test Connection
```bash
curl -v -H "X-Api-Key: dev" http://localhost:27183/status
```

### Check Discovery File
```bash
cat $TEMP/agentic_debugger.json
```

### Find Threading Issues
Look for:
- `ThreadHelper.JoinableTaskFactory.Run()` in non-UI thread paths
- `ThreadHelper.ThrowIfNotOnUIThread()` called from background threads
- Synchronous waits on UI thread operations

---

## References

- Visual Studio Extensibility Threading: https://docs.microsoft.com/en-us/visualstudio/extensibility/threading
- JoinableTaskFactory Best Practices: https://github.com/microsoft/vs-threading/blob/main/doc/threading_rules.md
- VSIX Project Configuration: https://docs.microsoft.com/en-us/visualstudio/extensibility/vsix-project-template

---

**Resolution Time:** ~2 hours
**Impact:** Extension now builds and runs correctly in both Debug and Release modes with full HTTP API functionality
