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

        internal HttpBridge(AsyncPackage package, DTE2 dte)
        {
            _package = package;
            _dte = dte;
            _myId = Guid.NewGuid().ToString("N");
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
            if (!IsAuthorized(ctx.Request))
            {
                RespondText(ctx.Response, "Unauthorized", 401);
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            if (string.IsNullOrWhiteSpace(path)) path = "/";
            string method = ctx.Request.HttpMethod;

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

            // Public Endpoint: Get state (local)
            if (method == "GET" && path == "/state")
            {
                RespondJson(ctx.Response, new AgentResponse { Ok = true, Message = "state", Snapshot = SafeSnapshot() }, 200);
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
                     return;
                 }
                 
                 if (method == "POST" && path == "/register")
                 {
                     var body = ReadBody(ctx.Request);
                     try {
                         var info = JsonConvert.DeserializeObject<InstanceInfo>(body);
                         if (info != null) {
                             info.LastSeen = DateTime.UtcNow;
                             lock(_registry) {
                                 _registry.RemoveAll(x => x.Id == info.Id || x.Port == info.Port); // simple dedup
                                 _registry.Add(info);
                             }
                             RespondText(ctx.Response, "Registered", 200);
                         } else RespondText(ctx.Response, "Invalid", 400);
                     } catch { RespondText(ctx.Response, "Err", 400); }
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
                         ProxyRequest(ctx, targetId, method, suffix, method == "POST" ? ReadBody(ctx.Request) : null);
                         return;
                     }
                 }
            }

            if (method == "POST" && path == "/command")
            {
                var body = ReadBody(ctx.Request);
                AgentCommand? cmd = null;
                try { cmd = JsonConvert.DeserializeObject<AgentCommand>(body); } catch { }

                if (cmd == null || string.IsNullOrWhiteSpace(cmd.Action))
                {
                    RespondJson(ctx.Response, new AgentResponse { Ok = false, Message = "Invalid command JSON" }, 400);
                    return;
                }
                
                // Proxy check
                if (!string.IsNullOrEmpty(cmd.InstanceId) && cmd.InstanceId != _myId)
                {
                    if (_isPrimary)
                    {
                        ProxyRequest(ctx, cmd.InstanceId, "POST", "/command", body);
                        return;
                    }
                    else 
                    {
                        RespondJson(ctx.Response, new AgentResponse { Ok=false, Message="I am not Primary, cannot proxy." }, 400);
                        return;
                    }
                }

                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var result = Execute(cmd);
                    RespondJson(ctx.Response, result, result.Ok ? 200 : 400);
                });
                return;
            }

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
        }

        public void OnEnterRunMode(dbgEventReason Reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Run", null);
            SetSnapshot(snap);
        }

        public void OnExceptionThrown(string ExceptionType, string Name, int Code, string Description, ref dbgExceptionAction ExceptionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snap = CaptureSnapshot("Break", $"{ExceptionType}: {Description}".Trim());
            SetSnapshot(snap);
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
<html><body>
<h1>Agentic Debugger Bridge</h1>
<p><b>Port 27183 is the PRIMARY</b> controller. All other instances are registered as SECONDARY.</p>
<h2>Discovery</h2>
<p>Agentic Debugger writes a file to <code>%TEMP%\agentic_debugger.json</code> when running as Primary. Read this file to find the port.</p>
<h2>Endpoints</h2>
<ul>
<li>GET /instances : List connected VS instances (Primary only)</li>
<li>GET /state : Get state of THIS instance</li>
<li>POST /command : Execute command. { ""action"": ""start|stop|break|step..."" }</li>
</ul>

<h3>Observability</h3>
<ul>
<li>GET /errors : List items from Error List</li>
<li>GET /projects : List Solution Projects</li>
<li>GET /output : List Output Panes</li>
<li>GET /output/{name} : Get content of Output Pane (e.g. /output/Build)</li>
</ul>

<h2>Proxying</h2>
<p>To control a specific instance, pass <code>instanceId</code> in the command JSON or use <code>/proxy/{id}/...</code> endpoints.</p>

<h2>Swagger</h2>
<a href=""/swagger.json"">/swagger.json</a>
</body></html>";
        }
        
        private object GetSwaggerJson()
        {
            return new {
                openapi = "3.0.1",
                info = new { title = "Agentic Debugger API", version = "1.0" },
                paths = new {
                    _errors = new {
                        get = new {
                            summary = "Get Error List items",
                            responses = new { _200 = new { description = "OK" } }
                        }
                    },
                     _projects = new {
                        get = new {
                            summary = "Get Projects in Solution",
                            responses = new { _200 = new { description = "OK" } }
                        }
                    },
                    _output = new {
                        get = new {
                            summary = "List Output Panes",
                            responses = new { _200 = new { description = "OK" } }
                        }
                    }
                }
            };
        }
    }
}
