# Example Agent Scripts

This folder contains example Python scripts demonstrating how to build AI agents that interact with the Agentic Debugger Bridge.

## Prerequisites

Install required Python packages:
```bash
pip install requests websocket-client
```

## Examples

### 1. `basic_agent.py` - HTTP API Basics

Demonstrates fundamental HTTP API usage:
- Discovery file reading
- State polling
- Command execution
- Batch commands (10x faster than individual commands)
- Error list retrieval
- Metrics monitoring

**Run:**
```bash
python basic_agent.py
```

**Key Learnings:**
- How to read the discovery file (`%TEMP%\agentic_debugger.json`)
- Difference between individual commands (slow) vs batch (fast)
- Polling for state changes (not recommended - use WebSocket instead!)

---

### 2. `websocket_agent.py` - Real-Time WebSocket

Demonstrates WebSocket real-time push notifications:
- WebSocket connection setup
- Real-time state change notifications
- Event handling for Break/Run/Design modes
- Batch command setup
- Heartbeat/keepalive

**Run:**
```bash
python websocket_agent.py
```

**Key Learnings:**
- <100ms notification latency (vs 1-2s polling)
- Automatic state updates when debugging events occur
- How to structure a WebSocket-based agent
- Event-driven debugging workflows

**Benefits Over Polling:**
- 90% reduction in API calls
- Instant notification when debugging stops (solves "agent doesn't know when app finishes" problem)
- Lower CPU usage
- More responsive agent behavior

---

### 3. `autonomous_debug_agent.py` - Autonomous Debugging

Demonstrates a more sophisticated autonomous agent that:
- Monitors debugging sessions in real-time
- Analyzes build errors and exceptions
- Suggests fixes based on error patterns
- Reviews output logs and metrics
- Runs complete debugging cycles autonomously

**Run:**
```bash
python autonomous_debug_agent.py
```

**Key Learnings:**
- How to combine multiple API endpoints for comprehensive analysis
- Error pattern matching and suggestion generation
- Session analysis using logs and metrics
- Building multi-step autonomous workflows
- Exception handling and context analysis

**Real-World Applications:**
- Integrate with LLM (GPT-4, Claude) to analyze exceptions and suggest code fixes
- Automatically set breakpoints based on error patterns
- Generate debugging reports
- Validate fixes by running tests
- Learn from debugging patterns over time

---

## API Endpoints Used

### Discovery
- `%TEMP%\agentic_debugger.json` - Connection info (port, API key)

### State & Control
- `GET /state` - Current debugger state (mode, stack, locals, file/line)
- `POST /command` - Execute single debugger command
- `POST /batch` - Execute multiple commands (10x faster)
- `WS /ws` - WebSocket for real-time state push

### Observability
- `GET /errors` - Build errors and warnings
- `GET /output/{pane}` - Output window content (Build, Debug, etc.)
- `GET /metrics` - Performance metrics
- `GET /logs` - Request/response audit trail
- `GET /health` - Health status

### Projects
- `GET /projects` - List solution projects
- `GET /instances` - List VS instances (primary only)

---

## Building Your Own Agent

### Pattern 1: Polling Agent (Simple, Higher Latency)
```python
while True:
    state = requests.get(f"{BASE_URL}/state", headers=HEADERS).json()
    if state['snapshot']['mode'] == 'Break':
        # Handle breakpoint
    time.sleep(0.5)  # 500ms polling
```

**Pros:** Simple, easy to understand
**Cons:** 500ms+ latency, high API overhead

---

### Pattern 2: WebSocket Agent (Recommended)
```python
def on_message(ws, message):
    data = json.loads(message)
    if data['type'] == 'stateChange':
        snapshot = data['snapshot']
        if snapshot['mode'] == 'Break':
            # Handle breakpoint instantly

ws = websocket.WebSocketApp(WS_URL, on_message=on_message)
ws.run_forever()
```

**Pros:** <100ms latency, 90% less API calls, real-time
**Cons:** Slightly more complex setup

---

### Pattern 3: Hybrid Agent (Best of Both)
```python
# Use WebSocket for real-time events
# Use HTTP for commands and queries

# WebSocket: Listen for state changes
ws_thread = threading.Thread(target=ws.run_forever, daemon=True)
ws_thread.start()

# HTTP: Execute commands when needed
def on_breakpoint_hit(snapshot):
    # Analyze with HTTP APIs
    errors = requests.get(f"{BASE_URL}/errors").json()

    # Execute fix with batch
    batch_execute([
        {"action": "eval", "expression": "variable"},
        {"action": "stepOver"}
    ])
```

**Pros:** Real-time notifications + full control
**Cons:** More complex architecture

---

## Common Workflows

### Workflow 1: Automated Breakpoint Debugging
```python
# 1. Set up breakpoints with batch
batch_execute([
    {"action": "setBreakpoint", "file": "Program.cs", "line": 42},
    {"action": "setBreakpoint", "file": "Worker.cs", "line": 25},
    {"action": "start"}
])

# 2. Listen for breakpoints via WebSocket
def on_message(ws, msg):
    if msg['type'] == 'stateChange' and msg['snapshot']['mode'] == 'Break':
        locals_dict = msg['snapshot']['locals']
        # Analyze locals, step through code, etc.
```

### Workflow 2: Build Error Analysis
```python
# 1. Trigger build
execute_command("build")
time.sleep(2)

# 2. Get errors
errors = requests.get(f"{BASE_URL}/errors").json()

# 3. Analyze patterns
for error in errors:
    # Send to LLM for fix suggestions
    # Apply fixes
    # Rebuild and validate
```

### Workflow 3: Exception Monitoring
```python
# Via WebSocket, get instant exception notifications
def on_message(ws, msg):
    snapshot = msg['snapshot']
    if snapshot.get('exception'):
        exception_type = snapshot['exception']
        stack_trace = snapshot['stack']
        locals_dict = snapshot['locals']

        # Send context to LLM
        # Get suggested fix
        # Apply and validate
```

---

## Integration with LLMs

All examples can be extended with LLM integration:

### Claude/GPT-4 Integration Example
```python
import anthropic

def analyze_exception_with_claude(snapshot):
    client = anthropic.Anthropic(api_key="...")

    context = f"""
    Exception: {snapshot['exception']}
    File: {snapshot['file']}:{snapshot['line']}
    Stack: {snapshot['stack']}
    Locals: {snapshot['locals']}
    """

    message = client.messages.create(
        model="claude-sonnet-4-5",
        messages=[{
            "role": "user",
            "content": f"Analyze this exception and suggest a fix:\n\n{context}"
        }]
    )

    return message.content[0].text
```

---

## Tips & Best Practices

1. **Use Batch Commands**: Reduce round-trips from N to 1
2. **Prefer WebSocket**: 90% less API overhead than polling
3. **Monitor Metrics**: Use `/metrics` to track agent performance
4. **Use Logs for Debugging**: `/logs` helps debug agent behavior
5. **Handle Connection Failures**: Agents should reconnect gracefully
6. **Rate Limiting**: Don't spam the API (but with WebSocket, you don't need to)

---

## Next Steps

- Read `handling-blocking-ui-operations.md` for dealing with VS dialogs
- Review `agent-notification-improvements.md` for WebSocket benefits
- Check `PROGRESS-SUMMARY.md` for all features available
- Explore `/docs` endpoint at `http://localhost:27183/docs`

**Happy Agent Building!** 🤖
