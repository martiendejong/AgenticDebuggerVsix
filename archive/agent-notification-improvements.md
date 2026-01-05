# Agent Notification Improvements

## Problem: Agent Doesn't Know When Debugging Stops

**Issue**: When the debugged application finishes/exits, agents polling `/state` don't get immediate notification. They continue waiting, thinking the application is still running.

**Root Cause**: Agents must poll `/state` endpoint repeatedly. There's latency between when debugging stops and when the agent's next poll occurs.

---

## Solution 1: Capture OnEnterDesignMode Event (✅ IMPLEMENTED)

**What Changed**:
- Added `OnEnterDesignMode` event handler in `HttpBridge.cs`
- Wired up the event in `AgenticDebuggerPackage.cs`
- Snapshot now updates immediately when debugging stops

**Code:**
```csharp
public void OnEnterDesignMode(dbgEventReason Reason)
{
    ThreadHelper.ThrowIfNotOnUIThread();
    var snap = CaptureSnapshot("Design", "Debugging stopped");
    SetSnapshot(snap);
}
```

**Impact**:
- Snapshot is now updated when debugging transitions to Design mode
- Next `/state` poll will show `mode: "Design"` and `notes: "Debugging stopped"`
- Still requires polling, but at least the data is correct when polled

---

## Solution 2: WebSocket Push Notifications (RECOMMENDED - NEXT STEP)

**Why WebSockets Solve This**:
Instead of agents polling every 1-2 seconds:
- Bridge **pushes** state changes to connected agents in real-time (<100ms)
- Agent connects via `ws://localhost:27183/ws`
- Receives instant notifications on:
  - Debugging started
  - Break mode entered (breakpoint hit)
  - Run mode resumed
  - **Design mode entered (debugging stopped)** ← Solves this problem
  - Exception thrown
  - Build completed (future)

**Agent Experience:**
```python
# Before (Polling - 1-2 second delay)
while True:
    state = bridge.get("/state")
    if state["mode"] == "Design":
        break
    time.sleep(1)  # 1 second blind spot

# After (WebSocket - instant notification)
ws = bridge.connect_websocket()
for event in ws:
    if event["type"] == "stateChange" and event["snapshot"]["mode"] == "Design":
        print("Debugging stopped!")
        break
# < 100ms notification
```

**Implementation Priority**: HIGH
- Eliminates polling overhead (reduces API calls by 90%)
- Real-time state synchronization
- Essential for responsive agent workflows

---

## Solution 3: Enhanced State Reporting (FUTURE)

Add more debugging lifecycle events to snapshot:

**Extended DebuggerSnapshot:**
```csharp
public class DebuggerSnapshot
{
    // ... existing fields ...

    public string? LastTransition { get; set; }  // "DebugStarted", "DebugStopped", "BreakpointHit"
    public DateTime TransitionTime { get; set; }
    public int? ExitCode { get; set; }           // Process exit code when debugging stops
}
```

**New Events to Capture:**
- Process started
- Process exited (with exit code)
- Build started/completed/failed
- Hot reload applied
- Breakpoint hit/removed

---

## Polling Best Practices (Until WebSockets)

**For Agents Using the Bridge:**

1. **Poll with Timeout**
   ```python
   state = requests.get("http://localhost:27183/state", timeout=2)
   ```

2. **Watch for Mode Transitions**
   ```python
   previous_mode = None
   while True:
       state = bridge.get("/state")
       current_mode = state["snapshot"]["mode"]

       if current_mode != previous_mode:
           print(f"Mode changed: {previous_mode} -> {current_mode}")
           if current_mode == "Design":
               print("Debugging stopped!")
               break

       previous_mode = current_mode
       time.sleep(0.5)  # 500ms polling interval
   ```

3. **Check Notes Field**
   ```python
   if state["snapshot"]["notes"] == "Debugging stopped":
       # Debugging just ended
   ```

---

## Implementation Status

| Solution | Status | Impact |
|----------|--------|--------|
| OnEnterDesignMode Event | ✅ **Done** | Immediate - snapshot updates correctly |
| WebSocket Push | ⏳ **Next** | High - eliminates polling lag entirely |
| Extended State | 📋 **Future** | Medium - richer debugging context |

---

## Next Steps

1. **Implement WebSocket support** (Final quick win)
   - Solve agent notification lag
   - Reduce API overhead by 90%
   - Enable real-time debugging workflows

2. **Document event types** in `/docs` endpoint
   - List all state transitions
   - Show WebSocket message format
   - Provide example agent code

3. **Add Build events** (future enhancement)
   - Notify when build starts/completes
   - Include build success/failure status
   - Push error list updates

---

**Bottom Line**: WebSockets are the definitive solution to this problem. Implementing WebSocket push notifications is the highest-value remaining quick win.
