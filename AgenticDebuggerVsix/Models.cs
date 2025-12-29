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
}
