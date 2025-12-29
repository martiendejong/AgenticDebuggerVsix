# Agentic Debugger Bridge (VSIX) - Visual Studio 2022

This VSIX starts a **local HTTP server** inside Visual Studio and exposes basic debugger control:
- set/clear breakpoints
- step into/over/out
- continue (go)
- pause (break)
- read last snapshot (stack + locals best-effort)

## Build & Install

1. Install prerequisites:
   - Visual Studio 2022 with "Visual Studio extension development" workload.

2. Open `AgenticDebuggerVsix.sln`
3. Build (Debug or Release)
4. The VSIX will be produced in:
   `AgenticDebuggerVsix\bin\<Configuration>\AgenticDebuggerVsix.vsix`
5. Double-click the `.vsix` to install into Visual Studio 2022.

## Usage

When Visual Studio starts, the package auto-loads and listens on:

`http://127.0.0.1:27183/`

Authorization:
- Sends/accepts a simple header: `X-Api-Key: dev`
- If missing, default is treated as `dev`

### Get state
GET `/state`

### Send command
POST `/command`
Content-Type: application/json

Examples:

Set breakpoint:
```json
{"action":"setBreakpoint","file":"C:\\path\\to\\UserService.cs","line":87}
```

Continue:
```json
{"action":"go"}
```

Step over:
```json
{"action":"stepOver"}
```

Eval expression:
```json
{"action":"eval","expression":"userId"}
```

Clear all breakpoints:
```json
{"action":"clearBreakpoints"}
```

## Notes
- Snapshot file/line is best-effort; stack + locals are the main reliable pieces.
- This is a minimal proof-of-concept. Extend it with:
  - conditional breakpoints
  - named pipe transport
  - richer variable expansion (scopes/children)
  - safer auth (random token on startup)
