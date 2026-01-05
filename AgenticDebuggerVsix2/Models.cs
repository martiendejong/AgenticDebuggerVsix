using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AgenticDebuggerVsix
{
    public sealed class DebuggerSnapshot
    {
        [JsonProperty("timestampUtc")]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [JsonProperty("mode")]
        public string Mode { get; set; } = "Unknown"; // Run/Break/Design

        [JsonProperty("exception")]
        public string? Exception { get; set; }

        [JsonProperty("file")]
        public string? File { get; set; }

        [JsonProperty("line")]
        public int? Line { get; set; }

        [JsonProperty("stack")]
        public List<string> Stack { get; set; } = new();

        [JsonProperty("locals")]
        public Dictionary<string, string> Locals { get; set; } = new();

        [JsonProperty("notes")]
        public string? Notes { get; set; }

        [JsonProperty("solutionName")]
        public string? SolutionName { get; set; }

        [JsonProperty("solutionPath")]
        public string? SolutionPath { get; set; }

        [JsonProperty("startupProject")]
        public string? StartupProject { get; set; }
    }

    public sealed class AgentCommand
    {
        [JsonProperty("action")]
        public string Action { get; set; } = "";

        [JsonProperty("file")]
        public string? File { get; set; }

        [JsonProperty("line")]
        public int? Line { get; set; }

        [JsonProperty("expression")]
        public string? Expression { get; set; }

        [JsonProperty("condition")]
        public string? Condition { get; set; }
        
        // For proxying
        [JsonProperty("instanceId")]
        public string? InstanceId { get; set; }
        
        // For start command
        [JsonProperty("projectName")]
        public string? ProjectName { get; set; }
    }

    public sealed class AgentResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("snapshot")]
        public DebuggerSnapshot? Snapshot { get; set; }
    }
    
    public sealed class InstanceInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        
        [JsonProperty("pid")]
        public int Pid { get; set; }
        
        [JsonProperty("port")]
        public int Port { get; set; }
        
        [JsonProperty("solutionName")]
        public string SolutionName { get; set; } = "";
        
        [JsonProperty("lastSeen")]
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }
    
    public sealed class ErrorItemModel
    {
        [JsonProperty("description")]
        public string Description { get; set; } = "";
        
        [JsonProperty("file")]
        public string? File { get; set; }
        
        [JsonProperty("line")]
        public int Line { get; set; }
        
        [JsonProperty("project")]
        public string? Project { get; set; }
        
        [JsonProperty("errorLevel")]
        public string ErrorLevel { get; set; } = ""; // Error, Warning
    }
    
    public sealed class ProjectModel
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
        
        [JsonProperty("uniqueName")]
        public string UniqueName { get; set; } = "";
        
        [JsonProperty("fullPath")]
        public string? FullPath { get; set; }
    }
    
    public sealed class OutputPaneModel
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("guid")]
        public string? Guid { get; set; }
    }

    public sealed class Metrics
    {
        [JsonProperty("startTime")]
        public DateTime StartTime { get; set; }

        [JsonProperty("uptime")]
        public string Uptime { get; set; } = "";

        [JsonProperty("totalRequests")]
        public long TotalRequests { get; set; }

        [JsonProperty("totalErrors")]
        public long TotalErrors { get; set; }

        [JsonProperty("averageResponseTimeMs")]
        public double AverageResponseTimeMs { get; set; }

        [JsonProperty("activeWebSocketConnections")]
        public int ActiveWebSocketConnections { get; set; }

        [JsonProperty("endpointCounts")]
        public Dictionary<string, long> EndpointCounts { get; set; } = new();

        [JsonProperty("commandCounts")]
        public Dictionary<string, long> CommandCounts { get; set; } = new();

        [JsonProperty("instanceCount")]
        public int InstanceCount { get; set; }
    }

    public sealed class HealthStatus
    {
        [JsonProperty("status")]
        public string Status { get; set; } = ""; // OK, Degraded, Down

        [JsonProperty("uptime")]
        public string Uptime { get; set; } = "";

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("details")]
        public Dictionary<string, string> Details { get; set; } = new();
    }

    public sealed class BatchCommand
    {
        [JsonProperty("commands")]
        public List<AgentCommand> Commands { get; set; } = new();

        [JsonProperty("stopOnError")]
        public bool StopOnError { get; set; } = true;
    }

    public sealed class BatchResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("results")]
        public List<AgentResponse> Results { get; set; } = new();

        [JsonProperty("successCount")]
        public int SuccessCount { get; set; }

        [JsonProperty("failureCount")]
        public int FailureCount { get; set; }

        [JsonProperty("totalCommands")]
        public int TotalCommands { get; set; }
    }
}
