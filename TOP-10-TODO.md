# Top 10 Items Still To Do

**Date**: 2026-01-05
**Current Status**: Roslyn integration complete, awaiting build/test

---

## 🔥 CRITICAL - Must Do First

### 1. **Build, Test & Deploy in Visual Studio 2022** ⚠️
**Priority**: 🔥 CRITICAL
**Effort**: 2-4 hours (MANUAL)
**Value**: Foundation for everything else
**Status**: ⏳ AWAITING MANUAL TESTING

**Why Critical**: All recent work (WebSocket, batch, Roslyn, metrics, logging, configure, self-docs) needs validation in actual VS 2022 before proceeding.

**Tasks**:
- [ ] Open solution in VS 2022
- [ ] Restore NuGet packages (Roslyn packages)
- [ ] Build solution (fix any compilation errors)
- [ ] Debug (F5) to launch experimental instance
- [ ] Test all HTTP endpoints with curl
- [ ] Test Roslyn endpoints (symbol search, definition, references, outline, semantic)
- [ ] Test WebSocket connection
- [ ] Test batch commands
- [ ] Run example Python agents (basic, websocket, autonomous)
- [ ] Document results in BUILD-VALIDATION.md

**Files to reference**:
- MANUAL-STEPS-REQUIRED.md (quick start guide)
- BUILD-VALIDATION.md (comprehensive test checklist)

**Blockers**: None - code is ready
**Next step after completion**: Move to item #2 (Test Execution API)

---

## 🚀 High Value - Strategic Investments

### 2. **Test Execution & Results API** 🧪
**Priority**: 🔥 HIGH
**Effort**: 1 week (500 lines)
**Value**: 9/10 - Enables autonomous validation
**ROI**: 2.25

**Why Important**: Agents can debug and fix code, but can't validate fixes with tests. This closes the loop for autonomous development.

**Features**:
- List all tests in solution
- Filter tests by project/namespace/name
- Run tests (all, filtered, or specific)
- Get pass/fail results with details
- Get test coverage data (if available)

**Implementation**:
- Integrate with VS Test Platform APIs
- New endpoints: `/tests`, `/tests/run`, `/tests/results`
- Models: TestInfo, TestRunRequest, TestRunResponse
- ~500 lines of code

**Impact**: Agents can implement TDD workflows, verify fixes automatically, detect regressions

---

### 3. **Enhanced /configure Endpoint** ⚙️
**Priority**: 🔥 HIGH
**Effort**: 3-5 days (200 lines)
**Value**: 8/10 - Zero blocking UI
**ROI**: 2.7

**Why Important**: Current /configure suppresses some warnings, but hot reload and other modal dialogs still block agents.

**Features**:
- Completely disable hot reload (not just suppress dialog)
- Suppress all modal dialogs system-wide
- Auto-dismiss message boxes
- Configure auto-save timing
- Set project reload behavior
- Return current configuration state

**Implementation**:
- Extend ConfigureRequest/Response models
- Add VS options manipulation
- Hook into dialog interception
- ~200 lines of code

**Impact**: 100% autonomous agent operation, no manual intervention needed

---

### 4. **Build Events via WebSocket** 📡
**Priority**: 🔥 HIGH
**Effort**: 2-3 days (150 lines)
**Value**: 8/10 - Complete lifecycle awareness
**ROI**: 2.7

**Why Important**: Agents don't know when builds start/finish without polling. Real-time notifications complete the event system.

**Features**:
- Build started event (project name, configuration)
- Build progress events (project X of Y building)
- Build completed event (success/failure, errors count)
- Build cancelled event
- Push via WebSocket to all connected clients

**Implementation**:
- Subscribe to DTE build events
- Broadcast via WebSocketHandler
- New event types in WS protocol
- ~150 lines of code

**Impact**: Agents react instantly to build completion, no polling needed

---

### 5. **Smart Breakpoint Management** 🎯
**Priority**: HIGH
**Effort**: 1 week (200 lines)
**Value**: 9/10 - Precision debugging
**ROI**: 3.0

**Why Important**: Current API only supports simple file/line breakpoints. Conditional breakpoints enable sophisticated debugging strategies.

**Features**:
- Set conditional breakpoints ("break when x > 100")
- Set hit count breakpoints ("break on 5th hit")
- Set tracepoints (log without stopping)
- Set function breakpoints (by method name)
- List all breakpoints with conditions
- Enable/disable breakpoints individually

**Implementation**:
- Extend AgentCommand with condition, hitCount, logMessage fields
- Use DTE Breakpoint2 interface for conditions
- New command actions: setConditionalBreakpoint, setTracepoint
- ~200 lines of code

**Impact**: Agents can debug intermittent issues, log state without breaking, set precise break conditions

---

## 💡 Medium Value - Nice to Have

### 6. **Code Modification API (via Roslyn)** ✏️
**Priority**: MEDIUM
**Effort**: 2-3 weeks (400 lines)
**Value**: 10/10 - REVOLUTIONARY
**ROI**: 2.5

