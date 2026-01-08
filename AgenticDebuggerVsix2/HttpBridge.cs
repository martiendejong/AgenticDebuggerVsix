using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace AgenticDebuggerVsix
{
    internal sealed class HttpBridge
    {
        private readonly AsyncPackage _package;
        private readonly DTE2 _dte;
        private HttpListener? _listener;
        private System.Threading.Thread? _thread;
        private volatile bool _running;

        // Configuration
        private const int PrimaryPort = 27183;
        private const string PrimaryUrl = "http://localhost:27183";
        private const string ApiKeyHeader = "X-Api-Key";
        private const string DefaultApiKey = "dev";
        private const string DiscoveryFileName = "agentic_debugger.json";

        // State
        private bool _isPrimary;
        private int _localPort;
        private readonly string _myId;
        private readonly object _lock = new();
        private DebuggerSnapshot _lastSnapshot = new() { Mode = "Design" };
        
        // Primary only: Registry of connected instances
        private readonly List<InstanceInfo> _registry = new();
        private readonly HttpClient _httpClient = new(); // For proxying

        // Metrics collection
        private readonly MetricsCollector _metrics = new();

        // Request logging
        private readonly RequestLogger _logger = new();

        // WebSocket support
        private WebSocketHandler _wsHandler;

        // Roslyn code analysis
        private RoslynBridge _roslynBridge;

        // Permissions - dynamically reloaded
        private PermissionsModel _permissions;
        private readonly AgenticDebuggerPackage _agenticPackage;

        internal HttpBridge(AsyncPackage package, DTE2 dte, PermissionsModel permissions)
        {
            _package = package;
            _agenticPackage = package as AgenticDebuggerPackage;
            _dte = dte;
            _permissions = permissions ?? new PermissionsModel();
            _myId = Guid.NewGuid().ToString("N");
            _wsHandler = new WebSocketHandler(_metrics);
        }

        /// <summary>
        /// Reload permissions from options page (for dynamic permission changes)
        /// </summary>
        private PermissionsModel GetCurrentPermissions()
        {
            if (_agenticPackage != null)
            {
                return _agenticPackage.GetPermissions();
            }
            return _permissions; // Fallback to cached
        }

        internal void SetRoslynBridge(RoslynBridge roslynBridge)
        {
            _roslynBridge = roslynBridge;
        }

        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            _listener = new HttpListener();
            bool bound = false;

            // 1. Try to bind Primary Port
            try 
            {
                _listener.Prefixes.Add($"http://127.0.0.1:{PrimaryPort}/");
                _listener.Prefixes.Add($"http://localhost:{PrimaryPort}/");
                _listener.Start();
                bound = true;
                _isPrimary = true;
                _localPort = PrimaryPort;
                WriteOutput($"Agentic Debugger: I am PRIMARY (Bridge). Listening on port {PrimaryPort}.");
                
                // Add myself to registry and write discovery file
                UpdateMyInfoInRegistry();
                WriteDiscoveryFile();
            }
            catch 
            {
                // Failed to bind primary, we are secondary
                _listener = new HttpListener(); // reset
                _isPrimary = false;
            }

            // 2. If not primary, bind random port
            if (!bound)
            {
                _localPort = GetRandomFreePort();
                _listener.Prefixes.Add($"http://127.0.0.1:{_localPort}/");
                _listener.Prefixes.Add($"http://localhost:{_localPort}/");
                try 
                {
                    _listener.Start();
                    bound = true;
                    WriteOutput($"Agentic Debugger: I am SECONDARY. Listening on port {_localPort}.");
                    
                    // Register with Primary
                    Task.Run(RegisterWithPrimary);
                }
                catch (Exception ex)
                {
                    WriteOutput($"Agentic Debugger: Failed to start listener. {ex.Message}");
                    return;
                }
            }

            _running = true;
            _thread = new System.Threading.Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AgenticDebuggerVsix.HttpBridge"
            };
            _thread.Start();
            
            // If primary, start a cleanup loop for registry
            if (_isPrimary)
            {
                 Task.Run(RegistryCleanupLoop);
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }

            try { _wsHandler?.CloseAll(); } catch { }

            if (_isPrimary)
            {
                DeleteDiscoveryFile();
            }
        }

        private void WriteDiscoveryFile()
        {
            try
            {
                var info = new 
                {
                    port = PrimaryPort,
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    apiKeyHeader = ApiKeyHeader,
                    defaultApiKey = DefaultApiKey
                };
                var json = JsonConvert.SerializeObject(info, Formatting.Indented);
                var path = Path.Combine(Path.GetTempPath(), DiscoveryFileName);
                File.WriteAllText(path, json);
                WriteOutput($"Discovery file written to: {path}");
            }
            catch (Exception ex)
            {
                WriteOutput($"Failed to write discovery file: {ex.Message}");
            }
        }
        
        private void DeleteDiscoveryFile()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), DiscoveryFileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private int GetRandomFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private async Task RegisterWithPrimary()
        {
            while (_running)
            {
                try
                {
                    var info = await GetMyInstanceInfoAsync(); 
                    var json = JsonConvert.SerializeObject(info);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    try 
                    {
                        await _httpClient.PostAsync($"{PrimaryUrl}/register", content);
                    }
                    catch { /* Primary might be down or starting */ }
                }
                catch { }

                await Task.Delay(5000); // Heartbeat every 5s
            }
        }
        
        private async Task<InstanceInfo> GetMyInstanceInfoAsync()
        {
             await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
             string slnName = "(No Solution)";
             try {
                if (_dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                    slnName = Path.GetFileName(_dte.Solution.FullName);
             } catch {}
             
             return new InstanceInfo 
             {
                 Id = _myId,
                 Pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                 Port = _localPort,
                 SolutionName = slnName
             };
        }
        
        // Run on UI thread
        private void UpdateMyInfoInRegistry()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string slnName = "(No Solution)";
             try {
                if (_dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                    slnName = Path.GetFileName(_dte.Solution.FullName);
             } catch {}
             
             var me = new InstanceInfo 
             {
                 Id = _myId,
                 Pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                 Port = _localPort,
                 SolutionName = slnName
             };
             
             lock(_registry)
             {
                 _registry.RemoveAll(x => x.Id == _myId);
                 _registry.Add(me);
             }
        }

        private async Task RegistryCleanupLoop()
        {
            while(_running)
            {
                lock(_registry)
                {
                    // Remove stale > 15s (myself is always fresh)
                     _registry.RemoveAll(x => x.Id != _myId && (DateTime.UtcNow - x.LastSeen).TotalSeconds > 15);
                     
                     // Keep myself updated
                }
                // Refresh my own info? Needs UI thread. 
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateMyInfoInRegistry();
                await Task.Delay(5000);
            }
        }

        private void ListenLoop()
        {
            while (_running && _listener != null && _listener.IsListening)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = _listener.GetContext();
                    Handle(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    try { if (ctx != null) RespondJson(ctx.Response, new AgentResponse { Ok = false, Message = ex.ToString() }, 500); } catch { }
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (string.IsNullOrWhiteSpace(path)) path = "/";
            string method = ctx.Request.HttpMethod;

            // Read body once for both logging and processing
            string requestBody = method == "POST" ? ReadBody(ctx.Request) : null;

            // Start logging
            var logEntry = _logger.StartRequest(method, path, requestBody);

            long elapsedMs = 0;
            bool isError = false;
            string responseBody = "";
            int statusCode = 200;

            using (new RequestTimer(ms => elapsedMs = ms))
            {
                try
                {
                    if (!IsAuthorized(ctx.Request))
                    {
                        responseBody = "Unauthorized";
                        statusCode = 401;
                        RespondText(ctx.Response, responseBody, statusCode);
                        isError = true;
                        return;
                    }

                    HandleRequest(ctx, path, method, requestBody, out isError);
                    statusCode = ctx.Response.StatusCode;
                }
                catch (Exception ex)
                {
                    isError = true;
                    statusCode = 500;
                    responseBody = ex.Message;
                }
                finally
                {
                    _metrics.RecordRequest(path, elapsedMs, isError);
                    _logger.CompleteRequest(logEntry, responseBody, statusCode, elapsedMs);
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx, string path, string method, string requestBody, out bool isError)
        {
            isError = false; // Default assignment - will be updated if error occurs
            bool localIsError = false;

            // Permission check (after auth, before routing)
            // Extract action from request body for command/batch endpoints
            string action = null;
            if ((path == "/command" || path == "/batch") && method == "POST" && !string.IsNullOrEmpty(requestBody))
            {
                try
                {
                    var cmdObj = JsonConvert.DeserializeObject<dynamic>(requestBody);
                    action = cmdObj?.action ?? cmdObj?.commands?[0]?.action;
                }
                catch { }
            }

            if (!IsPermissionGranted(method, path, action, out string permissionError))
            {
                localIsError = true;
                var response = new AgentResponse
                {
                    Ok = false,
                    Message = permissionError ?? "Permission denied"
                };
                RespondJson(ctx.Response, response, 403);
                return;
            }

            // WebSocket upgrade
            if (ctx.Request.IsWebSocketRequest && path == "/ws")
            {
                // Handle WebSocket asynchronously - don't block HTTP thread
                Task.Run(async () =>
                {
                    try
                    {
                        await _wsHandler.HandleWebSocketRequest(ctx);
                    }
                    catch (Exception) { }
                });
                return;
            }

            if (method == "GET" && path == "/")
            {
                RespondText(ctx.Response, "Agentic Debugger Bridge OK. See /docs for usage.", 200);
                return;
            }
            
            if (method == "GET" && path == "/docs")
            {
                RespondHtml(ctx.Response, GetDocsHtml(), 200);
                return;
            }
            
            if (method == "GET" && path == "/swagger.json")
            {
                RespondJson(ctx.Response, GetSwaggerJson(), 200);
                return;
            }

            // Metrics and health endpoints
            if (method == "GET" && path == "/metrics")
            {
                int instanceCount = 0;
                lock(_registry) { instanceCount = _registry.Count; }
                var metrics = _metrics.GetMetrics(instanceCount);
                RespondJson(ctx.Response, metrics, 200);
                return;
            }

            if (method == "GET" && path == "/health")
            {
                var health = _metrics.GetHealth();
                int statusCode_health = health.Status == "OK" ? 200 : health.Status == "Degraded" ? 503 : 503;
                RespondJson(ctx.Response, health, statusCode_health);
                return;
            }

            // Logging endpoints
            if (method == "GET" && path == "/logs")
            {
                // Parse query parameters for filtering
                var logs = _logger.GetLogs(limit: 100); // Default to last 100
                RespondJson(ctx.Response, logs, 200);
                return;
            }

            if (method == "GET" && path.StartsWith("/logs/"))
            {
                var logId = path.Substring("/logs/".Length);
                var log = _logger.GetLogById(logId);
                if (log == null)
                {
                    localIsError = true;
                    RespondText(ctx.Response, "Log entry not found", 404);
                }
                else
                {
                    RespondJson(ctx.Response, log, 200);
                }
                return;
            }

            if (method == "DELETE" && path == "/logs")
            {
                _logger.Clear();
                RespondText(ctx.Response, "Logs cleared", 200);
                return;
            }

            // Public Endpoint: Get state (local)
            if (method == "GET" && path == "/state")
            {
                RespondJson(ctx.Response, new AgentResponse { Ok = true, Message = "state", Snapshot = SafeSnapshot() }, 200);
                return;
            }

            // Status endpoint - shows permissions and basic info
            if (method == "GET" && path == "/status")
            {
                var mode = "Unknown";
                try
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () => {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        var debugMode = _dte.Debugger.CurrentMode;
                        mode = debugMode switch
                        {
                            dbgDebugMode.dbgBreakMode => "Break",
                            dbgDebugMode.dbgRunMode => "Run",
                            dbgDebugMode.dbgDesignMode => "Design",
                            _ => "Unknown"
                        };
                    });
                }
                catch { }

                // Reload permissions dynamically for accurate status reporting
                var currentPermissions = GetCurrentPermissions();

                var status = new
                {
                    version = "1.1",
                    extensionName = "Agentic Debugger Bridge",
                    currentMode = mode,
                    isPrimary = _isPrimary,
                    port = _localPort,
                    permissions = new
                    {
                        codeAnalysis = currentPermissions.AllowCodeAnalysis,
                        observability = currentPermissions.AllowObservability,
                        debugControl = currentPermissions.AllowDebugControl,
                        buildSystem = currentPermissions.AllowBuildSystem,
                        breakpoints = currentPermissions.AllowBreakpoints,
                        configuration = currentPermissions.AllowConfiguration
                    },
                    authentication = new
                    {
                        headerName = ApiKeyHeader,
                        requiresKey = true
                    },
                    capabilities = new[]
                    {
                        "websocket",
                        "batch-commands",
                        "roslyn-analysis",
                        "multi-instance",
                        "real-time-notifications"
                    }
                };

                RespondJson(ctx.Response, status, 200);
                return;
            }

            // New Observability Endpoints
            if (method == "GET" && path == "/errors")
            {
                ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var errors = GetErrorItems();
                    RespondJson(ctx.Response, errors, 200);
                });
                return;
            }
            
            if (method == "GET" && path == "/projects")
            {
                ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var projects = GetProjects();
                    RespondJson(ctx.Response, projects, 200);
                });
                return;
            }
            
            if (method == "GET" && path == "/output")
            {
                 ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var panes = GetOutputPanes();
                    RespondJson(ctx.Response, panes, 200);
                });
                return;
            }
            
            if (method == "GET" && path.StartsWith("/output/"))
            {
                var paneName = path.Substring("/output/".Length);
                ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var content = GetOutputPaneContent(paneName);
                    if (content == null) RespondText(ctx.Response, "Pane not found", 404);
                    else RespondText(ctx.Response, content, 200);
                });
                return;
            }
            
            // Primary Endpoints
            if (_isPrimary)
            {
                 if (method == "GET" && path == "/instances")
                 {
                     lock(_registry) {
                         RespondJson(ctx.Response, _registry, 200);
                     }
                     isError = localIsError;
                     return;
                 }
                 
                 if (method == "POST" && path == "/register")
                 {
                     try {
                         var info = JsonConvert.DeserializeObject<InstanceInfo>(requestBody);
                         if (info != null) {
                             info.LastSeen = DateTime.UtcNow;
                             lock(_registry) {
                                 _registry.RemoveAll(x => x.Id == info.Id || x.Port == info.Port); // simple dedup
                                 _registry.Add(info);
                             }
                             RespondText(ctx.Response, "Registered", 200);
                         } else RespondText(ctx.Response, "Invalid", 400);
                     } catch { RespondText(ctx.Response, "Err", 400); }
                     isError = localIsError;
                     return;
                 }

                 // Proxy State: /proxy/{id}/...
                 if (path.StartsWith("/proxy/"))
                 {
                     var segments = path.Split('/');
                     // /proxy/ID/suffix... -> segments: "", "proxy", "ID", "suffix" ...
                     if (segments.Length >= 4)
                     {
                         var targetId = segments[2];
                         var suffix = "/" + string.Join("/", segments.Skip(3));
                         ProxyRequest(ctx, targetId, method, suffix, method == "POST" ? requestBody : null);
                         isError = localIsError;
                         return;
                     }
                 }
            }

            if (method == "POST" && path == "/command")
            {
                AgentCommand? cmd = null;
                try { cmd = JsonConvert.DeserializeObject<AgentCommand>(requestBody); } catch { }

                if (cmd == null || string.IsNullOrWhiteSpace(cmd.Action))
                {
                    localIsError = true;
                    RespondJson(ctx.Response, new AgentResponse { Ok = false, Message = "Invalid command JSON" }, 400);
                    isError = localIsError;
                    return;
                }

                // Record command metric
                _metrics.RecordCommand(cmd.Action);

                // Proxy check
                if (!string.IsNullOrEmpty(cmd.InstanceId) && cmd.InstanceId != _myId)
                {
                    if (_isPrimary)
                    {
                        ProxyRequest(ctx, cmd.InstanceId, "POST", "/command", requestBody);
                        isError = localIsError;
                        return;
                    }
                    else
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new AgentResponse { Ok=false, Message="I am not Primary, cannot proxy." }, 400);
                        isError = localIsError;
                        return;
                    }
                }

                bool cmdIsError = false;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var result = Execute(cmd);
                    if (!result.Ok) cmdIsError = true;
                    RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                });
                localIsError = cmdIsError;
                isError = localIsError;
                return;
            }

            // Batch command endpoint
            if (method == "POST" && path == "/batch")
            {
                BatchCommand? batchCmd = null;
                try { batchCmd = JsonConvert.DeserializeObject<BatchCommand>(requestBody); } catch { }

                if (batchCmd == null || batchCmd.Commands == null || batchCmd.Commands.Count == 0)
                {
                    localIsError = true;
                    RespondJson(ctx.Response, new BatchResponse { Ok = false }, 400);
                    isError = localIsError;
                    return;
                }

                bool batchIsError = false;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var batchResult = ExecuteBatch(batchCmd);
                    if (!batchResult.Ok) batchIsError = true;
                    RespondJson(ctx.Response, batchResult, batchResult.Ok ? 200 : 400);
                });
                localIsError = batchIsError;
                isError = localIsError;
                return;
            }

            // Configure endpoint
            if (method == "POST" && path == "/configure")
            {
                ConfigureRequest? config = null;
                try { config = JsonConvert.DeserializeObject<ConfigureRequest>(requestBody); } catch { }

                if (config == null)
                {
                    localIsError = true;
                    RespondJson(ctx.Response, new ConfigureResponse { Ok = false, Message = "Invalid configuration JSON" }, 400);
                    isError = localIsError;
                    return;
                }

                bool configIsError = false;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var configResult = ApplyConfiguration(config);
                    if (!configResult.Ok) configIsError = true;
                    RespondJson(ctx.Response, configResult, configResult.Ok ? 200 : 400);
                });
                localIsError = configIsError;
                isError = localIsError;
                return;
            }

            // Roslyn Code Analysis Endpoints
            if (_roslynBridge != null)
            {
                // Symbol search
                if (method == "POST" && path == "/code/symbols")
                {
                    SymbolSearchRequest? searchReq = null;
                    try { searchReq = JsonConvert.DeserializeObject<SymbolSearchRequest>(requestBody); } catch { }

                    if (searchReq == null)
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new SymbolSearchResponse { Ok = false }, 400);
                        isError = localIsError;
                        return;
                    }

                    bool symbolsIsError = false;
                    Task.Run(async () =>
                    {
                        var result = await _roslynBridge.SearchSymbolsAsync(searchReq);
                        if (!result.Ok) symbolsIsError = true;
                        RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                    }).Wait();
                    localIsError = symbolsIsError;
                    isError = localIsError;
                    return;
                }

                // Go to definition
                if (method == "POST" && path == "/code/definition")
                {
                    DefinitionRequest? defReq = null;
                    try { defReq = JsonConvert.DeserializeObject<DefinitionRequest>(requestBody); } catch { }

                    if (defReq == null)
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new DefinitionResponse { Ok = false, Message = "Invalid request" }, 400);
                        isError = localIsError;
                        return;
                    }

                    bool defIsError = false;
                    Task.Run(async () =>
                    {
                        var result = await _roslynBridge.GoToDefinitionAsync(defReq);
                        if (!result.Ok) defIsError = true;
                        RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                    }).Wait();
                    localIsError = defIsError;
                    isError = localIsError;
                    return;
                }

                // Find references
                if (method == "POST" && path == "/code/references")
                {
                    ReferencesRequest? refReq = null;
                    try { refReq = JsonConvert.DeserializeObject<ReferencesRequest>(requestBody); } catch { }

                    if (refReq == null)
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new ReferencesResponse { Ok = false, Message = "Invalid request" }, 400);
                        isError = localIsError;
                        return;
                    }

                    bool refsIsError = false;
                    Task.Run(async () =>
                    {
                        var result = await _roslynBridge.FindReferencesAsync(refReq);
                        if (!result.Ok) refsIsError = true;
                        RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                    }).Wait();
                    localIsError = refsIsError;
                    isError = localIsError;
                    return;
                }

                // Document outline
                if (method == "GET" && path.StartsWith("/code/outline"))
                {
                    // Parse file from query string
                    var query = ctx.Request.Url.Query;
                    var filePath = ParseQueryStringParameter(query, "file");

                    if (string.IsNullOrEmpty(filePath))
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new DocumentOutlineResponse { Ok = false }, 400);
                        isError = localIsError;
                        return;
                    }

                    bool outlineIsError = false;
                    Task.Run(async () =>
                    {
                        var result = await _roslynBridge.GetDocumentOutlineAsync(filePath);
                        if (!result.Ok) outlineIsError = true;
                        RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                    }).Wait();
                    localIsError = outlineIsError;
                    isError = localIsError;
                    return;
                }

                // Semantic info
                if (method == "POST" && path == "/code/semantic")
                {
                    SemanticInfoRequest? semReq = null;
                    try { semReq = JsonConvert.DeserializeObject<SemanticInfoRequest>(requestBody); } catch { }

                    if (semReq == null)
                    {
                        localIsError = true;
                        RespondJson(ctx.Response, new SemanticInfoResponse { Ok = false, Message = "Invalid request" }, 400);
                        isError = localIsError;
                        return;
                    }

                    bool semIsError = false;
                    Task.Run(async () =>
                    {
                        var result = await _roslynBridge.GetSemanticInfoAsync(semReq);
                        if (!result.Ok) semIsError = true;
                        RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                    }).Wait();
                    localIsError = semIsError;
                    isError = localIsError;
                    return;
                }
            }

            localIsError = true;
            isError = localIsError;
            RespondText(ctx.Response, "Not found", 404);
        }
        
        // Helper methods for Observability
        private List<ErrorItemModel> GetErrorItems()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = new List<ErrorItemModel>();
            try 
            {
               var errorList = _dte.ToolWindows.ErrorList;
                if (errorList != null)
                {
                    for (int i = 1; i <= errorList.ErrorItems.Count; i++)
                    {
                        var item = errorList.ErrorItems.Item(i);
                        list.Add(new ErrorItemModel
                        {
                            Description = item.Description,
                            File = item.FileName,
                            Line = item.Line,
                            Project = item.Project,
                            ErrorLevel = item.ErrorLevel.ToString() 
                        });
                        if (list.Count > 100) break; // Limit
                    }
                }
            }
            catch {}
            return list;
        }
        
        private List<ProjectModel> GetProjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = new List<ProjectModel>();
             try 
            {
                foreach(Project p in _dte.Solution.Projects)
                {
                    list.Add(new ProjectModel 
                    {
                        Name = p.Name,
                        UniqueName = p.UniqueName,
                        FullPath = p.FullName
                    });
                }
            }
            catch {}
            return list;
        }
        
        private List<OutputPaneModel> GetOutputPanes()
        {
             ThreadHelper.ThrowIfNotOnUIThread();
             var list = new List<OutputPaneModel>();
             try {
                 foreach(OutputWindowPane p in _dte.ToolWindows.OutputWindow.OutputWindowPanes)
                 {
                     list.Add(new OutputPaneModel { Name = p.Name, Guid = p.Guid });
                 }
             } catch {}
             return list;
        }
        
        private string? GetOutputPaneContent(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try {
                foreach(OutputWindowPane p in _dte.ToolWindows.OutputWindow.OutputWindowPanes)
                {
                    if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        var doc = p.TextDocument;
                        var sel = doc.Selection;
                        sel.SelectAll();
                        return sel.Text;
                    }
                }
            } catch {}
            return null;
        }

        private void ProxyRequest(HttpListenerContext ctx, string targetId, string method, string remotePath, string? body)
        {
            InstanceInfo? target = null;
            lock(_registry) target = _registry.FirstOrDefault(r => r.Id == targetId);
            
            if (target == null)
            {
                RespondJson(ctx.Response, new AgentResponse{Ok=false, Message="Target instance not found"}, 404);
                return;
            }
            
            if (target.Id == _myId)
            {
                 RespondJson(ctx.Response, new AgentResponse{Ok=false, Message="Routing Error: Target is self"}, 500);
                 return;
            }
            
            try 
            {
                string url = $"http://localhost:{target.Port}{remotePath}";
                var req = new HttpRequestMessage(new HttpMethod(method), url);
                req.Headers.Add(ApiKeyHeader, DefaultApiKey);
                if (body != null)
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }
                
                var resp = _httpClient.SendAsync(req).Result;
                var respStr = resp.Content.ReadAsStringAsync().Result;
                
                var bytes = Encoding.UTF8.GetBytes(respStr);
                ctx.Response.StatusCode = (int)resp.StatusCode;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                RespondJson(ctx.Response, new AgentResponse{Ok=false, Message=$"Proxy Error: {ex.Message}"}, 502);
            }
        }

        private bool IsAuthorized(HttpListenerRequest req)
        {
            var apiKey = req.Headers[ApiKeyHeader];
            if (string.IsNullOrEmpty(apiKey)) apiKey = DefaultApiKey;
            return string.Equals(apiKey, DefaultApiKey, StringComparison.Ordinal);
        }

        private bool IsPermissionGranted(string method, string path, string action, out string errorMessage)
        {
            // Reload permissions dynamically to pick up changes from options dialog
            var currentPermissions = GetCurrentPermissions();

            if (currentPermissions.IsEndpointAllowed(method, path, action))
            {
                errorMessage = null;
                return true;
            }

            errorMessage = currentPermissions.GetPermissionDeniedMessage(method, path, action);
            return false;
        }

        private BatchResponse ExecuteBatch(BatchCommand batchCmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var response = new BatchResponse
            {
                TotalCommands = batchCmd.Commands.Count,
                Results = new List<AgentResponse>()
            };

            foreach (var cmd in batchCmd.Commands)
            {
                _metrics.RecordCommand(cmd.Action);

                var result = Execute(cmd);
                response.Results.Add(result);

                if (result.Ok)
                {
                    response.SuccessCount++;
                }
                else
                {
                    response.FailureCount++;
                    if (batchCmd.StopOnError)
                    {
                        response.Ok = false;
                        return response;
                    }
                }
            }

            response.Ok = response.FailureCount == 0;
            return response;
        }

        private ConfigureResponse ApplyConfiguration(ConfigureRequest config)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var response = new ConfigureResponse
            {
                AppliedMode = config.Mode,
                Settings = new Dictionary<string, string>()
            };

            try
            {
                if (config.Mode.Equals("agent", StringComparison.OrdinalIgnoreCase))
                {
                    // Agent mode: Minimize blocking UI operations

                    // Note: SolutionBuild.SuppressUI is not available in the DTE automation model
                    // Build UI suppression would require alternative approaches
                    if (config.SuppressWarnings)
                    {
                        response.Settings["SuppressBuildUI"] = "not supported in DTE automation";
                    }

                    // Auto-save documents
                    if (config.AutoSave)
                    {
                        try
                        {
                            // Set auto-load changed files
                            var props = _dte.Properties["Environment", "Documents"];
                            if (props != null)
                            {
                                props.Item("AutoloadChangedFiles").Value = true;
                                response.Settings["AutoloadChangedFiles"] = "true";
                            }
                        }
                        catch (Exception ex)
                        {
                            response.Settings["AutoloadChangedFiles"] = $"failed: {ex.Message}";
                        }
                    }

                    response.Ok = true;
                    response.Message = "Agent mode configured successfully";
                }
                else if (config.Mode.Equals("human", StringComparison.OrdinalIgnoreCase))
                {
                    // Human mode: Restore normal behavior
                    response.Settings["SuppressBuildUI"] = "not supported in DTE automation";

                    response.Ok = true;
                    response.Message = "Human mode configured successfully";
                }
                else
                {
                    response.Ok = false;
                    response.Message = $"Unknown mode: {config.Mode}. Use 'agent' or 'human'.";
                }
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Message = $"Configuration failed: {ex.Message}";
            }

            return response;
        }

        private AgentResponse Execute(AgentCommand cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var act = cmd.Action.Trim().ToLowerInvariant();
                switch (act)
                {
                    case "go":
                    case "continue":
                        if (_dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
                        {
                             _dte.Solution.SolutionBuild.Debug();
                             return Ok("Started Debugging");
                        }
                        _dte.Debugger.Go(false);
                        return Ok("Debugger.Go()");
                    
                    case "start":
                        if (!string.IsNullOrEmpty(cmd.ProjectName))
                        {
                            foreach(Project p in _dte.Solution.Projects)
                            {
                                if (p.Name == cmd.ProjectName)
                                {
                                    _dte.Solution.SolutionBuild.StartupProjects = p.UniqueName;
                                    break;
                                }
                            }
                        }
                        _dte.Solution.SolutionBuild.Debug();
                        return Ok("Started Debugging");

                    case "stop":
                        _dte.Debugger.Stop(false);
                        return Ok("Stopped Debugging");
                        
                    case "break":
                    case "pause":
                        _dte.Debugger.Break(false);
                        return Ok("Debugger.Break()");

                    case "stepinto":
                        _dte.Debugger.StepInto(false);
                        return Ok("StepInto");

                    case "stepover":
                        _dte.Debugger.StepOver(false);
                        return Ok("StepOver");

                    case "stepout":
                        _dte.Debugger.StepOut(false);
                        return Ok("StepOut");
                        
                    case "clean":
                        _dte.Solution.SolutionBuild.Clean(true);
                        return Ok("Solution Cleaned");
                        
                    case "build":
                    case "rebuild":
                        if (act == "rebuild")
                        {
                             _dte.Solution.SolutionBuild.Clean(true);
                             _dte.Solution.SolutionBuild.Build(true);
                             return Ok("Solution Rebuild Triggered");
                        }
                        else 
                        {
                            _dte.Solution.SolutionBuild.Build(true);
                            return Ok("Solution Build Triggered");
                        }

                    case "setbreakpoint":
                    case "bp":
                        if (string.IsNullOrWhiteSpace(cmd.File) || cmd.Line == null || cmd.Line <= 0)
                            return Fail("setBreakpoint requires {file, line}");

                        _dte.Debugger.Breakpoints.Add(File: cmd.File, Line: cmd.Line.Value);
                        return Ok($"Breakpoint added: {cmd.File}:{cmd.Line}");

                    case "clearbreakpoints":
                    case "clrbp":
                        foreach (Breakpoint bp in _dte.Debugger.Breakpoints)
                            bp.Delete();
                        return Ok("All breakpoints cleared");

                    case "eval":
                        if (string.IsNullOrWhiteSpace(cmd.Expression))
                            return Fail("eval requires {expression}");

                        var expr = _dte.Debugger.GetExpression(cmd.Expression, true, 1);
                        var val = expr?.Value ?? "(null)";
                        UpdateSnapshotNotes($"eval {cmd.Expression} => {val}");
                        return Ok($"eval ok: {val}");

                    case "addwatch":
                        if (string.IsNullOrWhiteSpace(cmd.Expression))
                            return Fail("addWatch requires {expression}");
                        Window watchWindow = _dte.Windows.Item(EnvDTE.Constants.vsWindowKindWatch);
                        watchWindow.Activate();

                        dynamic watch = watchWindow.Object;
                        watch.WatchItems.Add(cmd.Expression);
                        return Ok($"Watch added: {cmd.Expression}");

                    default:
                        return Fail($"Unknown action: {cmd.Action}");
                }
            }
            catch (Exception ex)
            {
                return new AgentResponse { Ok = false, Message = ex.ToString(), Snapshot = SafeSnapshot() };
            }
        }

        private AgentResponse Ok(string msg) => new AgentResponse { Ok = true, Message = msg, Snapshot = SafeSnapshot() };
        private AgentResponse Fail(string msg) => new AgentResponse { Ok = false, Message = msg, Snapshot = SafeSnapshot() };

        private string ReadBody(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            return sr.ReadToEnd();
        }

        /// <summary>
        /// Parse query string parameter without System.Web.HttpUtility dependency
        /// </summary>
        private string ParseQueryStringParameter(string queryString, string paramName)
        {
            if (string.IsNullOrEmpty(queryString))
                return null;

            // Remove leading '?' if present
            if (queryString.StartsWith("?"))
                queryString = queryString.Substring(1);

            var pairs = queryString.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2 && parts[0].Equals(paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }
            return null;
        }

        private void RespondText(HttpListenerResponse resp, string text, int status)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            resp.StatusCode = status;
            resp.ContentType = "text/plain; charset=utf-8";
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }
        
        private void RespondHtml(HttpListenerResponse resp, string html, int status)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            resp.StatusCode = status;
            resp.ContentType = "text/html; charset=utf-8";
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }

        private void RespondJson(HttpListenerResponse resp, object obj, int status)
        {
            var json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.StatusCode = status;
            resp.ContentType = "application/json; charset=utf-8";
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }

        private DebuggerSnapshot SafeSnapshot()
        {
            if (ThreadHelper.JoinableTaskFactory.Context.IsOnMainThread)
            {
                return CaptureLiveSnapshotWithOverrides();
            }

            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return CaptureLiveSnapshotWithOverrides();
            });
        }

        private void SetSnapshot(DebuggerSnapshot snap)
        {
            lock (_lock) { _lastSnapshot = snap; }
        }

        private void UpdateSnapshotNotes(string notes)
        {
            lock (_lock)
            {
                _lastSnapshot.Notes = notes;
                _lastSnapshot.TimestampUtc = DateTime.UtcNow;
            }
        }

        // Debugger event handlers
        public void OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Break", null);
            SetSnapshot(snap);
            _wsHandler?.BroadcastStateChange(snap);
        }

        public void OnEnterRunMode(dbgEventReason Reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Run", null);
            SetSnapshot(snap);
            _wsHandler?.BroadcastStateChange(snap);
        }

        public void OnExceptionThrown(string ExceptionType, string Name, int Code, string Description, ref dbgExceptionAction ExceptionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Break", $"{ExceptionType}: {Description}".Trim());
            SetSnapshot(snap);
            _wsHandler?.BroadcastStateChange(snap);
        }

        public void OnEnterDesignMode(dbgEventReason Reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Design", "Debugging stopped");
            SetSnapshot(snap);
            _wsHandler?.BroadcastStateChange(snap);
        }

        private DebuggerSnapshot CaptureSnapshot(string mode, string? exception)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var snap = new DebuggerSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Mode = mode,
                Exception = exception
            };

            try
            {
                 // Solution Info
                 if (_dte.Solution != null && _dte.Solution.IsOpen)
                 {
                     snap.SolutionName = Path.GetFileName(_dte.Solution.FullName);
                     snap.SolutionPath = _dte.Solution.FullName;
                     try {
                         var sb = _dte.Solution.SolutionBuild;
                         if (sb != null && sb.StartupProjects != null)
                         {
                             // StartupProjects is object[] of unique names
                             var sups = sb.StartupProjects as object[];
                             if (sups != null && sups.Length > 0)
                                snap.StartupProject = sups[0] as string;
                         }
                     } catch {}
                 }
                
                var thread = _dte.Debugger.CurrentThread;
                var frame = _dte.Debugger.CurrentStackFrame;

                try {} catch { } // file/line from stack

                // stack
                try
                {
                    if (thread != null)
                    {
                        foreach (EnvDTE.StackFrame f in thread.StackFrames)
                        {
                            var fn = f.FunctionName ?? "(unknown)";
                            snap.Stack.Add(fn);
                        }
                    }
                }
                catch { }

                // locals
                try
                {
                    if (frame != null)
                    {
                        foreach (Expression e in frame.Locals)
                        {
                            var n = e.Name ?? "(noname)";
                            var v = e.Value ?? "";
                            if (!snap.Locals.ContainsKey(n))
                                snap.Locals[n] = v;
                        }
                    }
                }
                catch { }

                // file/line best-effort via TextSelection at current statement
                try
                {
                    var doc = _dte.ActiveDocument;
                    if (doc != null && doc.FullName != null)
                    {
                        var sel = doc.Selection as TextSelection;
                        if (sel != null)
                        {
                            snap.File = doc.FullName;
                            snap.Line = sel.ActivePoint.Line;
                        }
                    }
                }
                catch { }

            }
            catch { }

            return snap;
        }

        private DebuggerSnapshot CaptureLiveSnapshotWithOverrides()
        {
            var snap = CaptureLiveSnapshot();
            string? notes;
            string? exception;
            lock (_lock)
            {
                notes = _lastSnapshot.Notes;
                exception = _lastSnapshot.Exception;
            }

            if (!string.IsNullOrWhiteSpace(notes))
                snap.Notes = notes;
            if (!string.IsNullOrWhiteSpace(exception))
                snap.Exception = exception;

            return snap;
        }

        private DebuggerSnapshot CaptureLiveSnapshot()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var mode = "Unknown";
            try
            {
                var debugMode = _dte.Debugger.CurrentMode;
                mode = debugMode switch
                {
                    dbgDebugMode.dbgBreakMode => "Break",
                    dbgDebugMode.dbgRunMode => "Run",
                    dbgDebugMode.dbgDesignMode => "Design",
                    _ => "Unknown"
                };
            }
            catch { }

            return CaptureSnapshot(mode, null);
        }

        private void WriteOutput(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _dte.ToolWindows.OutputWindow.OutputWindowPanes.Add("Agentic Debugger Bridge").OutputString($"{message}\n");
            }
            catch
            {
                // ignore
            }
        }
        
        private string GetDocsHtml()
        {
            return @"
<html><head><style>
body{font-family:system-ui,sans-serif;max-width:1200px;margin:20px auto;padding:0 20px;line-height:1.6}
h1{border-bottom:2px solid #0078d4;padding-bottom:10px}
h2{color:#0078d4;margin-top:30px}
h3{color:#106ebe;margin-top:20px}
code{background:#f0f0f0;padding:2px 6px;border-radius:3px}
pre{background:#f5f5f5;padding:10px;border-left:3px solid #0078d4;overflow-x:auto}
ul{margin:10px 0}
li{margin:5px 0}
.endpoint{font-weight:bold;color:#107c10}
.method-get{color:#0078d4}
.method-post{color:#ca5010}
.method-delete{color:#d13438}
.method-ws{color:#8764b8}
</style></head><body>
<h1>🤖 Agentic Debugger Bridge API</h1>
<p><strong>Version:</strong> 1.1 | <strong>Port:</strong> 27183 (Primary) | <strong>Protocol:</strong> HTTP + WebSocket</p>

<h2>📡 Discovery</h2>
<p>The bridge writes <code>%TEMP%\agentic_debugger.json</code> with connection details:</p>
<pre>{""port"":27183,""pid"":1234,""keyHeader"":""X-Api-Key"",""defaultKey"":""dev""}</pre>

<h2>🔒 Authentication</h2>
<p>All HTTP requests require: <code>X-Api-Key: dev</code> header</p>

<h2>🌐 Core Endpoints</h2>
<ul>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/</span> - API status check</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/docs</span> - This documentation</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/swagger.json</span> - OpenAPI 3.0 specification</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/status</span> - Get version, mode, and enabled permissions</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/state</span> - Get debugger state (mode, stack, locals, file/line)</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/instances</span> - List all VS instances (Primary only)</li>
</ul>

<h2>🔒 Permissions & Security</h2>
<p>The API uses a permission-based security model. By default, only <strong>read-only</strong> operations are enabled.</p>
<h3>Default Permissions</h3>
<ul>
<li>✅ <strong>Code Analysis</strong> - Semantic search, definitions, references (Enabled)</li>
<li>✅ <strong>Observability</strong> - Read state, metrics, errors, logs (Enabled)</li>
<li>❌ <strong>Debug Control</strong> - Start/stop, step, control execution (Disabled)</li>
<li>❌ <strong>Build System</strong> - Trigger builds, rebuilds, clean (Disabled)</li>
<li>❌ <strong>Breakpoints</strong> - Set/clear breakpoints (Disabled)</li>
<li>❌ <strong>Configuration</strong> - Change settings, eval expressions (Disabled)</li>
</ul>
<p>Configure permissions in: <strong>Tools > Options > Agentic Debugger > Permissions</strong></p>
<p>Check current permissions: <code>GET /status</code></p>

<h2>🐛 Debugger Control</h2>
<h3>POST /command</h3>
<p>Execute debugger/build commands. JSON body with <code>action</code> field:</p>
<ul>
<li><strong>Debug:</strong> start, go, continue, stop, break, pause</li>
<li><strong>Step:</strong> stepInto, stepOver, stepOut</li>
<li><strong>Build:</strong> clean, build, rebuild</li>
<li><strong>Breakpoints:</strong> setBreakpoint, clearBreakpoints</li>
<li><strong>Eval:</strong> eval, addWatch</li>
</ul>
<p><strong>Fields:</strong> action (required), projectName, file, line, expression, condition, instanceId</p>
<pre>{""action"":""start"",""projectName"":""MyApp""}</pre>

<h2>⚡ Batch Operations</h2>
<ul>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/batch</span> - Execute multiple commands in one request (10x faster)</li>
</ul>
<pre>{""commands"":[{""action"":""setBreakpoint"",""file"":""C:\\Code\\Program.cs"",""line"":42},{""action"":""start""}],""stopOnError"":true}</pre>

<h2>🔍 Code Analysis (Roslyn)</h2>
<ul>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/code/symbols</span> - Search symbols (classes, methods, properties)</li>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/code/definition</span> - Go to definition at file position</li>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/code/references</span> - Find all references to symbol</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/code/outline?file={path}</span> - Get document structure</li>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/code/semantic</span> - Get semantic info (type, docs) at position</li>
</ul>
<p><strong>Example - Symbol Search:</strong></p>
<pre>{""query"":""Customer"",""kind"":""Class"",""maxResults"":50}</pre>
<p><strong>Example - Go to Definition:</strong></p>
<pre>{""file"":""C:\\Code\\Program.cs"",""line"":42,""column"":15}</pre>

<h2>📊 Observability</h2>
<ul>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/metrics</span> - Performance metrics (requests, latency, errors, commands)</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/health</span> - Health status (OK/Degraded/Down)</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/logs</span> - Recent request/response logs (last 100)</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/logs/{id}</span> - Specific log entry</li>
<li><span class=""method-delete"">DELETE</span> <span class=""endpoint"">/logs</span> - Clear all logs</li>
</ul>

<h2>📋 Solution Information</h2>
<ul>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/errors</span> - Build errors/warnings from Error List</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/projects</span> - List projects in solution</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/output</span> - List Output window panes</li>
<li><span class=""method-get"">GET</span> <span class=""endpoint"">/output/{name}</span> - Get Output pane content (e.g., /output/Build)</li>
</ul>

<h2>⚙️ Configuration</h2>
<ul>
<li><span class=""method-post"">POST</span> <span class=""endpoint"">/configure</span> - Configure agent/human mode, suppress warnings, auto-save</li>
</ul>
<pre>{""mode"":""agent"",""suppressWarnings"":true,""autoSave"":true}</pre>

<h2>🔌 Real-Time WebSocket</h2>
<ul>
<li><span class=""method-ws"">WS</span> <span class=""endpoint"">/ws</span> - WebSocket connection for push notifications (&lt;100ms latency)</li>
</ul>
<p><strong>Events:</strong> <code>connected</code>, <code>stateChange</code> (breakpoint hit, exception, debug stop), <code>pong</code></p>
<p><strong>Connect:</strong> <code>ws://localhost:27183/ws</code> (no auth header needed for WS)</p>

<h2>🔀 Multi-Instance Proxying</h2>
<p>Control specific VS instances by including <code>instanceId</code> in command JSON, or use proxy endpoints:</p>
<ul>
<li><span class=""endpoint"">/proxy/{id}/state</span> - Get state of specific instance</li>
<li><span class=""endpoint"">/proxy/{id}/command</span> - Send command to specific instance</li>
</ul>

<h2>📚 Full API Specification</h2>
<p>For complete OpenAPI 3.0 specification with all schemas and examples:</p>
<p><a href=""/swagger.json"">📄 /swagger.json</a></p>

<hr>
<p><em>Built for next-generation AI-assisted software development. Enable agents to see and control Visual Studio programmatically.</em></p>
</body></html>";
        }
        
        private object GetSwaggerJson()
        {
            // Comprehensive OpenAPI 3.0 specification for AI agent discovery
            var spec = new {
                openapi = "3.0.1",
                info = new {
                    title = "Agentic Debugger Bridge API",
                    version = "1.1.0",
                    description = "HTTP + WebSocket API for AI agents to control Visual Studio debugger, build system, and perform semantic code analysis via Roslyn. Uses permission-based security model with safe defaults (read-only operations enabled, write operations disabled)."
                },
                servers = new[] {
                    new { url = "http://localhost:27183", description = "Primary Bridge (default port)" }
                },
                components = new {
                    securitySchemes = new {
                        ApiKeyAuth = new {
                            type = "apiKey",
                            name = "X-Api-Key",
                            @in = "header",
                            description = "API key for authentication. Default: 'dev'. Found in %TEMP%\\agentic_debugger.json"
                        }
                    },
                    schemas = new {
                        // Core models
                        DebuggerSnapshot = new {
                            type = "object",
                            description = "Current state of debugger with stack, locals, and position",
                            properties = new {
                                timestampUtc = new { type = "string", format = "date-time" },
                                mode = new { type = "string", @enum = new[] { "Design", "Run", "Break" }, description = "Debugger mode" },
                                exception = new { type = "string", nullable = true },
                                file = new { type = "string", nullable = true },
                                line = new { type = "integer", nullable = true },
                                stack = new { type = "array", items = new { type = "string" }, description = "Call stack frames" },
                                locals = new { type = "object", additionalProperties = new { type = "string" }, description = "Local variables" },
                                notes = new { type = "string", nullable = true },
                                solutionName = new { type = "string", nullable = true },
                                solutionPath = new { type = "string", nullable = true },
                                startupProject = new { type = "string", nullable = true }
                            }
                        },
                        AgentResponse = new {
                            type = "object",
                            properties = new {
                                ok = new { type = "boolean", description = "Success flag" },
                                message = new { type = "string", description = "Status or error message" },
                                snapshot = new { @ref = "#/components/schemas/DebuggerSnapshot", nullable = true }
                            }
                        },
                        AgentCommand = new {
                            type = "object",
                            required = new[] { "action" },
                            properties = new {
                                action = new { type = "string", description = "Command action: start, stop, break, stepInto, stepOver, stepOut, build, rebuild, clean, setBreakpoint, clearBreakpoints, eval, etc." },
                                file = new { type = "string", nullable = true, description = "File path for setBreakpoint" },
                                line = new { type = "integer", nullable = true, description = "Line number for setBreakpoint" },
                                expression = new { type = "string", nullable = true, description = "Expression for eval/addWatch" },
                                condition = new { type = "string", nullable = true, description = "Breakpoint condition" },
                                instanceId = new { type = "string", nullable = true, description = "Target specific VS instance" },
                                projectName = new { type = "string", nullable = true, description = "Project name for start command" }
                            },
                            example = new { action = "start", projectName = "MyApp" }
                        },
                        // Batch operations
                        BatchCommand = new {
                            type = "object",
                            properties = new {
                                commands = new { type = "array", items = new { @ref = "#/components/schemas/AgentCommand" }, description = "Array of commands to execute" },
                                stopOnError = new { type = "boolean", @default = true, description = "Stop execution on first error" }
                            }
                        },
                        BatchResponse = new {
                            type = "object",
                            properties = new {
                                ok = new { type = "boolean" },
                                results = new { type = "array", items = new { @ref = "#/components/schemas/AgentResponse" } },
                                successCount = new { type = "integer" },
                                failureCount = new { type = "integer" },
                                totalCommands = new { type = "integer" }
                            }
                        },
                        // Roslyn Code Analysis models
                        SymbolInfo = new {
                            type = "object",
                            description = "Information about a code symbol (class, method, property, etc.)",
                            properties = new {
                                name = new { type = "string" },
                                kind = new { type = "string", description = "Symbol kind: Class, Method, Property, Field, etc." },
                                containerName = new { type = "string", nullable = true, description = "Containing type or namespace" },
                                file = new { type = "string", nullable = true },
                                line = new { type = "integer", nullable = true },
                                column = new { type = "integer", nullable = true },
                                summary = new { type = "string", nullable = true, description = "XML documentation summary" }
                            }
                        },
                        CodeLocation = new {
                            type = "object",
                            properties = new {
                                file = new { type = "string" },
                                line = new { type = "integer" },
                                column = new { type = "integer" },
                                endLine = new { type = "integer", nullable = true },
                                endColumn = new { type = "integer", nullable = true }
                            }
                        },
                        SymbolSearchRequest = new {
                            type = "object",
                            required = new[] { "query" },
                            properties = new {
                                query = new { type = "string", description = "Search term" },
                                kind = new { type = "string", nullable = true, description = "Filter by symbol kind (Class, Method, etc.)" },
                                maxResults = new { type = "integer", @default = 50 }
                            }
                        },
                        SymbolSearchResponse = new {
                            type = "object",
                            properties = new {
                                ok = new { type = "boolean" },
                                results = new { type = "array", items = new { @ref = "#/components/schemas/SymbolInfo" } },
                                totalFound = new { type = "integer" }
                            }
                        },
                        DefinitionRequest = new {
                            type = "object",
                            required = new[] { "file", "line", "column" },
                            properties = new {
                                file = new { type = "string" },
                                line = new { type = "integer" },
                                column = new { type = "integer" }
                            }
                        },
                        DefinitionResponse = new {
                            type = "object",
                            properties = new {
                                ok = new { type = "boolean" },
                                message = new { type = "string" },
                                symbol = new { @ref = "#/components/schemas/SymbolInfo", nullable = true },
                                location = new { @ref = "#/components/schemas/CodeLocation", nullable = true }
                            }
                        },
                        // Configuration
                        ConfigureRequest = new {
                            type = "object",
                            properties = new {
                                mode = new { type = "string", @enum = new[] { "agent", "human" }, @default = "human" },
                                suppressWarnings = new { type = "boolean", @default = false },
                                autoSave = new { type = "boolean", @default = false }
                            }
                        },
                        ConfigureResponse = new {
                            type = "object",
                            properties = new {
                                ok = new { type = "boolean" },
                                message = new { type = "string" },
                                appliedMode = new { type = "string" },
                                settings = new { type = "object", additionalProperties = new { type = "string" } }
                            }
                        },
                        // Observability
                        Metrics = new {
                            type = "object",
                            description = "Performance metrics",
                            properties = new {
                                startTime = new { type = "string", format = "date-time" },
                                uptime = new { type = "string" },
                                totalRequests = new { type = "integer", format = "int64" },
                                totalErrors = new { type = "integer", format = "int64" },
                                averageResponseTimeMs = new { type = "number", format = "double" },
                                activeWebSocketConnections = new { type = "integer" },
                                endpointCounts = new { type = "object", additionalProperties = new { type = "integer" } },
                                commandCounts = new { type = "object", additionalProperties = new { type = "integer" } },
                                instanceCount = new { type = "integer" }
                            }
                        },
                        HealthStatus = new {
                            type = "object",
                            properties = new {
                                status = new { type = "string", @enum = new[] { "OK", "Degraded", "Down" } },
                                uptime = new { type = "string" },
                                timestamp = new { type = "string", format = "date-time" },
                                details = new { type = "object", additionalProperties = new { type = "string" } }
                            }
                        },
                        // Other models
                        ErrorItem = new {
                            type = "object",
                            properties = new {
                                description = new { type = "string" },
                                file = new { type = "string", nullable = true },
                                line = new { type = "integer" },
                                project = new { type = "string", nullable = true },
                                errorLevel = new { type = "string", @enum = new[] { "Error", "Warning" } }
                            }
                        },
                        Project = new {
                            type = "object",
                            properties = new {
                                name = new { type = "string" },
                                uniqueName = new { type = "string" },
                                fullPath = new { type = "string", nullable = true }
                            }
                        },
                        InstanceInfo = new {
                            type = "object",
                            properties = new {
                                id = new { type = "string" },
                                pid = new { type = "integer" },
                                port = new { type = "integer" },
                                solutionName = new { type = "string" },
                                lastSeen = new { type = "string", format = "date-time" }
                            }
                        }
                    }
                },
                security = new[] { new { ApiKeyAuth = Array.Empty<string>() } },
                paths = new Dictionary<string, object> {
                    ["/"] = new { get = new { summary = "API status check", tags = new[] { "Core" }, responses = new { _200 = new { description = "OK" } } } },
                    ["/docs"] = new { get = new { summary = "API documentation (HTML)", tags = new[] { "Core" }, responses = new { _200 = new { description = "HTML documentation" } } } },
                    ["/swagger.json"] = new { get = new { summary = "OpenAPI specification", tags = new[] { "Core" }, responses = new { _200 = new { description = "OpenAPI 3.0 JSON" } } } },
                    ["/status"] = new { get = new { summary = "Get extension status and permissions", tags = new[] { "Core" }, description = "Returns extension version, current debugger mode, and enabled permission categories. Always allowed regardless of permissions.", responses = new { _200 = new { description = "Status information", content = new { application_json = new { schema = new { type = "object" } } } } } } },
                    ["/state"] = new { get = new { summary = "Get debugger state", tags = new[] { "Debugger" }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/AgentResponse" } } } } } } },
                    ["/command"] = new { post = new { summary = "Execute debugger/build command", tags = new[] { "Debugger" }, requestBody = new { required = true, content = new { application_json = new { schema = new { @ref = "#/components/schemas/AgentCommand" } } } }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/AgentResponse" } } } } } } },
                    ["/batch"] = new { post = new { summary = "Execute multiple commands (10x faster)", tags = new[] { "Debugger" }, requestBody = new { required = true, content = new { application_json = new { schema = new { @ref = "#/components/schemas/BatchCommand" } } } }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/BatchResponse" } } } } } } },
                    ["/code/symbols"] = new { post = new { summary = "Search symbols across solution", tags = new[] { "Roslyn" }, requestBody = new { required = true, content = new { application_json = new { schema = new { @ref = "#/components/schemas/SymbolSearchRequest" } } } }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/SymbolSearchResponse" } } } } } } },
                    ["/code/definition"] = new { post = new { summary = "Go to definition", tags = new[] { "Roslyn" }, requestBody = new { required = true, content = new { application_json = new { schema = new { @ref = "#/components/schemas/DefinitionRequest" } } } }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/DefinitionResponse" } } } } } } },
                    ["/code/references"] = new { post = new { summary = "Find all references", tags = new[] { "Roslyn" }, description = "Find all references to symbol at position", responses = new { _200 = new { description = "OK" } } } },
                    ["/code/outline"] = new { get = new { summary = "Get document structure", tags = new[] { "Roslyn" }, parameters = new[] { new { name = "file", @in = "query", required = true, schema = new { type = "string" } } }, responses = new { _200 = new { description = "OK" } } } },
                    ["/code/semantic"] = new { post = new { summary = "Get semantic info at position", tags = new[] { "Roslyn" }, description = "Get type, documentation for symbol at position", responses = new { _200 = new { description = "OK" } } } },
                    ["/metrics"] = new { get = new { summary = "Performance metrics", tags = new[] { "Observability" }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/Metrics" } } } } } } },
                    ["/health"] = new { get = new { summary = "Health status", tags = new[] { "Observability" }, responses = new { _200 = new { description = "Healthy", content = new { application_json = new { schema = new { @ref = "#/components/schemas/HealthStatus" } } } }, _503 = new { description = "Degraded or Down" } } } },
                    ["/logs"] = new { get = new { summary = "Get recent request logs", tags = new[] { "Observability" }, responses = new { _200 = new { description = "OK" } } }, delete = new { summary = "Clear all logs", tags = new[] { "Observability" }, responses = new { _200 = new { description = "OK" } } } },
                    ["/configure"] = new { post = new { summary = "Configure agent/human mode", tags = new[] { "Configuration" }, requestBody = new { required = true, content = new { application_json = new { schema = new { @ref = "#/components/schemas/ConfigureRequest" } } } }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { @ref = "#/components/schemas/ConfigureResponse" } } } } } } },
                    ["/errors"] = new { get = new { summary = "Get build errors/warnings", tags = new[] { "Solution" }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { type = "array", items = new { @ref = "#/components/schemas/ErrorItem" } } } } } } } },
                    ["/projects"] = new { get = new { summary = "Get projects in solution", tags = new[] { "Solution" }, responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { type = "array", items = new { @ref = "#/components/schemas/Project" } } } } } } } },
                    ["/instances"] = new { get = new { summary = "List VS instances", tags = new[] { "Multi-Instance" }, description = "Primary only - list all connected instances", responses = new { _200 = new { description = "OK", content = new { application_json = new { schema = new { type = "array", items = new { @ref = "#/components/schemas/InstanceInfo" } } } } } } } }
                },
                tags = new[] {
                    new { name = "Core", description = "Core API endpoints" },
                    new { name = "Debugger", description = "Debugger control and state" },
                    new { name = "Roslyn", description = "Code analysis and semantic navigation" },
                    new { name = "Observability", description = "Metrics, health, and logging" },
                    new { name = "Configuration", description = "Agent/human mode configuration" },
                    new { name = "Solution", description = "Solution information (errors, projects)" },
                    new { name = "Multi-Instance", description = "Multi-instance management" }
                }
            };
            return spec;
        }
    }
}
