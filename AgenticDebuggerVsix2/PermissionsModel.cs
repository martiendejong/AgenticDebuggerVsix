using System;

namespace AgenticDebuggerVsix
{
    /// <summary>
    /// Defines which API categories the agent can access
    /// </summary>
    [Serializable]
    public class PermissionsModel
    {
        // Read-only permissions (safe - enabled by default)
        public bool AllowCodeAnalysis { get; set; } = true;
        public bool AllowObservability { get; set; } = true;

        // Write permissions (dangerous - disabled by default)
        public bool AllowDebugControl { get; set; } = false;
        public bool AllowBuildSystem { get; set; } = false;
        public bool AllowBreakpoints { get; set; } = false;
        public bool AllowConfiguration { get; set; } = false;

        // Custom API key (future enhancement)
        public string ApiKey { get; set; } = "dev";

        /// <summary>
        /// Checks if an endpoint is allowed based on permissions
        /// </summary>
        public bool IsEndpointAllowed(string method, string path, string action = null)
        {
            // Core endpoints always allowed
            if (path == "/" || path == "/docs" || path == "/swagger.json" ||
                path == "/status" || path == "/ws")
                return true;

            // Code Analysis
            if (path.StartsWith("/code/"))
                return AllowCodeAnalysis;

            // Observability (read-only)
            if (path == "/state" || path == "/metrics" || path == "/health" ||
                path == "/errors" || path == "/projects" || path == "/instances" ||
                path.StartsWith("/output") || (path.StartsWith("/logs") && method == "GET"))
                return AllowObservability;

            // Logs deletion requires observability
            if (path == "/logs" && method == "DELETE")
                return AllowObservability;

            // Configuration
            if (path == "/configure")
                return AllowConfiguration;

            // /command and /batch require action-specific checks
            if (path == "/command" || path == "/batch")
            {
                return IsCommandAllowed(action);
            }

            // Proxy and register endpoints (multi-instance support)
            if (path.StartsWith("/proxy/") || path == "/register")
                return AllowObservability; // Registry operations require observability

            // Default deny
            return false;
        }

        private bool IsCommandAllowed(string action)
        {
            if (string.IsNullOrEmpty(action))
                return false;

            var act = action.Trim().ToLowerInvariant();

            // Debug Control
            if (act == "start" || act == "go" || act == "continue" ||
                act == "stop" || act == "break" || act == "pause" ||
                act == "stepinto" || act == "stepover" || act == "stepout")
                return AllowDebugControl;

            // Build System
            if (act == "build" || act == "rebuild" || act == "clean")
                return AllowBuildSystem;

            // Breakpoints
            if (act == "setbreakpoint" || act == "clearbreakpoints" ||
                act == "bp" || act == "clrbp")
                return AllowBreakpoints;

            // Configuration/Evaluation
            if (act == "eval" || act == "addwatch")
                return AllowConfiguration;

            return false;
        }

        /// <summary>
        /// Gets a user-friendly error message for denied permission
        /// </summary>
        public string GetPermissionDeniedMessage(string method, string path, string action = null)
        {
            if (path.StartsWith("/code/"))
                return "Code Analysis permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

            if (path == "/state" || path == "/metrics" || path == "/health" ||
                path == "/errors" || path == "/projects" || path == "/instances" ||
                path.StartsWith("/output") || path.StartsWith("/logs"))
                return "Observability permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

            if (path == "/configure")
                return "Configuration permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

            if (path.StartsWith("/proxy/") || path == "/register")
                return "Multi-instance operations require Observability permission. Enable it in Tools > Options > Agentic Debugger.";

            if (!string.IsNullOrEmpty(action))
            {
                var act = action.ToLowerInvariant();
                if (act == "start" || act == "go" || act == "continue" || act == "stop" ||
                    act == "break" || act == "pause" || act == "stepinto" || act == "stepover" || act == "stepout")
                    return "Debug Control permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

                if (act == "build" || act == "rebuild" || act == "clean")
                    return "Build System permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

                if (act == "setbreakpoint" || act == "clearbreakpoints" || act == "bp" || act == "clrbp")
                    return "Breakpoints permission is disabled. Enable it in Tools > Options > Agentic Debugger.";

                if (act == "eval" || act == "addwatch")
                    return "Configuration permission is disabled. Enable it in Tools > Options > Agentic Debugger.";
            }

            return "Permission denied. Configure permissions in Tools > Options > Agentic Debugger.";
        }

        /// <summary>
        /// Reset to safe defaults
        /// </summary>
        public void ResetToDefaults()
        {
            AllowCodeAnalysis = true;
            AllowObservability = true;
            AllowDebugControl = false;
            AllowBuildSystem = false;
            AllowBreakpoints = false;
            AllowConfiguration = false;
            ApiKey = "dev";
        }

        /// <summary>
        /// Enable all permissions (for first-run acceptance)
        /// </summary>
        public void EnableAllPermissions()
        {
            AllowCodeAnalysis = true;
            AllowObservability = true;
            AllowDebugControl = true;
            AllowBuildSystem = true;
            AllowBreakpoints = true;
            AllowConfiguration = true;
        }
    }
}
