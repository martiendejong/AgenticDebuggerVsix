# Implementation Plan: Top 4 Quick Wins

## Overview
Implementing WebSocket support, batch commands, logging, and metrics to achieve 5-10x agent performance improvement.

**Estimated Total Effort**: 2-3 days
**Expected Impact**: 5-10x performance improvement, production readiness

---

## Feature 1: WebSocket Support for Real-Time State Updates

### Goal
Eliminate polling by pushing debugger state changes to connected clients in real-time.

### Technical Design

**New Components:**
- `WebSocketHandler` class to manage WebSocket connections
- Connection registry to track active WebSocket clients
- Event broadcaster to push state changes

**Implementation Steps:**
1. Add WebSocket NuGet package reference
2. Create `WebSocketHandler.cs` with connection management
3. Add `/ws` endpoint to HttpBridge listener
4. Broadcast state changes on debugger events (OnEnterBreakMode, OnEnterRunMode, OnExceptionThrown)
5. Add WebSocket authentication (same X-Api-Key header)

**API Design:**
```
Client connects to: ws://localhost:27183/ws
Server pushes JSON messages:
{
  "event": "stateChange",
  "timestamp": "2026-01-05T...",
  "snapshot": { ... debugger snapshot ... }
}
```

**Files to Modify:**
- `HttpBridge.cs` - Add WebSocket endpoint handler
- New file: `WebSocketHandler.cs`

**Estimated Effort**: ~4 hours

---

## Feature 2: Batch Command Execution

### Goal
Allow agents to send multiple commands in single request, reducing round-trips from 10+ to 1.

### Technical Design

**New Models:**
```csharp
public class BatchCommand
{
    public List<AgentCommand> Commands { get; set; }
    public bool StopOnError { get; set; } = true;
    public bool Atomic { get; set; } = false;
}

public class BatchResponse
{
    public bool Ok { get; set; }
    public List<AgentResponse> Results { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}
```

**Implementation Steps:**
1. Add BatchCommand and BatchResponse models to `Models.cs`
2. Add `POST /batch` endpoint to HttpBridge
3. Implement sequential command execution with error handling
4. Return aggregated results

**Files to Modify:**
- `Models.cs` - Add batch models
- `HttpBridge.cs` - Add `/batch` endpoint handler

**Estimated Effort**: ~2 hours

---

## Feature 3: Request/Response Logging & Replay

### Goal
Log all HTTP/WebSocket interactions for debugging, audit, and replay capabilities.

### Technical Design

**New Components:**
- `RequestLogger` class for structured logging
- Log entry model with request/response/timestamp
- `/logs` endpoint to retrieve logs
- Optional `/replay/{logId}` endpoint to replay past requests

**Log Entry Model:**
```csharp
public class LogEntry
{
    public string Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Method { get; set; }
    public string Path { get; set; }
    public string RequestBody { get; set; }
    public string ResponseBody { get; set; }
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
}
```

**Implementation Steps:**
1. Create `RequestLogger.cs` with in-memory circular buffer (last 1000 entries)
2. Add logging middleware to wrap request handling
3. Add `GET /logs` endpoint (with optional filtering)
4. Add `GET /logs/{id}` for specific entry
5. Optional: Add configuration for log file persistence

**Files to Modify:**
- New file: `RequestLogger.cs`
- `HttpBridge.cs` - Wrap Handle() method with logging

**Estimated Effort**: ~3 hours

---

## Feature 4: Metrics & Health Endpoint

### Goal
Expose performance metrics for monitoring and optimization.

### Technical Design

**Metrics to Track:**
- Total requests
- Requests by endpoint
- Requests by command type
- Error count
- Average response time
- Active WebSocket connections
- Instance registry size

**Metrics Model:**
```csharp
public class Metrics
{
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public long TotalRequests { get; set; }
    public long TotalErrors { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ActiveWebSocketConnections { get; set; }
    public Dictionary<string, long> EndpointCounts { get; set; }
    public Dictionary<string, long> CommandCounts { get; set; }
    public int InstanceCount { get; set; }
}
```