**Why Important**: Agents can read and understand code (via Roslyn) but can't modify it. This enables autonomous code fixes.

**Features**:
- Edit document (replace text range)
- Add using statement
- Rename symbol across solution
- Extract method
- Format document
- Apply code fix (if available from analyzer)

**Implementation**:
- Roslyn workspace editing APIs
- Document.WithSyntaxRoot() for modifications
- Format with Formatter.FormatAsync()
- New endpoints: `/code/edit`, `/code/rename`, `/code/format`
- ~400 lines of code

**Impact**: GAME CHANGER - Agents can fix bugs they discover, refactor code, generate code

**Caution**: Requires careful testing, undo support, file watching for conflicts

---

### 7. **Memory Inspection & Heap Analysis** 💾
**Priority**: MEDIUM
**Effort**: 3-4 weeks (700 lines)
**Value**: 9/10 - Deep diagnostics
**ROI**: 1.8

**Why Important**: Agents can see local variables but not heap objects, references, or memory patterns.

**Features**:
- List heap objects (by type)
- Get object details (fields, properties, values)
- Find references to object (who holds this?)
- Detect circular references
- Get GC generation info
- Estimate object size
- Memory leak detection hints

**Implementation**:
- Use debugger memory APIs (CorDebug)
- Heap walking and object enumeration
- Reference tracking
- New endpoints: `/memory/heap`, `/memory/object/{address}`, `/memory/references`
- ~700 lines of code

**Impact**: Agents can diagnose memory leaks, understand object lifecycles

---

### 8. **Source Control Integration (Git)** 🌿
**Priority**: MEDIUM
**Effort**: 2-3 weeks (600 lines)
**Value**: 8/10 - Autonomous workflows
**ROI**: 1.6

**Why Important**: Agents can fix code but can't commit changes, create branches, or check status.

**Features**:
- Git status (staged, unstaged, untracked)
- Git diff (file changes)
- Git commit (with message)
- Git branch (create, switch, list)
- Git log (recent commits)
- Check if repo is clean

**Implementation**:
- Integrate LibGit2Sharp library
- New endpoints: `/git/status`, `/git/commit`, `/git/branch`, `/git/diff`
- Models: GitStatus, GitCommit, GitBranch
- ~600 lines of code

**Impact**: Agents can save fixes to branches, create proper commits with context

---

### 9. **Performance Profiling API** ⚡
**Priority**: MEDIUM
**Effort**: 3-4 weeks (700 lines)
**Value**: 8/10 - Performance analysis
**ROI**: 1.6

**Why Important**: Agents can't measure performance or identify bottlenecks.

**Features**:
- Start/stop profiling session
- Get CPU usage by method
- Get memory allocations
- Get hot paths (most called methods)
- Export profiling data
- Compare two profiling sessions

**Implementation**:
- Integrate VS Profiler APIs
- Background profiling sessions
- New endpoints: `/profile/start`, `/profile/stop`, `/profile/results`
- ~700 lines of code

**Impact**: Agents can find performance bottlenecks, suggest optimizations

---

### 10. **Environment & Launch Configuration API** 🚀
**Priority**: LOW
**Effort**: 1-2 weeks (250 lines)
**Value**: 8/10 - Configuration control
**ROI**: 2.7

**Why Important**: Agents can't configure launch settings (env vars, command args, working directory).

**Features**:
- Read launchSettings.json
- Modify environment variables
- Set command line arguments
- Change working directory
- Select launch profile
- Save changes back to file

**Implementation**:
- Parse/modify launchSettings.json
- Update project properties
- New endpoints: `/launch/settings`, `/launch/configure`
- Models: LaunchSettings, LaunchProfile
- ~250 lines of code

**Impact**: Agents can test different configurations, reproduce environment-specific bugs

---

## 📊 Summary

**Total Items**: 10
**Critical**: 1 (Build & Test - MANUAL)
**High Priority**: 4 (Test Execution, Enhanced Configure, Build Events, Smart Breakpoints)
**Medium Priority**: 5 (Code Modification, Memory Inspection, Git Integration, Performance Profiling, Launch Config)

**Estimated Total Effort**: 15-20 weeks (3-5 months) for all 10 items

**Recommended Order**:
1. ⚠️ Build & Test (MANUAL - do first!)
2. Test Execution API (closes the autonomous development loop)
3. Enhanced /configure (eliminates all blocking)
4. Build Events via WebSocket (completes event system)
5. Smart Breakpoint Management (precision debugging)
6. Code Modification API (revolutionary - enables autonomous fixes)
7. Memory Inspection (deep diagnostics)
8. Git Integration (proper workflows)
9. Performance Profiling (optimization)
10. Launch Configuration (environment control)

**Next Immediate Action**: **Open Visual Studio 2022 and follow MANUAL-STEPS-REQUIRED.md** to validate all recent work before proceeding with new features.

---

**Status**: 📋 TODO LIST - Ready for execution
**Last Updated**: 2026-01-05
**Dependencies**: Item #1 blocks all others - must validate current work first
