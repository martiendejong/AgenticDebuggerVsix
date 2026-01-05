# Build & Validation Checklist

**Status**: Ready for manual build and testing in Visual Studio 2022
**Last Updated**: 2026-01-05

---

## ✅ Pre-Build Preparation Complete

### Project File Updated
- ✅ Added MetricsCollector.cs to compilation
- ✅ Added RequestLogger.cs to compilation
- ✅ Added WebSocketHandler.cs to compilation
- ✅ Added Newtonsoft.Json NuGet package (v13.0.3)

### Source Files Created
- ✅ AgenticDebuggerVsix2/MetricsCollector.cs (metrics collection)
- ✅ AgenticDebuggerVsix2/RequestLogger.cs (request logging)
- ✅ AgenticDebuggerVsix2/WebSocketHandler.cs (WebSocket connections)
- ✅ AgenticDebuggerVsix2/HttpBridge.cs (updated with all features)
- ✅ AgenticDebuggerVsix2/Models.cs (extended with new models)
- ✅ AgenticDebuggerVsix2/AgenticDebuggerPackage.cs (updated event handlers)

---

## 📋 Manual Build Steps

### 1. Open Solution in Visual Studio 2022
```
File → Open → Project/Solution
Navigate to: C:\Projects\AgenticDebuggerVsix\
Open: AgenticDebuggerVsix.sln (or AgenticDebuggerVsix2.csproj)
```

### 2. Restore NuGet Packages
```
Right-click solution → Restore NuGet Packages
OR
Tools → NuGet Package Manager → Package Manager Console
PM> dotnet restore
```

**Expected**: Newtonsoft.Json 13.0.3 should be restored successfully

### 3. Build Solution
```
Build → Build Solution (Ctrl+Shift+B)
OR
Build → Rebuild Solution (for clean build)
```

**Expected Output**:
```
Build started...
1>------ Build started: Project: AgenticDebuggerVsix2, Configuration: Debug Any CPU ------
1>  AgenticDebuggerVsix2 -> C:\Projects\AgenticDebuggerVsix\AgenticDebuggerVsix2\bin\Debug\AgenticDebuggerVsix2.dll
========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
```

### 4. Check for Errors
**If build fails**, check Error List (View → Error List) and fix:
- Missing using statements
- Type mismatches
- Namespace issues
- Missing dependencies

---

## 🧪 Testing Checklist

### Phase 1: Basic Functionality (No VS Extension Required)

#### Test 1: Code Compiles
- [ ] Solution builds without errors
- [ ] All warnings reviewed (ignore harmless ones)
- [ ] VSIX package created in bin/Debug or bin/Release

---

### Phase 2: VS Extension Testing (Manual)

#### Test 2: Install & Load
1. [ ] Close all Visual Studio instances
2. [ ] Double-click generated .vsix file OR use "Debug" → "Start Debugging" (F5)
3. [ ] VS Experimental instance launches
4. [ ] Extension loads without errors
5. [ ] Check Output Window → "Agentic Debugger Bridge" pane
6. [ ] Should see: "Agentic Debugger: I am PRIMARY (Bridge). Listening on port 27183."

#### Test 3: Discovery File
1. [ ] Navigate to `%TEMP%` folder
2. [ ] Find `agentic_debugger.json`
3. [ ] Verify contents:
```json
{
  "port": 27183,
  "pid": <some_number>,
  "apiKeyHeader": "X-Api-Key",
  "defaultApiKey": "dev"
}
```

#### Test 4: HTTP Endpoints (Basic)
Open browser or use curl:

**Root Endpoint:**
```bash
curl http://localhost:27183/
# Expected: "Agentic Debugger Bridge OK. See /docs for usage."
```

**Docs Endpoint:**
```bash
curl http://localhost:27183/docs
# Expected: HTML documentation page
```

**State Endpoint:**
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/state
# Expected: JSON with debugger snapshot
```

---

### Phase 3: New Features Testing

#### Test 5: Metrics Endpoint
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/metrics
```

**Expected Response:**
```json
{
  "startTime": "2026-01-05T...",
  "uptime": "5m 30s",
  "totalRequests": 3,
  "totalErrors": 0,
  "averageResponseTimeMs": 12.5,
  "activeWebSocketConnections": 0,
  "endpointCounts": {
    "/": 1,
    "/docs": 1,
    "/state": 1
  },
  "commandCounts": {},
  "instanceCount": 1
}
```

**Verify:**
- [ ] startTime is recent
- [ ] uptime is reasonable
- [ ] totalRequests increments
- [ ] averageResponseTimeMs < 100ms