**Implementation Steps:**
1. Create `MetricsCollector.cs` with thread-safe counters
2. Instrument HttpBridge to record metrics
3. Add `GET /metrics` endpoint (JSON format)
4. Add `GET /health` endpoint (simple OK/degraded/down status)
5. Optional: Prometheus format support

**Files to Modify:**
- New file: `MetricsCollector.cs`
- `Models.cs` - Add Metrics model
- `HttpBridge.cs` - Add instrumentation and endpoints

**Estimated Effort**: ~3 hours

---

## Implementation Order

### Day 1 Morning: Foundation (4 hours)
1. Add necessary NuGet packages (WebSocket, JSON logging)
2. Implement Metrics & Health endpoint (simplest, helps monitor other features)
3. Implement Request/Response logging

### Day 1 Afternoon: Batch Commands (2 hours)
4. Implement batch command execution
5. Test batch endpoint with various scenarios

### Day 2: WebSocket Support (4-6 hours)
6. Implement WebSocket handler and connection management
7. Integrate with debugger events
8. Test WebSocket push notifications

### Day 2-3: Testing & Documentation (3-4 hours)
9. Comprehensive testing of all features
10. Update README.md with new endpoints
11. Update Swagger documentation
12. Create example usage scripts for agents
13. Git commit and tag release

---

## Testing Strategy

### Unit Tests
- Batch command execution with various failure scenarios
- Metrics calculation accuracy
- Log entry creation and retrieval

### Integration Tests
- WebSocket connection/disconnection handling
- Multiple WebSocket clients receiving same events
- Batch commands with 10+ operations
- Metrics accuracy under load

### Manual Testing
- Connect with WebSocket client and verify real-time updates
- Send batch commands via Postman/curl
- Review logs endpoint output
- Monitor metrics during debugging session

---

## Documentation Updates

### README.md Updates
Add sections for:
- WebSocket endpoint documentation
- Batch command examples
- Logging and observability
- Metrics endpoint reference

### Swagger Updates
Add schemas for:
- BatchCommand/BatchResponse
- LogEntry
- Metrics
- WebSocket protocol documentation

---

## Dependencies

### NuGet Packages to Add
- System.Net.WebSockets (should be included in .NET Framework)
- Newtonsoft.Json (already present)

### No Breaking Changes
All new features are additive - existing API remains unchanged.

---

## Success Criteria

**Feature Complete When:**
- ✅ WebSocket clients receive state change events in <100ms
- ✅ Batch commands reduce 10-command workflow from ~2s to <200ms
- ✅ All requests logged with <5ms overhead
- ✅ Metrics endpoint shows accurate real-time data
- ✅ Documentation updated with examples
- ✅ All features work in both primary and secondary instances

**Performance Targets:**
- WebSocket push latency: <100ms
- Batch command overhead: <10ms per command
- Logging overhead: <5ms per request
- Metrics endpoint response: <50ms

---

## Risk Mitigation

**Potential Issues:**
1. WebSocket connection stability - Implement heartbeat/ping-pong
2. Memory usage from logging - Use circular buffer with size limit
3. Thread safety in metrics - Use Interlocked operations or ConcurrentDictionary
4. WebSocket authentication - Validate API key in WebSocket handshake

**Rollback Plan:**
All features are additive and can be disabled via configuration if issues arise.

---

## Next Steps After Implementation

**Quick Follow-ups:**
- Add configuration file support to enable/disable features
- Implement WebSocket compression for large payloads
- Add filtering to logs endpoint (by time range, status code, etc.)
- Expose metrics in Prometheus format for monitoring tools

**Monitoring in Production:**
- Watch metrics for performance degradation
- Monitor WebSocket connection churn
- Track log volume and storage
- Measure batch command adoption

---

*This plan represents a pragmatic, high-impact implementation strategy based on expert consensus.*
