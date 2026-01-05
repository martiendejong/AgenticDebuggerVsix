# Agentic Debugger Bridge for Visual Studio 2022

**Enable AI Agents to See and Control Visual Studio.**

The **Agentic Debugger Bridge** allows external tools (like AI coding agents) to drive Visual Studio's debugger, build system, and error lists programmatically via a simple HTTP API.

## 🚀 Key Features

*   **HTTP Control Bridge**: Exposes an HTTP server (default port `27183`) to control VS.
*   **WebSocket Real-Time Updates**: Push notifications for state changes (<100ms latency) - eliminates polling!
*   **Multi-Instance Support**: The first VS instance acts as the **Primary Bridge**. It automatically discovers and routes commands to other open VS instances (Secondary).
*   **Full Debugger Control**: Start, Stop, Break, Step Into/Over/Out, Set Breakpoints, Evaluate Expressions.
*   **Build System Integration**: Clean, Build, and Rebuild solutions or specific projects.
*   **Batch Command Execution**: Execute multiple commands in a single request (10x faster workflows).
*   **Observability & Diagnostics**:
    *   **Error List API**: Read compilation errors and warnings directly as JSON.
    *   **Output Window API**: retrieve full build or debug logs programmatically.
    *   **Project List**: Discover solution structure to target builds correctly.
    *   **Metrics & Health**: Real-time performance metrics and health status.
    *   **Request Logging**: Complete audit trail of all API requests/responses.
*   **Discovery Mechanism**: Automatically writes connection info (Port/PID) to `%TEMP%\agentic_debugger.json` so agents can find it without configuration.

## 🛠️ Usage

### 1. Installation
Install the VSIX. When you open Visual Studio, the bridge automatically starts.
- **Primary Instance**: Binds to **Port 27183**.
- **Secondary Instances**: Bind to random ports and register with the Primary.

### 2. Discovery
Your AI agent can look for the discovery file to know where to connect:
`%TEMP%\agentic_debugger.json`
```json
{
  "port": 27183,
  "pid": 1234,
  "keyHeader": "X-Api-Key",
  "defaultKey": "dev"
}
```

### 3. API Overview
Visit **`http://localhost:27183/docs`** for a friendly documentation page, or **`http://localhost:27183/swagger.json`** for the OpenAPI definition.

#### Common Endpoints

**State & Debugging:**
*   `GET /state` - Get current debugger state (Mode, Stack, Locals, File/Line).
*   `GET /errors` - Get list of build errors/warnings.
*   `GET /output/{pane}` - Get text from Output window (e.g. `/output/Build`).
*   `GET /projects` - List projects in the solution.
*   `GET /instances` - List all connected VS instances (Primary only).

**Observability:**
*   `GET /metrics` - Real-time performance metrics (requests, latency, commands).
*   `GET /health` - Health status (OK/Degraded/Down).
*   `GET /logs` - Recent request/response logs (last 100).
*   `GET /logs/{id}` - Specific log entry by ID.
*   `DELETE /logs` - Clear all logs.

**Real-Time:**
*   `WS /ws` - WebSocket connection for real-time state push notifications.

#### Commands (`POST /command`)
Send JSON payload to execute actions.
*   **Start Debugging**: `{"action": "start"}` (or `{"action": "start", "projectName": "MyProject"}`)
*   **Stop**: `{"action": "stop"}`
*   **Step**: `{"action": "stepOver"}`, `{"action": "stepInto"}`
*   **Break**: `{"action": "break"}`
*   **Build**: `{"action": "build"}` / `{"action": "rebuild"}` / `{"action": "clean"}`

**Controlling Specific Instances**:
To control a secondary instance from the primary bridge, include `instanceId`:
```json
{
  "action": "start",
  "instanceId": "34a6...",
  "projectName": "BackendAPI"
}
```

### 4. Batch Commands (`POST /batch`)

Execute multiple commands in a single request for 10x faster workflows:

```json
POST /batch
{
  "commands": [
    {"action": "setBreakpoint", "file": "C:\\Code\\Program.cs", "line": 42},
    {"action": "setBreakpoint", "file": "C:\\Code\\Worker.cs", "line": 15},
    {"action": "start"}
  ],
  "stopOnError": true
}
```

**Response:**
```json
{
  "ok": true,
  "results": [ /* individual command responses */ ],
  "successCount": 3,
  "failureCount": 0,
  "totalCommands": 3
}
```

**Benefits:**
- Reduce round-trips from N requests to 1
- Atomic execution with `stopOnError`
- Aggregated success/failure reporting

### 5. WebSocket Real-Time Updates

Connect to `ws://localhost:27183/ws` for push notifications instead of polling:

**Connection:**
```python
import websocket
import json

ws = websocket.WebSocket()
ws.connect("ws://localhost:27183/ws")

# Receive welcome message
welcome = json.loads(ws.recv())
print(f"Connected: {welcome['connectionId']}")

# Listen for state changes
while True:
    message = json.loads(ws.recv())
    if message["type"] == "stateChange":
        snapshot = message["snapshot"]
        print(f"Debugger mode: {snapshot['mode']}")
        if snapshot["mode"] == "Design":
            print("Debugging stopped!")
            break
```

**Event Types:**
- `connected` - Initial connection established
- `stateChange` - Debugger state changed (Break/Run/Design mode)
  - Includes full `snapshot` with mode, stack, locals, file/line
- `pong` - Response to ping keepalive

**Benefits:**
- <100ms notification latency (vs 1-2s polling)
- 90% reduction in API calls
- Real-time agent responsiveness
- Eliminates "agent doesn't know when debugging finishes" problem

### 6. Metrics & Health Monitoring

**Get Metrics:**
```bash
curl http://localhost:27183/metrics
```

**Response:**
```json
{
  "startTime": "2026-01-05T10:00:00Z",
  "uptime": "2h 15m 30s",
  "totalRequests": 1234,
  "totalErrors": 5,
  "averageResponseTimeMs": 45.2,
  "activeWebSocketConnections": 2,
  "endpointCounts": {
    "/state": 500,
    "/command": 300,
    "/batch": 50
  },
  "commandCounts": {
    "start": 10,
    "stepover": 150,
    "setbreakpoint": 45
  },
  "instanceCount": 3
}
```

**Health Check:**
```bash
curl http://localhost:27183/health
```

Returns `200 OK` if healthy, `503` if degraded/down.

### 7. Request Logging

**View Recent Logs:**
```bash
curl http://localhost:27183/logs
```

Returns last 100 requests with timing, status codes, and bodies.

**Get Specific Log:**
```bash
curl http://localhost:27183/logs/123
```

**Clear Logs:**
```bash
curl -X DELETE http://localhost:27183/logs
```

## 🔒 Security Note
This extension opens a local HTTP server. It uses a simple `X-Api-Key: dev` header by default. It is intended for **local development use only** to bridge AI agents running on the same machine.

---
*Built for the next generation of AI-assisted software development.*