#### Test 6: Health Endpoint
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/health
```

**Expected Response:**
```json
{
  "status": "OK",
  "uptime": "5m 45s",
  "timestamp": "2026-01-05T...",
  "details": {
    "totalRequests": "4",
    "totalErrors": "0",
    "activeWebSockets": "0"
  }
}
```

**Verify:**
- [ ] Status is "OK"
- [ ] No errors reported

#### Test 7: Batch Commands
```bash
curl -X POST http://localhost:27183/batch \
  -H "X-Api-Key: dev" \
  -H "Content-Type: application/json" \
  -d '{
    "commands": [
      {"action": "clearBreakpoints"},
      {"action": "setBreakpoint", "file": "C:\\test.cs", "line": 10}
    ],
    "stopOnError": true
  }'
```

**Expected Response:**
```json
{
  "ok": true,
  "results": [
    {"ok": true, "message": "All breakpoints cleared", ...},
    {"ok": true, "message": "Breakpoint added: C:\\test.cs:10", ...}
  ],
  "successCount": 2,
  "failureCount": 0,
  "totalCommands": 2
}
```

**Verify:**
- [ ] Both commands executed
- [ ] successCount = 2
- [ ] Individual results returned

#### Test 8: Request Logging
```bash
# Make a few requests first
curl -H "X-Api-Key: dev" http://localhost:27183/state
curl -H "X-Api-Key: dev" http://localhost:27183/metrics

# Then check logs
curl -H "X-Api-Key: dev" http://localhost:27183/logs
```

**Expected Response:**
```json
[
  {
    "id": "2",
    "timestamp": "2026-01-05T...",
    "method": "GET",
    "path": "/metrics",
    "requestBody": "",
    "responseBody": "{\"startTime\":...",
    "statusCode": 200,
    "durationMs": 15
  },
  {
    "id": "1",
    "timestamp": "2026-01-05T...",
    "method": "GET",
    "path": "/state",
    "requestBody": "",
    "responseBody": "{\"ok\":true...",
    "statusCode": 200,
    "durationMs": 12
  }
]
```

**Verify:**
- [ ] Recent requests appear
- [ ] Most recent first
- [ ] Includes timing info
- [ ] Request/response bodies captured

#### Test 9: Configure Endpoint
```bash
curl -X POST http://localhost:27183/configure \
  -H "X-Api-Key: dev" \
  -H "Content-Type: application/json" \
  -d '{
    "mode": "agent",
    "suppressWarnings": true,
    "autoSave": true
  }'
```

**Expected Response:**
```json
{
  "ok": true,
  "message": "Agent mode configured successfully",
  "appliedMode": "agent",
  "settings": {
    "SuppressBuildUI": "true",
    "AutoloadChangedFiles": "true"
  }
}
```

**Verify:**
- [ ] Configuration applied
- [ ] Settings show success or failure reasons

#### Test 10: WebSocket Connection
Use Python example or WebSocket test tool:

```python
import websocket
import json

ws = websocket.WebSocket()
ws.connect("ws://localhost:27183/ws")

# Receive welcome
msg = ws.recv()
print(json.loads(msg))
# Expected: {"type": "connected", "connectionId": "...", "timestamp": "..."}

# Send ping
ws.send("ping")
response = ws.recv()
print(json.loads(response))
# Expected: {"type": "pong"}

ws.close()
```

**Verify:**
- [ ] Connection established
- [ ] Welcome message received
- [ ] Ping/pong works
- [ ] Metrics show activeWebSocketConnections = 1 while connected

---

### Phase 4: Integration Testing with Example Agents

#### Test 11: Basic Agent
```bash
cd examples
python basic_agent.py
```

**Expected Output:**
```
🤖 Agentic Debugger - Basic Agent Example
Connected to: http://localhost:27183

Current mode: Design

Example 1: Setting breakpoints individually (slow)
⏱️  Took 0.15s for 3 individual requests

Example 2: Setting breakpoints with batch (fast)
⏱️  Took 0.05s for 1 batch request (3 commands)
✅ Success: 3, ❌ Failures: 0

Example 3: Checking for build errors
✅ No errors found!

Example 4: Performance metrics
📊 Uptime: 15m 22s
📊 Total requests: 45
📊 Average response time: 18.5ms
📊 Active WebSocket connections: 0

💡 Tip: Use WebSocket for real-time updates
```

**Verify:**
- [ ] Agent connects successfully
- [ ] Batch is faster than individual
- [ ] Metrics are displayed
- [ ] No errors

#### Test 12: WebSocket Agent
```bash
cd examples
python websocket_agent.py
```

**Expected Output:**
```
🤖 Agentic Debugger - WebSocket Agent Example
Connecting to: ws://localhost:27183/ws

📋 Setting up debugging session...
Setting breakpoints with batch command...
✅ Setup complete (2 commands executed)

🚀 Starting debug session...
Now listening for real-time state changes via WebSocket...
(Press Ctrl+C to stop)

🌐 WebSocket connection opened
✅ Connected! ID: abc123...

