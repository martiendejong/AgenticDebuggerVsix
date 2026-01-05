# Handling Blocking UI Operations & Popups in Visual Studio

## The Problem

Visual Studio frequently displays modal dialogs and blocking UI operations that can interrupt agent workflows:

### Common Blocking Operations:
1. **Hot Reload Dialogs** - "Apply code changes?" prompt
2. **Build Confirmation** - "Save modified files before building?"
3. **Debug Start Warnings** - "This project is out of date, would you like to build it?"
4. **Exception Assistant** - Large popup showing exception details
5. **IntelliSense Popups** - Completion lists, parameter info
6. **Source Control Prompts** - Check-out confirmations, merge conflicts
7. **Extension Install/Update** - Prompts to install recommended extensions
8. **Solution Load Errors** - Project load failures, missing SDKs
9. **Debugger Attach Dialogs** - Process selection, security warnings
10. **File Save Dialogs** - Encoding warnings, line ending conflicts

### Impact on Agents:
- **Workflow Stalls**: Agent sends command, VS shows dialog, HTTP request times out
- **State Uncertainty**: Agent doesn't know if VS is waiting for input or executing
- **Silent Failures**: Operation doesn't complete but no error returned to agent
- **Resource Waste**: Agent polls `/state` repeatedly while VS waits for human input

---

## Recommended Solutions

### **Strategy 1: Disable Problematic Features via Options** (Preferred for Agent-Driven Sessions)

Configure VS to minimize interactive prompts when running under agent control:

**DTE Automation Commands:**
```csharp
// Disable hot reload prompts
_dte.ExecuteCommand("Tools.Options", "Environment/Documents");
// Set: AutoloadChangedFiles = true, SaveNewlyCreatedFiles = true

// Disable save prompts before build
_dte.Solution.SolutionBuild.BuildAndCheckIfOutOfDate = false;

// Disable debugging warnings
_dte.ExecuteCommand("Tools.Options", "Debugging/General");
// Set: Disable "Warn if no user code on launch"
```

**Add Configuration Endpoint:**
```csharp
POST /configure
{
  "agentMode": true,  // Disables interactive prompts
  "autoSave": true,
  "suppressWarnings": true,
  "hotReloadMode": "silent"  // auto-apply, silent, or interactive
}
```

**Implementation in HttpBridge:**
- New `/configure` endpoint to enable "agent mode"
- Sets VS options programmatically via DTE
- Persists settings or applies only for current session
- Returns current configuration state

---

### **Strategy 2: Timeout & Fallback Detection**

Detect when operations hang due to blocking UI:

**Timeout Monitoring:**
```csharp
public class OperationMonitor
{
    private DateTime _lastStateChange;
    private string _currentOperation;

    public bool IsLikelyBlocked()
    {
        // If state hasn't changed in 30s and we're not in Run mode, likely blocked
        return (DateTime.UtcNow - _lastStateChange).TotalSeconds > 30
               && _dte.Debugger.CurrentMode != dbgDebugMode.dbgRunMode;
    }
}
```

**Add to Snapshot:**
```json
{
  "mode": "Break",
  "likelyBlocked": true,
  "blockReason": "Operation timeout - possible modal dialog",
  "lastActivity": "2026-01-05T14:32:00Z"
}
```

**Agent Can Then:**
- Retry operation with different parameters
- Send "escape" keystrokes via DTE
- Alert human operator
- Abort and try alternative approach

---

### **Strategy 3: Programmatic Dialog Handling**

Use UI Automation to detect and dismiss blocking dialogs:

**Dialog Detection:**
```csharp
using System.Windows.Automation;

public class DialogHandler
{
    public bool TryDismissActiveDialog()
    {
        var desktop = AutomationElement.RootElement;

        // Find modal dialogs
        var condition = new PropertyCondition(
            AutomationElement.ClassNameProperty,
            "#32770"  // Standard dialog class
        );

        var dialog = desktop.FindFirst(TreeScope.Children, condition);
        if (dialog != null)
        {
            // Try to click "Yes" or "OK" button
            var okButton = FindButtonByText(dialog, "OK", "Yes", "Continue", "Apply");
            okButton?.Invoke();
            return true;
        }
        return false;
    }
}
```

