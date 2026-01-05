using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AgenticDebuggerVsix
{
    internal sealed class MetricsCollector
    {
        private readonly DateTime _startTime;
        private long _totalRequests;
        private long _totalErrors;
        private long _totalResponseTimeMs;
        private readonly ConcurrentDictionary<string, long> _endpointCounts = new();
        private readonly ConcurrentDictionary<string, long> _commandCounts = new();
        private int _activeWebSocketConnections;

        public MetricsCollector()
        {
            _startTime = DateTime.UtcNow;
        }

        public void RecordRequest(string endpoint, long responseTimeMs, bool isError)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalResponseTimeMs, responseTimeMs);

            if (isError)
            {
                Interlocked.Increment(ref _totalErrors);
            }

            _endpointCounts.AddOrUpdate(endpoint, 1, (key, count) => count + 1);
        }

        public void RecordCommand(string commandAction)
        {
            if (!string.IsNullOrWhiteSpace(commandAction))
            {
                _commandCounts.AddOrUpdate(commandAction.ToLowerInvariant(), 1, (key, count) => count + 1);
            }
        }

        public void IncrementWebSocketConnections()
        {
            Interlocked.Increment(ref _activeWebSocketConnections);
        }

        public void DecrementWebSocketConnections()
        {
            Interlocked.Decrement(ref _activeWebSocketConnections);
        }

        public Metrics GetMetrics(int instanceCount)
        {
            var totalReqs = Interlocked.Read(ref _totalRequests);
            var totalTime = Interlocked.Read(ref _totalResponseTimeMs);

            return new Metrics
            {
                StartTime = _startTime,
                Uptime = FormatUptime(DateTime.UtcNow - _startTime),
                TotalRequests = totalReqs,
                TotalErrors = Interlocked.Read(ref _totalErrors),
                AverageResponseTimeMs = totalReqs > 0 ? (double)totalTime / totalReqs : 0,
                ActiveWebSocketConnections = _activeWebSocketConnections,
                EndpointCounts = new Dictionary<string, long>(_endpointCounts),
                CommandCounts = new Dictionary<string, long>(_commandCounts),
                InstanceCount = instanceCount
            };
        }

        public HealthStatus GetHealth()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var totalReqs = Interlocked.Read(ref _totalRequests);
            var totalErrs = Interlocked.Read(ref _totalErrors);

            // Determine status
            string status = "OK";
            var details = new Dictionary<string, string>();

            if (totalReqs > 0)
            {
                var errorRate = (double)totalErrs / totalReqs;
                if (errorRate > 0.5)
                {
                    status = "Degraded";
                    details["reason"] = $"High error rate: {errorRate:P1}";
                }
                else if (errorRate > 0.9)
                {
                    status = "Down";
                    details["reason"] = $"Critical error rate: {errorRate:P1}";
                }
            }

            details["totalRequests"] = totalReqs.ToString();
            details["totalErrors"] = totalErrs.ToString();
            details["activeWebSockets"] = _activeWebSocketConnections.ToString();

            return new HealthStatus
            {
                Status = status,
                Uptime = FormatUptime(uptime),
                Timestamp = DateTime.UtcNow,
                Details = details
            };
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            if (uptime.TotalHours >= 1)
                return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
            if (uptime.TotalMinutes >= 1)
                return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
            return $"{uptime.Seconds}s";
        }
    }

    /// <summary>
    /// Simple stopwatch wrapper for measuring request duration
    /// </summary>
    internal sealed class RequestTimer : IDisposable
    {
        private readonly Stopwatch _sw;
        private readonly Action<long> _onComplete;

        public RequestTimer(Action<long> onComplete)
        {
            _onComplete = onComplete;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _sw.Stop();
            _onComplete?.Invoke(_sw.ElapsedMilliseconds);
        }
    }
}
