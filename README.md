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
*   **Roslyn Code Analysis**: Semantic code navigation - search symbols, go to definition, find references, get document outline, type information.
*   **Observability & Diagnostics**:
    *   **Error List API**: Read compilation errors and warnings directly as JSON.
    *   **Output Window API**: retrieve full build or debug logs programmatically.
    *   **Project List**: Discover solution structure to target builds correctly.
    *   **Metrics & Health**: Real-time performance metrics and health status.
    *   **Request Logging**: Complete audit trail of all API requests/responses.
*   **Discovery Mechanism**: Automatically writes connection info (Port/PID) to `%TEMP%\agentic_debugger.json` so agents can find it without configuration.

## 🔒 Security & Permissions

The Agentic Debugger uses a **permission-based security model** to control what AI agents can access.

### Permission Categories

**Read-Only Permissions (Safe - Enabled by Default):**
- ✅ **Code Analysis**: Semantic search, go-to-definition, find references (Roslyn)
- ✅ **Observability**: Read debugger state, metrics, errors, logs, project info

**Write Permissions (Require Caution - Disabled by Default):**
- ⚠️ **Debug Control**: Start/stop debugging, step through code, control execution
- ⚠️ **Build System**: Trigger builds, rebuilds, clean operations
- ⚠️ **Breakpoints**: Set and clear breakpoints in code
- ⚠️ **Configuration**: Change VS settings, evaluate expressions, add watches

### Configuring Permissions

1. Open Visual Studio
2. Go to **Tools > Options**
3. Navigate to **Agentic Debugger > Permissions**
4. Check/uncheck permissions as needed
5. Click **OK** to apply

### Checking Current Permissions

AI agents can query the `/status` endpoint:

```bash
curl -H "X-Api-Key: dev" http://localhost:27183/status
```

Response includes:
```json
{
  "version": "1.1",
  "permissions": {
    "codeAnalysis": true,
    "observability": true,
    "debugControl": false,
    "buildSystem": false,
    "breakpoints": false,
    "configuration": false
  }
}
```

### Permission Denied Errors

If an agent tries to use a disabled permission, they'll receive a `403 Forbidden` response:

```json
{
  "ok": false,
  "message": "Debug Control permission is disabled. Enable it in Tools > Options > Agentic Debugger."
}
```

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

**Core:**
*   `GET /status` - Get extension version, current mode, and enabled permissions.

**State & Debugging:**
*   `GET /state` - Get current debugger state (Mode, Stack, Locals, File/Line).
*   `GET /errors` - Get list of build errors/warnings.
*   `GET /output/{pane}` - Get text from Output window (e.g. `/output/Build`).
*   `GET /projects` - List projects in the solution.
*   `GET /instances` - List all connected VS instances (Primary only).

**Code Analysis (Roslyn):**
*   `POST /code/symbols` - Search symbols across solution (classes, methods, properties).
*   `POST /code/definition` - Go to definition at file position.
*   `POST /code/references` - Find all references to symbol.
*   `GET /code/outline?file={path}` - Get document structure hierarchy.
*   `POST /code/semantic` - Get semantic info at position (type, docs).

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

### 8. Roslyn Code Analysis (NEW!)

Semantic code navigation and analysis powered by Roslyn. Enables agents to understand code structure, search symbols, navigate definitions, and find references.

**Search Symbols Across Solution:**
```bash
POST /code/symbols
{
  "query": "Customer",
  "kind": "Class",  # optional: Class, Method, Property, Field, etc.
  "maxResults": 50
}
```

**Response:**
```json
{
  "ok": true,
  "results": [
    {
      "name": "Customer",
      "kind": "Class",
      "containerName": "MyApp.Models",
      "file": "C:\\Code\\Models\\Customer.cs",
      "line": 10,
      "column": 18,
      "summary": "Represents a customer entity"
    }
  ],
  "totalFound": 1
}
```

**Go to Definition:**
```bash
POST /code/definition
{
  "file": "C:\\Code\\Program.cs",
  "line": 42,
  "column": 15
}
```

**Find All References:**
```bash
POST /code/references
{
  "file": "C:\\Code\\Models\\Customer.cs",
  "line": 10,
  "column": 18,
  "includeDeclaration": true
}
```

**Response:**
```json
{
  "ok": true,
  "symbol": {
    "name": "Customer",
    "kind": "Class",
    "containerName": "MyApp.Models"
  },
  "references": [
    {
      "file": "C:\\Code\\Program.cs",
      "line": 25,
      "column": 12,
      "endLine": 25,
      "endColumn": 20
    }
  ],
  "totalCount": 15
}
```

**Get Document Outline:**
```bash
GET /code/outline?file=C:\Code\Program.cs
```

Returns hierarchical structure of classes, methods, properties in the file.

**Get Semantic Info at Position:**
```bash
POST /code/semantic
{
  "file": "C:\\Code\\Program.cs",
  "line": 42,
  "column": 15
}
```

**Response:**
```json
{
  "ok": true,
  "symbol": {
    "name": "ProcessOrder",
    "kind": "Method",
    "containerName": "OrderService"
  },
  "type": "void",
  "documentation": "<summary>Processes an order...</summary>",
  "isLocal": false,
  "isParameter": false
}
```

**Benefits:**
- **100x Agent Capabilities**: Semantic understanding vs text-only
- **Code Navigation**: Jump to definitions, find usages across solution
- **Intelligent Search**: Find classes, methods, properties by name
- **Type Information**: Understand types, documentation, symbols
- **Context Aware**: Know what symbol is under cursor position

## 🔒 Security Note
This extension opens a local HTTP server. It uses a simple `X-Api-Key: dev` header by default. It is intended for **local development use only** to bridge AI agents running on the same machine.

---
*Built for the next generation of AI-assisted software development.*
