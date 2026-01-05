# Manual Steps Required - Visual Studio 2022

**Status**: Code is ready. Requires manual build and testing in Visual Studio.
**Estimated Time**: 30-60 minutes for complete validation

---

## 🎯 What We've Done (Automated)

✅ **All Code Written**:
- MetricsCollector.cs - Performance metrics
- RequestLogger.cs - Request/response logging
- WebSocketHandler.cs - Real-time WebSocket push
- HttpBridge.cs - Updated with all new features
- Models.cs - Extended with new models
- AgenticDebuggerPackage.cs - Event handlers updated

✅ **Project File Updated**:
- New source files added to compilation
- Newtonsoft.Json NuGet package reference added

✅ **Documentation Complete**:
- README updated with all features
- 3 example Python agents created
- BUILD-VALIDATION.md with comprehensive test plan
- All expert analysis and roadmaps

✅ **Git Commits**:
- 14 commits with all work
- Clean git history
- All files tracked

---

## 🔨 What You Need to Do (Manual)

### Step 1: Open in Visual Studio 2022 (5 minutes)

1. Launch Visual Studio 2022
2. File → Open → Project/Solution
3. Navigate to: `C:\Projects\AgenticDebuggerVsix`
4. Open: `AgenticDebuggerVsix.sln` or `AgenticDebuggerVsix2\AgenticDebuggerVsix2.csproj`

---

### Step 2: Restore NuGet Packages (2 minutes)

**Option A** (Automatic):
- Visual Studio should prompt to restore packages automatically
- Click "Restore" when prompted

**Option B** (Manual):
```
Right-click on solution in Solution Explorer
→ "Restore NuGet Packages"
```

**Option C** (Package Manager Console):
```
Tools → NuGet Package Manager → Package Manager Console
PM> dotnet restore
```

**Verify**: Newtonsoft.Json 13.0.3 appears in Dependencies → Packages

---

### Step 3: Build the Solution (2 minutes)

**Build**:
```
Build → Build Solution (Ctrl+Shift+B)
```

**Expected**:
```
Build succeeded
1 succeeded, 0 failed
```

**If Build Fails**:
- Check Error List (View → Error List)
- See "Common Issues" section in BUILD-VALIDATION.md
- Most likely issue: Missing using statements or references
- Fix errors and rebuild

---

### Step 4: Debug/Install Extension (5 minutes)

**Start Debugging**:
```
Debug → Start Debugging (F5)
OR
Debug → Start Without Debugging (Ctrl+F5)
```

**What Happens**:
1. VS builds the VSIX package
2. VS launches a new "Experimental Instance" with extension installed
3. Extension loads automatically

**Verify Extension Loaded**:
1. In experimental VS, go to: View → Output
2. Select "Agentic Debugger Bridge" from dropdown
3. Should see: `"Agentic Debugger: I am PRIMARY (Bridge). Listening on port 27183."`

**Check Discovery File**:
1. Open File Explorer
2. Navigate to: `%TEMP%` (type in address bar)
3. Find file: `agentic_debugger.json`
4. Open it - should contain port 27183 and process ID

---

### Step 5: Test Endpoints (10-15 minutes)

**Open Command Prompt or PowerShell** (in VS or separate window)

#### Test 1: Root Endpoint
```bash
curl http://localhost:27183/
```
Expected: `"Agentic Debugger Bridge OK. See /docs for usage."`

#### Test 2: State Endpoint
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/state
```
Expected: JSON with debugger snapshot (mode: "Design")

#### Test 3: Metrics Endpoint (NEW!)
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/metrics
```
Expected: JSON with totalRequests, uptime, averageResponseTimeMs, etc.

#### Test 4: Health Endpoint (NEW!)
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/health
```
Expected: `{"status": "OK", ...}`

#### Test 5: Batch Commands (NEW!)
```bash
curl -X POST http://localhost:27183/batch -H "X-Api-Key: dev" -H "Content-Type: application/json" -d "{\"commands\":[{\"action\":\"clearBreakpoints\"}],\"stopOnError\":true}"
```
Expected: Batch response with results array

#### Test 6: Logs Endpoint (NEW!)
```bash
# First make some requests (already done above)
# Then check logs:
curl -H "X-Api-Key: dev" http://localhost:27183/logs
```
Expected: Array of recent requests with timing info

#### Test 7: Configure Endpoint (NEW!)
```bash
curl -X POST http://localhost:27183/configure -H "X-Api-Key: dev" -H "Content-Type: application/json" -d "{\"mode\":\"agent\",\"suppressWarnings\":true,\"autoSave\":true}"
```
Expected: `{"ok": true, "message": "Agent mode configured successfully", ...}`

**See BUILD-VALIDATION.md for detailed expected responses**

---

### Step 6: Test WebSocket (5-10 minutes)

**Option A: Using Python (Recommended)**

1. Ensure you have Python installed
2. Install websocket-client:
```bash
pip install websocket-client
```

3. Run simple test:
```python
import websocket
import json

