# Agentic Debugger Bridge for Visual Studio 2022

**Enable AI Agents to See and Control Visual Studio.**

The **Agentic Debugger Bridge** allows external tools (like AI coding agents) to drive Visual Studio's debugger, build system, and error lists programmatically via a simple HTTP API.

## 🚀 Key Features

*   **HTTP Control Bridge**: Exposes an HTTP server (default port `27183`) to control VS.
*   **Multi-Instance Support**: The first VS instance acts as the **Primary Bridge**. It automatically discovers and routes commands to other open VS instances (Secondary).
*   **Full Debugger Control**: Start, Stop, Break, Step Into/Over/Out, Set Breakpoints, Evaluate Expressions.
*   **Build System Integration**: Clean, Build, and Rebuild solutions or specific projects.
*   **Observability & Diagnostics**:
    *   **Error List API**: Read compilation errors and warnings directly as JSON.
    *   **Output Window API**: retrieve full build or debug logs programmatically.
    *   **Project List**: Discover solution structure to target builds correctly.
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
*   `GET /state` - Get current debugger state (Mode, Stack, Locals, File/Line).
*   `GET /errors` - Get list of build errors/warnings.
*   `GET /output/{pane}` - Get text from Output window (e.g. `/output/Build`).
*   `GET /projects` - List projects in the solution.
*   `GET /instances` - List all connected VS instances (Primary only).

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

## 🔒 Security Note
This extension opens a local HTTP server. It uses a simple `X-Api-Key: dev` header by default. It is intended for **local development use only** to bridge AI agents running on the same machine.

---
*Built for the next generation of AI-assisted software development.*
