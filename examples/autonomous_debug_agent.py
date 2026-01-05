"""
Autonomous Debugging Agent Example
Demonstrates an AI agent that:
1. Monitors for errors via WebSocket
2. Analyzes error patterns
3. Suggests fixes
4. Validates with test runs
"""

import requests
import json
import tempfile
import os
import websocket
import threading
import time

# Discovery
discovery_file = os.path.join(tempfile.gettempdir(), "agentic_debugger.json")
with open(discovery_file, 'r') as f:
    discovery = json.load(f)

BASE_URL = f"http://localhost:{discovery['port']}"
WS_URL = f"ws://localhost:{discovery['port']}/ws"
API_KEY = discovery['defaultApiKey']
HEADERS = {discovery['keyHeader']: API_KEY}

class AutonomousDebugAgent:
    def __init__(self):
        self.ws = None
        self.running = False
        self.current_state = {}
        self.error_history = []

    def execute_command(self, action, **kwargs):
        """Execute single command"""
        cmd = {"action": action, **kwargs}
        resp = requests.post(f"{BASE_URL}/command", json=cmd, headers=HEADERS)
        return resp.json()

    def batch_execute(self, commands):
        """Execute batch commands"""
        batch = {"commands": commands, "stopOnError": False}  # Continue on error
        resp = requests.post(f"{BASE_URL}/batch", json=batch, headers=HEADERS)
        return resp.json()

    def get_errors(self):
        """Get current build errors"""
        resp = requests.get(f"{BASE_URL}/errors", headers=HEADERS)
        return resp.json()

    def get_output(self, pane="Build"):
        """Get output window content"""
        resp = requests.get(f"{BASE_URL}/output/{pane}", headers=HEADERS)
        if resp.status_code == 200:
            return resp.text
        return None

    def get_logs(self):
        """Get recent request logs"""
        resp = requests.get(f"{BASE_URL}/logs", headers=HEADERS)
        return resp.json()

    def analyze_error(self, error_item):
        """Analyze an error and suggest fixes"""
        description = error_item.get('description', '')
        file_path = error_item.get('file', '')
        line = error_item.get('line', 0)

        print(f"\n🔍 Analyzing error:")
        print(f"   File: {file_path}:{line}")
        print(f"   Error: {description}")

        # Simple pattern matching (in real agent, use LLM here)
        suggestions = []

        if "not defined" in description.lower() or "does not exist" in description.lower():
            suggestions.append("Missing import or undefined variable")
            suggestions.append(f"Action: Check imports at {file_path}")

        elif "null reference" in description.lower():
            suggestions.append("Null reference exception")
            suggestions.append(f"Action: Add null check at line {line}")

        elif "type mismatch" in description.lower() or "cannot convert" in description.lower():
            suggestions.append("Type conversion issue")
            suggestions.append("Action: Check type compatibility")

        return suggestions

    def on_message(self, ws, message):
        """Handle WebSocket messages"""
        data = json.loads(message)
        event_type = data.get("type")

        if event_type == "connected":
            print(f"✅ Agent connected: {data['connectionId']}")

        elif event_type == "stateChange":
            snapshot = data["snapshot"]
            self.current_state = snapshot
            mode = snapshot["mode"]

            print(f"\n🔔 State: {mode}")

            if mode == "Break":
                # Breakpoint hit - analyze context
                exception = snapshot.get('exception')
                if exception:
                    print(f"   ⚠️  Exception: {exception}")
                    self.handle_exception(snapshot)
                else:
                    print(f"   📍 {snapshot.get('file')}:{snapshot.get('line')}")

            elif mode == "Design":
                # Debugging stopped - analyze results
                print("   ⏹️  Session ended")
                self.analyze_session()

    def handle_exception(self, snapshot):
        """Handle exception breakpoint"""
        exception = snapshot.get('exception', '')
        locals_dict = snapshot.get('locals', {})

        print(f"\n🐛 Exception Handler:")
        print(f"   Type: {exception}")
        print(f"   Local variables: {list(locals_dict.keys())[:10]}")

        # In real agent: send context to LLM for analysis
        # LLM would analyze stack trace, locals, exception type
        # and suggest specific code changes

        # Simplified pattern matching
        if "NullReferenceException" in exception:
            print("\n💡 Suggestion: Add null checks before object access")
            print("   Example: if (obj != null) { obj.Method(); }")

        elif "IndexOutOfRangeException" in exception:
            print("\n💡 Suggestion: Validate array/list bounds")
            print("   Example: if (index >= 0 && index < array.Length)")

    def analyze_session(self):
        """Analyze debugging session after completion"""
        print("\n📊 Session Analysis:")

        # Get build errors
        errors = self.get_errors()
        if errors:
            print(f"   ⚠️  {len(errors)} errors/warnings found:")
            for error in errors[:5]:
                suggestions = self.analyze_error(error)
                for suggestion in suggestions[:2]:
                    print(f"      💡 {suggestion}")

        # Get output logs
        build_output = self.get_output("Build")
        if build_output and "error" in build_output.lower():
            print("   📝 Build output contains errors (check /output/Build)")

        # Get request logs to analyze agent behavior
        logs = self.get_logs()
        if logs:
            avg_time = sum(log.get('durationMs', 0) for log in logs) / len(logs)
            print(f"   ⏱️  Average API response time: {avg_time:.1f}ms")

        print("\n✅ Analysis complete")

    def run_debugging_cycle(self):
        """Run a complete debugging cycle"""
        print("\n🔄 Starting automated debugging cycle...")

        # Step 1: Clean build
        print("\n1️⃣ Building solution...")
        result = self.execute_command("build")
        if result.get("ok"):
            print("   ✅ Build triggered")
        time.sleep(2)  # Wait for build

        # Step 2: Check for errors
        errors = self.get_errors()
        if errors:
            print(f"\n2️⃣ Found {len(errors)} errors - analyzing...")
            for error in errors[:3]:
                self.analyze_error(error)
        else:
            print("\n2️⃣ No build errors found!")

            # Step 3: Start debugging
            print("\n3️⃣ Starting debug session...")
            self.batch_execute([
                {"action": "clearBreakpoints"},
                {"action": "setBreakpoint", "file": "C:\\Code\\Program.cs", "line": 42},
                {"action": "start"}
            ])

    def start(self):
        """Start the autonomous agent"""
        print("🤖 Autonomous Debugging Agent Starting...")
        print(f"   Base URL: {BASE_URL}")
        print(f"   WebSocket: {WS_URL}")
        print()

        self.running = True

        # Set up WebSocket in a separate thread
        self.ws = websocket.WebSocketApp(
            WS_URL,
            on_message=self.on_message,
            on_error=lambda ws, err: print(f"❌ WS Error: {err}"),
            on_close=lambda ws, code, msg: print(f"🔌 Disconnected"),
            on_open=lambda ws: print("🌐 WebSocket connected")
        )

        ws_thread = threading.Thread(target=self.ws.run_forever, daemon=True)
        ws_thread.start()

        time.sleep(1)  # Wait for connection

        # Run debugging cycle
        self.run_debugging_cycle()

        print("\n📡 Monitoring for state changes... (Ctrl+C to stop)")
        try:
            while self.running:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\n👋 Stopping agent...")
            self.ws.close()

if __name__ == "__main__":
    agent = AutonomousDebugAgent()
    agent.start()