**Add Endpoint:**
```
POST /dismissDialog
{
  "buttonText": "Yes"  // Optional: specific button to click
}
```

**Risks:**
- May auto-confirm unintended operations
- Requires elevated permissions
- Fragile if VS UI changes

---

### **Strategy 4: State Enrichment with Dialog Detection**

Enhance `/state` endpoint to report active dialogs:

**Extended Snapshot:**
```csharp
public class DebuggerSnapshot
{
    // ... existing fields ...

    public bool IsDialogActive { get; set; }
    public string? DialogTitle { get; set; }
    public List<string>? DialogButtons { get; set; }
}
```

**Detection Logic:**
```csharp
private bool DetectActiveDialog(out string title, out List<string> buttons)
{
    title = null;
    buttons = new List<string>();

    try
    {
        // Use UI Automation to find modal windows
        var windows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)
        );

        foreach (AutomationElement window in windows)
        {
            if (IsModal(window))
            {
                title = window.Current.Name;
                buttons = GetButtonTexts(window);
                return true;
            }
        }
    }
    catch { }

    return false;
}
```

**Agent Response:**
```json
{
  "mode": "Break",
  "isDialogActive": true,
  "dialogTitle": "Hot Reload",
  "dialogButtons": ["Apply", "Ignore", "Cancel"],
  "suggestion": "Dialog blocking execution - use /dismissDialog or /configure to prevent"
}
```

---

### **Strategy 5: Hot Reload Specific Handling**

Since Hot Reload is a primary concern:

**Disable Hot Reload Entirely:**
```csharp
// In package initialization or via /configure endpoint
_dte.ExecuteCommand("Tools.Options", "Debugging/Hot Reload");
// Set: EnableHotReload = false
```

**Or Set to Auto-Apply:**
```csharp
// Configure to apply changes without prompting
var hotReloadService = GetService<IHotReloadService>();
hotReloadService?.SetMode(HotReloadMode.Automatic);
```

**Add Command to Force Hot Reload:**
```json
POST /command
{
  "action": "applyHotReload",
  "force": true  // Apply without prompting
}
```

---

## Recommended Implementation Priority

### **Phase 1: Essential (Implement Now)**
1. **Add `/configure` endpoint** - Enable "agent mode" to disable prompts
2. **Disable hot reload prompts** - Most common blocker
3. **Add `isBlocked` detection** - Timeout-based heuristic in `/state`

### **Phase 2: Enhanced Detection (Next Sprint)**
4. **UI Automation dialog detection** - Identify active modal dialogs
5. **Extended snapshot with dialog info** - Report dialog title/buttons
6. **Smart defaults for common dialogs** - Auto-handle "Save before build", etc.

### **Phase 3: Advanced Handling (Future)**
7. **`/dismissDialog` endpoint** - Programmatically click buttons
8. **Event hooking for dialog display** - Intercept before dialog shows
9. **Configuration profiles** - Save/restore "agent mode" vs "human mode" settings

---

## Configuration API Design

**Endpoint:** `POST /configure`

**Request:**
```json
{
  "mode": "agent",  // "agent" or "human"
  "settings": {
    "hotReload": "disable",  // "disable", "auto", "prompt"
    "buildWarnings": "suppress",
    "savePrompts": "auto",
    "debuggerWarnings": "suppress",
    "exceptionDialogs": "minimal"
  },
  "persist": false  // If true, save to user settings; if false, session only
}
```

**Response:**
```json
{
  "ok": true,
  "appliedSettings": {
    "hotReload": "disable",
    "buildWarnings": "suppress",
    // ...
  },
  "requiresRestart": false,
  "previousMode": "human"
}
```

