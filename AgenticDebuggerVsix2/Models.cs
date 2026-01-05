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

    public sealed class ConfigureRequest
    {
        [JsonProperty("mode")]
        public string Mode { get; set; } = "human"; // "agent" or "human"

        [JsonProperty("suppressWarnings")]
        public bool SuppressWarnings { get; set; } = false;

        [JsonProperty("autoSave")]
        public bool AutoSave { get; set; } = false;
    }

    public sealed class ConfigureResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("appliedMode")]
        public string AppliedMode { get; set; } = "";

        [JsonProperty("settings")]
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    // Roslyn Code Analysis Models
    public sealed class SymbolInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("kind")]
        public string Kind { get; set; } = ""; // Class, Method, Property, Field, etc.

        [JsonProperty("containerName")]
        public string? ContainerName { get; set; }

        [JsonProperty("file")]
        public string? File { get; set; }

        [JsonProperty("line")]
        public int? Line { get; set; }

        [JsonProperty("column")]
        public int? Column { get; set; }

        [JsonProperty("summary")]
        public string? Summary { get; set; } // XML documentation summary
    }

    public sealed class CodeLocation
    {
        [JsonProperty("file")]
        public string File { get; set; } = "";

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("endLine")]
        public int? EndLine { get; set; }

        [JsonProperty("endColumn")]
        public int? EndColumn { get; set; }
    }

    public sealed class SymbolSearchRequest
    {
        [JsonProperty("query")]
        public string Query { get; set; } = "";

        [JsonProperty("kind")]
        public string? Kind { get; set; } // Filter by symbol kind

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; } = 50;
    }

    public sealed class SymbolSearchResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("results")]
        public List<SymbolInfo> Results { get; set; } = new();

        [JsonProperty("totalFound")]
        public int TotalFound { get; set; }
    }

    public sealed class DefinitionRequest
    {
        [JsonProperty("file")]
        public string File { get; set; } = "";

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }
    }

    public sealed class DefinitionResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("symbol")]
        public SymbolInfo? Symbol { get; set; }

        [JsonProperty("location")]
        public CodeLocation? Location { get; set; }
    }

    public sealed class ReferencesRequest
    {
        [JsonProperty("file")]
        public string File { get; set; } = "";

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("includeDeclaration")]
        public bool IncludeDeclaration { get; set; } = true;
    }

    public sealed class ReferencesResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("symbol")]
        public SymbolInfo? Symbol { get; set; }

        [JsonProperty("references")]
        public List<CodeLocation> References { get; set; } = new();

        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }
    }

    public sealed class DocumentOutlineResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("file")]
        public string File { get; set; } = "";

        [JsonProperty("symbols")]
        public List<OutlineSymbol> Symbols { get; set; } = new();
    }

    public sealed class OutlineSymbol
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("kind")]
        public string Kind { get; set; } = "";

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("children")]
        public List<OutlineSymbol> Children { get; set; } = new();
    }

    public sealed class SemanticInfoRequest
    {
        [JsonProperty("file")]
        public string File { get; set; } = "";

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }
    }

    public sealed class SemanticInfoResponse
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("symbol")]
        public SymbolInfo? Symbol { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("documentation")]
        public string? Documentation { get; set; }

        [JsonProperty("isLocal")]
        public bool IsLocal { get; set; }

        [JsonProperty("isParameter")]
        public bool IsParameter { get; set; }
    }
}

