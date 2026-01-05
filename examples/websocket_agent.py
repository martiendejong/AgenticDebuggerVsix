"""
WebSocket Agent Example - Agentic Debugger Bridge
Demonstrates real-time debugging with WebSocket push notifications.
"""

import requests
import json
import tempfile
import os
import websocket  # pip install websocket-client

# Discovery
discovery_file = os.path.join(tempfile.gettempdir(), "agentic_debugger.json")
with open(discovery_file, 'r') as f:
    discovery = json.load(f)

BASE_URL = f"http://localhost:{discovery['port']}"
WS_URL = f"ws://localhost:{discovery['port']}/ws"
API_KEY = discovery['defaultApiKey']
HEADERS = {discovery['keyHeader']: API_KEY}

def execute_command(action, **kwargs):
    """Execute a debugger command via HTTP"""
    cmd = {"action": action, **kwargs}
    resp = requests.post(f"{BASE_URL}/command", json=cmd, headers=HEADERS)
    return resp.json()

def batch_execute(commands):
    """Execute batch commands"""
    batch = {"commands": commands, "stopOnError": True}
    resp = requests.post(f"{BASE_URL}/batch", json=batch, headers=HEADERS)
    return resp.json()

# WebSocket event handlers
def on_message(ws, message):
    """Handle incoming WebSocket messages"""
    data = json.loads(message)
    event_type = data.get("type")

    if event_type == "connected":
        print(f"✅ Connected! ID: {data['connectionId']}")

    elif event_type == "stateChange":
        snapshot = data["snapshot"]
        mode = snapshot["mode"]
        print(f"\n🔔 State Change: {mode}")

        if mode == "Break":
            print(f"   📍 File: {snapshot.get('file', 'unknown')}")
            print(f"   📍 Line: {snapshot.get('line', '?')}")
            print(f"   🔍 Locals: {len(snapshot.get('locals', {}))} variables")

            # Show stack trace
            stack = snapshot.get('stack', [])
            if stack:
                print(f"   📚 Stack:")
                for i, frame in enumerate(stack[:3]):  # Top 3 frames
                    print(f"      {i+1}. {frame}")

        elif mode == "Run":
            print("   ▶️  Execution continuing...")

        elif mode == "Design":
            notes = snapshot.get('notes', '')
            print(f"   ⏹️  {notes}")
            # Could trigger cleanup, analysis, etc.

    elif event_type == "pong":
        print("💓 Heartbeat OK")

def on_error(ws, error):
    print(f"❌ WebSocket error: {error}")

def on_close(ws, close_status_code, close_msg):
    print(f"🔌 Connection closed: {close_status_code} - {close_msg}")

def on_open(ws):
    print("🌐 WebSocket connection opened")

# Example: Autonomous debugging agent
if __name__ == "__main__":
    print("🤖 Agentic Debugger - WebSocket Agent Example")
    print(f"Connecting to: {WS_URL}")
    print()

    # Set up WebSocket connection
    ws = websocket.WebSocketApp(
        WS_URL,
        on_message=on_message,
        on_error=on_error,
        on_close=on_close,
        on_open=on_open
    )

    # Before starting WebSocket, set up debugging session
    print("📋 Setting up debugging session...")

    # Use batch commands for fast setup
    print("Setting breakpoints with batch command...")
    result = batch_execute([
        {"action": "clearBreakpoints"},  # Clean slate
        {"action": "setBreakpoint", "file": "C:\\Code\\Program.cs", "line": 42},
        {"action": "setBreakpoint", "file": "C:\\Code\\Worker.cs", "line": 25},
    ])

    if result["ok"]:
        print(f"✅ Setup complete ({result['successCount']} commands executed)")
    print()

    # Start debugging
    print("🚀 Starting debug session...")
    execute_command("start")
    print("Now listening for real-time state changes via WebSocket...")
    print("(Press Ctrl+C to stop)")
    print()

    # Run WebSocket listener (blocks until closed)
    try:
        ws.run_forever()
    except KeyboardInterrupt:
        print("\n👋 Stopping agent...")
        ws.close()