**Implementation:**
```csharp
private AgentResponse ConfigureForAgentMode(ConfigureRequest config)
{
    ThreadHelper.ThrowIfNotOnUIThread();

    if (config.Mode == "agent")
    {
        // Disable interactive prompts
        _dte.Properties["Environment", "Documents"].Item("AutoloadChangedFiles").Value = true;
        _dte.Properties["Environment", "Documents"].Item("SaveNewlyCreatedFiles").Value = true;

        // Disable build warnings
        _dte.Solution.SolutionBuild.SuppressUI = true;

        // Configure hot reload
        if (config.Settings.HotReload == "disable")
        {
            // Disable hot reload via DTE or service
        }
    }

    return Ok("Configuration applied");
}
```

---

## Best Practices for Agents Using the Bridge

### **Agent-Side Patterns:**

**1. Pre-Configure on Connect:**
```python
# Agent initialization
bridge.post("/configure", {
    "mode": "agent",
    "settings": {"hotReload": "disable", "buildWarnings": "suppress"}
})
```

**2. Monitor for Blocking:**
```python
state = bridge.get("/state")
if state["likelyBlocked"]:
    # Try to dismiss dialog or alert operator
    bridge.post("/dismissDialog", {"buttonText": "Yes"})
```

**3. Graceful Degradation:**
```python
try:
    result = bridge.post("/command", {"action": "start"}, timeout=30)
except Timeout:
    # Check if blocked
    state = bridge.get("/state")
    if state["isDialogActive"]:
        bridge.post("/dismissDialog")
        result = bridge.post("/command", {"action": "start"})
```

---

## Summary: Recommended Approach

**For AgenticDebuggerVsix:**

1. **Add `/configure` endpoint** with "agent mode" that disables common prompts
2. **Enhance `/state`** with `isBlocked` heuristic and optional dialog detection
3. **Document agent best practices** - configure on connect, monitor for blocks
4. **Provide optional `/dismissDialog`** for advanced use cases

**Key Principle:**
**Prevention > Detection > Remediation**

- **Prevent**: Configure VS to minimize prompts when agent is driving
- **Detect**: Expose when VS is waiting for input via `/state`
- **Remediate**: Provide tools to dismiss dialogs programmatically

This approach balances:
- **Safety**: Doesn't auto-dismiss critical warnings without configuration
- **Usability**: Simple "agent mode" switch for common cases
- **Flexibility**: Advanced dialog handling for power users
- **Transparency**: Agents always know when VS is blocked

---

## Code Example: Complete Implementation

```csharp
// Models.cs - Add configuration models
public sealed class ConfigureRequest
{
    [JsonProperty("mode")]
    public string Mode { get; set; } = "human"; // "agent" or "human"

    [JsonProperty("hotReload")]
    public string HotReload { get; set; } = "prompt"; // "disable", "auto", "prompt"

    [JsonProperty("suppressWarnings")]
    public bool SuppressWarnings { get; set; } = false;
}

// HttpBridge.cs - Add endpoint
if (method == "POST" && path == "/configure")
{
    var body = ReadBody(ctx.Request);
    var config = JsonConvert.DeserializeObject<ConfigureRequest>(body);

    ThreadHelper.JoinableTaskFactory.Run(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var result = ApplyConfiguration(config);
        RespondJson(ctx.Response, result, 200);
    });
    return;
}

// HttpBridge.cs - Implementation
private AgentResponse ApplyConfiguration(ConfigureRequest config)
{
    ThreadHelper.ThrowIfNotOnUIThread();

    try
    {
        if (config.Mode == "agent")
        {
            // Auto-save modified files
            _dte.Properties["Environment", "Documents"].Item("AutoloadChangedFiles").Value = true;

            // Suppress build UI
            _dte.Solution.SolutionBuild.SuppressUI = config.SuppressWarnings;
        }

        return Ok($"Configuration applied: {config.Mode} mode");
    }
    catch (Exception ex)
    {
        return Fail($"Configuration failed: {ex.Message}");
    }
}
```

---

**Next Steps:**
1. Implement `/configure` endpoint with "agent mode" settings
2. Add blocking detection to `/state` endpoint
3. Update documentation with agent best practices
4. Consider UI Automation for dialog detection in Phase 2

This will dramatically improve agent reliability and eliminate the most common failure mode (waiting indefinitely for user input).
