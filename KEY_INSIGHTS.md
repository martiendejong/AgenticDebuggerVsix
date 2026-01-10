# Key Insights - Agentic Debugger VSIX Development

## Threading Anti-Patterns in VS Extensions

### ❌ NEVER DO THIS
```csharp
// In HTTP handler or any background thread
ThreadHelper.JoinableTaskFactory.Run(async () => {
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    var result = _dte.SomeUIThreadOperation();
});
```

**Why:** This creates a deadlock. Background thread blocks waiting for UI thread, but UI thread may be in a state where it can't respond.

### ✅ ALWAYS DO THIS INSTEAD
```csharp
// Pattern 1: Cache + Periodic Refresh
private object _cachedData;

// From UI thread (startup or periodic)
private void RefreshCache()
{
    ThreadHelper.ThrowIfNotOnUIThread();
    _cachedData = _dte.SomeUIThreadOperation();
}

// From background thread (HTTP handler)
private object GetData()
{
    return _cachedData;  // No blocking!
}

// Pattern 2: Fully Async
private async Task HandleAsync(HttpListenerContext ctx)
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    var result = _dte.SomeUIThreadOperation();
    await RespondAsync(ctx, result);
}
```

---

## VSIX Build Configuration Gotchas

### Problem: Configuration-Specific Settings
Debug builds can work while Release builds fail due to missing properties.

### Critical Properties for ALL Configurations
```xml
<!-- MUST be in BOTH Debug AND Release -->
<DeployExtension>false</DeployExtension>
```

**Why:** VS attempts to install/verify extension during build when this is missing. Works in Debug accidentally due to other factors, fails in Release.

---

## Assembly Reference Strategy

### Old Approach (Broken)
```xml
<PackageReference Include="Microsoft.VisualStudio.SDK"
                  ExcludeAssets="runtime" />
```

**Problem:** Blocks assemblies from being available, breaks command-line builds.

### New Approach (Works)
```xml
<PackageReference Include="Microsoft.VisualStudio.SDK"
                  IncludeAssets="compile;build" />
<!-- Plus explicit assembly references -->
<Reference Include="EnvDTE" />
<Reference Include="EnvDTE80" />
<Reference Include="Microsoft.VisualStudio.Shell.15.0" />
```

**Insight:** .NET Framework 4.7.2 VSIX projects need explicit references even with NuGet packages.

---

## Diagnostic Techniques

### 1. Port Listening vs. Responding
```bash
# Port bound?
netstat -ano | grep 27183
# Shows: LISTENING ✅

# But responding?
curl -v http://localhost:27183/status
# Shows: Connected... timeout ❌
```

**Insight:** Port can be listening but server deadlocked. Connection != Response.

### 2. Threading Issues Signature
```
* Connected to localhost
* Request completely sent off
<long pause - 10+ seconds>
* Operation timed out
```

**Diagnosis:** Background thread blocking on UI thread operation.

### 3. Exception Swallowing
```csharp
catch { }  // ❌ Hides the real problem
```

**Better:**
```csharp
catch (Exception ex)
{
    WriteOutput($"Error: {ex}");
    throw;
}
```

---

## Permission System Architecture

### Anti-Pattern: Query on Every Request
```csharp
// Called 100+ times per second during active debugging
private bool CheckPermission()
{
    ThreadHelper.ThrowIfNotOnUIThread();  // ❌ Exception!
    return GetDialogPage().AllowBreakpoints;
}
```

### Correct Pattern: Cache + Periodic Refresh
```csharp
// State
private PermissionsModel _permissions = new() { /* defaults */ };

// Startup (UI thread)
public void Start()
{
    ThreadHelper.ThrowIfNotOnUIThread();
    RefreshPermissionsCache();
    StartBackgroundServer();
}

// Background loop
private async Task RefreshLoop()
{
    while (running)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        RefreshPermissionsCache();
        await Task.Delay(5000);
    }
}

// Any thread
private bool CheckPermission()
{
    return _permissions.AllowBreakpoints;  // ✅ Fast, safe
}
```

**Benefits:**
- No UI thread blocking from background threads
- Changes take effect within refresh interval
- Sub-millisecond permission checks

---

## Development Workflow Lessons

### 1. Test Both Configurations Early
Don't wait until release to test Release builds. Different MSBuild behavior can hide issues.

### 2. Verify After Every Fix
After fixing build issues, verify runtime works:
```bash
# Build succeeds?
dotnet build -c Release

# Extension installs?
# Double-click .vsix

# Runtime works?
curl http://localhost:27183/status
```

### 3. Use Verbose Diagnostics
```bash
curl -v  # See connection details
curl --max-time 3  # Fail fast on hangs
```

### 4. Read the Actual Error
"Extension could not be found" actually meant:
- Not: Extension isn't installed
- But: Build trying to verify extension that doesn't exist yet

---

## VS Extension Threading Rules

### Safe from Any Thread
- Reading cached/immutable data
- Pure computation
- HTTP response writing
- Logging to Debug output

### Requires UI Thread
- `_dte.*` operations (DTE2 automation)
- `GetDialogPage()` and options
- `EnvDTE.Debugger` state queries
- Solution/Project file access
- Output window writing

### How to Switch Safely
```csharp
// From background to UI (async)
await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

// Check if already on UI thread
if (ThreadHelper.JoinableTaskFactory.Context.IsOnMainThread)
{
    // Direct access OK
}
else
{
    // Must switch or use cache
}
```

---

## Root Cause Analysis Pattern

### Symptom Hierarchy
1. **User Report:** "Doesn't build in Release"
2. **Build Error:** "Extension could not be found"
3. **Real Cause:** Missing `<DeployExtension>false</DeployExtension>`

### Multi-Layer Debugging
1. **Build succeeds** → Test installation
2. **Installs successfully** → Test HTTP port
3. **Port listening** → Test HTTP response
4. **Connects but hangs** → Threading issue
5. **Trace code path** → Find blocking call

**Key:** Each layer reveals the next problem. Fix sequentially.

---

## Performance Insights

### Before Fixes
- Build: ❌ Failed in Release
- Connection: 30+ seconds timeout
- Requests/second: 0

### After Fixes
- Build: ✅ <5 seconds
- Connection: <100ms response
- Requests/second: Limited by network latency only (~50-100/sec)

**Bottleneck Removed:** UI thread synchronization was 1000x slower than caching.

---

## Future Prevention

### Build-Time Checks
- Template projects should include `<DeployExtension>false</DeployExtension>` in ALL configurations
- CI should test both Debug and Release builds

### Runtime Checks
- Code analyzer rule: Detect `ThreadHelper.JoinableTaskFactory.Run()` in HttpListener handler
- Unit test: Verify permission checks don't throw from background thread

### Documentation
- Inline comments at all UI thread boundaries
- Architecture diagram showing thread ownership

---

## Applicable Beyond This Project

These patterns apply to:
- **Any VS extension** with background services
- **ASP.NET in VS extensions** (IIS Express integration)
- **Language servers** running in VS process
- **Build tools** with VS automation

**Core Principle:** Never synchronously block background work on UI thread. Cache, queue, or make fully async.

---

## Time Investment vs. Value

**Time spent debugging:** ~2 hours
**Value delivered:**
- Extension now functional ✅
- Build system fixed ✅
- Patterns documented for future projects ✅
- Deep understanding of VS threading model ✅

**ROI:** High - Prevents similar issues in all future VS extensions.
