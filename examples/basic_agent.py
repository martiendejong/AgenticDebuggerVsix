"""
Basic Agent Example - Agentic Debugger Bridge
Demonstrates basic HTTP API usage for debugging control.
"""

import requests
import json
import time

# Discovery: Read connection info
import os
import tempfile

discovery_file = os.path.join(tempfile.gettempdir(), "agentic_debugger.json")
with open(discovery_file, 'r') as f:
    discovery = json.load(f)

BASE_URL = f"http://localhost:{discovery['port']}"
API_KEY = discovery['defaultApiKey']
HEADERS = {discovery['keyHeader']: API_KEY}

def get_state():
    """Get current debugger state"""
    resp = requests.get(f"{BASE_URL}/state", headers=HEADERS)
    return resp.json()

def execute_command(action, **kwargs):
    """Execute a debugger command"""
    cmd = {"action": action, **kwargs}
    resp = requests.post(f"{BASE_URL}/command", json=cmd, headers=HEADERS)
    return resp.json()

def batch_execute(commands, stop_on_error=True):
    """Execute multiple commands in a single request"""
    batch = {
        "commands": commands,
        "stopOnError": stop_on_error
    }
    resp = requests.post(f"{BASE_URL}/batch", json=batch, headers=HEADERS)
    return resp.json()

def get_errors():
    """Get build/compilation errors"""
    resp = requests.get(f"{BASE_URL}/errors", headers=HEADERS)
    return resp.json()

def get_metrics():
    """Get performance metrics"""
    resp = requests.get(f"{BASE_URL}/metrics", headers=HEADERS)
    return resp.json()

# Example workflow
if __name__ == "__main__":
    print("🤖 Agentic Debugger - Basic Agent Example")
    print(f"Connected to: {BASE_URL}")
    print()

    # Get initial state
    state = get_state()
    print(f"Current mode: {state['snapshot']['mode']}")
    print()

    # Example 1: Simple command execution
    print("Example 1: Setting breakpoints individually (slow)")
    start = time.time()
    execute_command("setBreakpoint", file="C:\\Code\\Program.cs", line=10)
    execute_command("setBreakpoint", file="C:\\Code\\Worker.cs", line=25)
    execute_command("setBreakpoint", file="C:\\Code\\Service.cs", line=42)
    elapsed = time.time() - start
    print(f"⏱️  Took {elapsed:.2f}s for 3 individual requests")
    print()

    # Example 2: Batch command execution (fast!)
    print("Example 2: Setting breakpoints with batch (fast)")
    start = time.time()
    result = batch_execute([
        {"action": "setBreakpoint", "file": "C:\\Code\\Program.cs", "line": 15},
        {"action": "setBreakpoint", "file": "C:\\Code\\Worker.cs", "line": 30},
        {"action": "setBreakpoint", "file": "C:\\Code\\Service.cs", "line": 45},
    ])
    elapsed = time.time() - start
    print(f"⏱️  Took {elapsed:.2f}s for 1 batch request (3 commands)")
    print(f"✅ Success: {result['successCount']}, ❌ Failures: {result['failureCount']}")
    print()

    # Example 3: Check for build errors
    print("Example 3: Checking for build errors")
    errors = get_errors()
    if errors:
        print(f"⚠️  Found {len(errors)} errors/warnings:")
        for err in errors[:5]:  # Show first 5
            print(f"  - {err['file']}:{err['line']} - {err['description']}")
    else:
        print("✅ No errors found!")
    print()

    # Example 4: View metrics
    print("Example 4: Performance metrics")
    metrics = get_metrics()
    print(f"📊 Uptime: {metrics['uptime']}")
    print(f"📊 Total requests: {metrics['totalRequests']}")
    print(f"📊 Average response time: {metrics['averageResponseTimeMs']:.1f}ms")
    print(f"📊 Active WebSocket connections: {metrics['activeWebSocketConnections']}")
    print()

    # Example 5: Polling for state changes (old way - use WebSocket instead!)
    print("Example 5: Polling for state changes (not recommended)")
    print("Starting debug session...")
    execute_command("start")

    print("Polling for state changes... (this is slow, use WebSocket instead!)")
    for i in range(10):
        state = get_state()
        mode = state['snapshot']['mode']
        print(f"  Poll {i+1}: mode = {mode}")

        if mode == "Break":
            print("🛑 Hit breakpoint!")
            # Get locals
            locals_dict = state['snapshot']['locals']
            print(f"Locals: {list(locals_dict.keys())[:5]}")
            break

        time.sleep(0.5)  # 500ms polling interval - wasteful!

    print()
    print("💡 Tip: Use WebSocket for real-time updates (see websocket_agent.py)")
