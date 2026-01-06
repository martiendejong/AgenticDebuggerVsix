using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AgenticDebuggerVsix
{
    internal sealed class RequestLogger
    {
        private readonly object _lock = new();
        private readonly Queue<LogEntry> _entries = new();
        private const int MaxEntries = 1000;
        private long _nextId = 1;

        public LogEntry StartRequest(string method, string path, string? requestBody)
        {
            var entry = new LogEntry
            {
                Id = System.Threading.Interlocked.Increment(ref _nextId).ToString(),
                Timestamp = DateTime.UtcNow,
                Method = method,
                Path = path,
                RequestBody = requestBody ?? ""
            };

            return entry;
        }

        public void CompleteRequest(LogEntry entry, string responseBody, int statusCode, long durationMs)
        {
            entry.ResponseBody = responseBody;
            entry.StatusCode = statusCode;
            entry.DurationMs = durationMs;

            lock (_lock)
            {
                _entries.Enqueue(entry);

                // Keep only last MaxEntries
                while (_entries.Count > MaxEntries)
                {
                    _entries.Dequeue();
                }
            }
        }

        public List<LogEntry> GetLogs(int? limit = null, string? pathFilter = null, int? minStatusCode = null, int? maxStatusCode = null)
        {
            lock (_lock)
            {
                IEnumerable<LogEntry> query = _entries.Reverse(); // Most recent first

                if (!string.IsNullOrEmpty(pathFilter))
                {
                    query = query.Where(e => e.Path.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (minStatusCode.HasValue)
                {
                    query = query.Where(e => e.StatusCode >= minStatusCode.Value);
                }

                if (maxStatusCode.HasValue)
                {
                    query = query.Where(e => e.StatusCode <= maxStatusCode.Value);
                }

                if (limit.HasValue && limit.Value > 0)
                {
                    query = query.Take(limit.Value);
                }

                return query.ToList();
            }
        }

        public LogEntry? GetLogById(string id)
        {
            lock (_lock)
            {
                return _entries.FirstOrDefault(e => e.Id == id);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }

    public sealed class LogEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; } = "";

        [JsonProperty("path")]
        public string Path { get; set; } = "";

        [JsonProperty("requestBody")]
        public string RequestBody { get; set; } = "";

        [JsonProperty("responseBody")]
        public string ResponseBody { get; set; } = "";

        [JsonProperty("statusCode")]
        public int StatusCode { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("error")]
        public bool IsError => StatusCode >= 400;
    }

    public sealed class LogsQuery
    {
        [JsonProperty("limit")]
        public int? Limit { get; set; }

        [JsonProperty("pathFilter")]
        public string? PathFilter { get; set; }

        [JsonProperty("minStatusCode")]
        public int? MinStatusCode { get; set; }

        [JsonProperty("maxStatusCode")]
        public int? MaxStatusCode { get; set; }
    }
}