[Wait for debugging events]
```

**Manual Steps:**
1. Start debugging in VS (F5)
2. Hit a breakpoint
3. Watch for WebSocket events

**Expected Events:**
```
🔔 State Change: Run
   ▶️  Execution continuing...

🔔 State Change: Break
   📍 File: C:\Code\Program.cs
   📍 Line: 42
   🔍 Locals: 5 variables
   📚 Stack:
      1. Program.Main()
      2. ...
```

**Verify:**
- [ ] WebSocket connects
- [ ] Events received in real-time
- [ ] State changes captured
- [ ] Stack and locals shown

---

## 🐛 Common Issues & Fixes

### Issue 1: Build Fails - Missing Newtonsoft.Json
**Error**: `The type or namespace name 'Newtonsoft' could not be found`

**Fix**:
```bash
PM> Install-Package Newtonsoft.Json
```

### Issue 2: Build Fails - Missing using statements
**Error**: `The type or namespace name 'JsonConvert' could not be found`

**Fix**: Add to top of file:
```csharp
using Newtonsoft.Json;
```

### Issue 3: WebSocket Not Connecting
**Error**: Connection refused or timeout

**Possible Causes**:
- Extension not loaded (check Output window)
- Port 27183 already in use (check with `netstat -an | findstr 27183`)
- Firewall blocking localhost (unlikely but check)

**Fix**:
- Restart VS Experimental instance
- Check for port conflicts
- Verify discovery file exists

### Issue 4: Request Returns 401 Unauthorized
**Error**: 401 response

**Fix**: Include API key header:
```bash
curl -H "X-Api-Key: dev" http://localhost:27183/state
```

### Issue 5: Metrics Show No Data
**Issue**: All counters are 0

**Reason**: This is normal on first load. Make some requests first.

**Fix**: Access a few endpoints, then check metrics again.

---

## ✅ Success Criteria

**Phase 1: Build**
- ✅ Solution builds without errors
- ✅ VSIX package created
- ✅ No critical warnings

**Phase 2: Basic Function**
- ✅ Extension loads in VS
- ✅ Discovery file created
- ✅ Primary port (27183) listening
- ✅ Basic endpoints respond

**Phase 3: New Features**
- ✅ Metrics endpoint returns data
- ✅ Health endpoint shows OK
- ✅ Batch commands execute
- ✅ Logs capture requests
- ✅ Configure endpoint works
- ✅ WebSocket connects and receives events

**Phase 4: Integration**
- ✅ Example agents run successfully
- ✅ WebSocket agent receives real-time events
- ✅ Batch commands are faster than individual
- ✅ No errors in agent output

---

## 📊 Performance Validation

After testing, verify these metrics from `/metrics`:

**Target Performance:**
- Average response time: < 50ms
- Error rate: < 1%
- WebSocket connection success: 100%
- Batch speedup: > 5x vs individual commands

**Actual Performance** (fill in after testing):
- Average response time: _____ ms
- Total requests: _____
- Total errors: _____
- Error rate: _____%
- Active WebSocket connections: _____
- Uptime: _____

---

## 🎯 Next Steps After Validation

Once all tests pass:

1. **Document any issues found** in GitHub issues
2. **Update STATUS.md** with validation results
3. **Tag release** (e.g., v1.0.0-quickwins)
4. **Deploy to production** VS instances
5. **Start using** with real agents
6. **Collect feedback** from actual usage
7. **Proceed to** Next Priority #2 (Roslyn Integration)

---

## 📝 Test Results Log

**Tester**: _____________
**Date**: _____________
**VS Version**: Visual Studio 2022 (version: ______)

### Build Results
- [ ] Build succeeded
- [ ] VSIX created
- [ ] No errors
- Warnings: _____ (list if any)

### Feature Tests
- [ ] Metrics endpoint: PASS / FAIL (notes: _______)
- [ ] Health endpoint: PASS / FAIL (notes: _______)
- [ ] Batch commands: PASS / FAIL (notes: _______)
- [ ] Request logging: PASS / FAIL (notes: _______)
- [ ] Configure endpoint: PASS / FAIL (notes: _______)
- [ ] WebSocket connection: PASS / FAIL (notes: _______)

### Integration Tests
- [ ] basic_agent.py: PASS / FAIL (notes: _______)
- [ ] websocket_agent.py: PASS / FAIL (notes: _______)

### Performance
- Average response time: _____ ms
- Batch vs individual speedup: _____ x
- WebSocket latency: _____ ms

### Issues Found
1. _______________
2. _______________
3. _______________

### Overall Result
- [ ] ✅ ALL TESTS PASSED - Ready for production
- [ ] ⚠️ TESTS PASSED WITH MINOR ISSUES - Document and proceed
- [ ] ❌ TESTS FAILED - Fix issues before proceeding

---

**Status**: Ready for manual validation in Visual Studio 2022
**Next**: Open VS → Build → Test → Document results
