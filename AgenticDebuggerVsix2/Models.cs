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
}