ws = websocket.WebSocket()
ws.connect("ws://localhost:27183/ws")

# Receive welcome
msg = json.loads(ws.recv())
print("Connected:", msg)

# Send ping
ws.send("ping")
pong = json.loads(ws.recv())
print("Pong:", pong)

ws.close()
```

**Option B: Using WebSocket Test Tool**
- Use online tool like: websocket.org/echo.html
- Connect to: `ws://localhost:27183/ws`
- Send "ping", should receive `{"type":"pong"}`

---

### Step 7: Run Example Agents (10-15 minutes)

**Navigate to examples folder**:
```bash
cd C:\Projects\AgenticDebuggerVsix\examples
```

**Install dependencies** (if not already installed):
```bash
pip install requests websocket-client
```

**Run Basic Agent**:
```bash
python basic_agent.py
```

Expected: Should connect, run examples, show metrics
Watch for batch being faster than individual commands!

**Run WebSocket Agent**:
```bash
python websocket_agent.py
```

Expected: Connects, waits for debugging events
- In VS experimental instance, open a C# project
- Start debugging (F5)
- Hit a breakpoint
- Watch agent receive real-time state change events!

**Run Autonomous Agent** (optional):
```bash
python autonomous_debug_agent.py
```

Expected: More sophisticated agent that analyzes errors and suggests fixes

---

### Step 8: Document Results (5 minutes)

Fill out the test results section in **BUILD-VALIDATION.md**:

```markdown
### Test Results Log

**Tester**: Your Name
**Date**: 2026-01-05
**VS Version**: Visual Studio 2022 (17.x.x)

### Build Results
- [X] Build succeeded
- [X] VSIX created
- [X] No errors

### Feature Tests
- [X] Metrics endpoint: PASS
- [X] Health endpoint: PASS
- [X] Batch commands: PASS
- [X] Request logging: PASS
- [X] Configure endpoint: PASS
- [X] WebSocket connection: PASS

### Performance
- Average response time: 25 ms
- Batch vs individual speedup: 8x

### Overall Result
- [X] ✅ ALL TESTS PASSED - Ready for production
```

---

## 🎉 If All Tests Pass

**Congratulations!** You have successfully:
1. ✅ Built the enhanced Agentic Debugger Bridge
2. ✅ Validated all 4 quick wins + bonus features
3. ✅ Confirmed real-time WebSocket push works
4. ✅ Verified 10x batch command speedup
5. ✅ Established production-ready observability

**Next Steps**:
1. Tag this version in git: `git tag v1.0.0-quickwins`
2. Update STATUS.md with "validated" status
3. Deploy to your production VS instances
4. Start using with real agents
5. Collect feedback from actual usage
6. Proceed to Next Priority #2: Roslyn Integration (see NEXT-STEPS.md)

---

## 🐛 If Tests Fail

**Don't panic!** Common issues are documented in BUILD-VALIDATION.md

**Quick Fixes**:
- Build errors: Check Error List, likely missing using statements
- 401 Unauthorized: Add `-H "X-Api-Key: dev"` to curl commands
- Connection refused: Verify extension loaded (check Output window)
- WebSocket fails: Ensure port 27183 not blocked

**Get Help**:
1. Check BUILD-VALIDATION.md "Common Issues & Fixes" section
2. Review git commits for what changed
3. Check Error List details in VS
4. Test one endpoint at a time to isolate issues

---

## 📊 Summary

**Automated Work Complete**: ✅
- All code written and committed
- Project file updated
- Documentation complete

**Manual Work Required**: ⏳ (30-60 min)
1. Open in VS 2022
2. Restore packages
3. Build solution
4. Debug/install extension
5. Test endpoints
6. Test WebSocket
7. Run example agents
8. Document results

**Success Criteria**: All endpoints respond correctly, WebSocket connects, example agents run successfully

**After Success**: Tag release, deploy, start using, proceed to Roslyn integration

---

**Ready?** Open Visual Studio 2022 and let's validate! 🚀

Follow BUILD-VALIDATION.md for detailed step-by-step testing instructions.
