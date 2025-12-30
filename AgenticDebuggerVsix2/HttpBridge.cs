using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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

        // Change these if you want
        private const int Port = 27183;
        private const string ApiKeyHeader = "X-Api-Key";
        private const string DefaultApiKey = "dev"; // set header to override

        private readonly object _lock = new();
        private DebuggerSnapshot _lastSnapshot = new() { Mode = "Design" };

        internal HttpBridge(AsyncPackage package, DTE2 dte)
        {
            _package = package;
            _dte = dte;
        }

        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Prefixes.Add($"http://localhost:{Port}/");

            _listener.Start();
            _running = true;

            _thread = new System.Threading.Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AgenticDebuggerVsix.HttpBridge"
            };
            _thread.Start();

            WriteOutput($"Agentic Debugger Bridge listening on http://127.0.0.1:{Port}/ (ApiKey header {ApiKeyHeader}={DefaultApiKey})");
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
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
                catch (HttpListenerException)
                {
                    // listener stopped
                    break;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (ctx != null)
                            RespondJson(ctx.Response, new AgentResponse { Ok = false, Message = ex.ToString(), Snapshot = SafeSnapshot() }, 500);
                    }
                    catch { }
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

            if (ctx.Request.HttpMethod == "GET" && path == "/")
            {
                RespondText(ctx.Response, "Agentic Debugger Bridge OK. Endpoints: GET /state, POST /command", 200);
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/state")
            {
                RespondJson(ctx.Response, new AgentResponse { Ok = true, Message = "state", Snapshot = SafeSnapshot() }, 200);
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/command")
            {
                var body = ReadBody(ctx.Request);
                AgentCommand? cmd = null;
                try { cmd = JsonConvert.DeserializeObject<AgentCommand>(body); } catch { }

                if (cmd == null || string.IsNullOrWhiteSpace(cmd.Action))
                {
                    RespondJson(ctx.Response, new AgentResponse { Ok = false, Message = "Invalid command JSON", Snapshot = SafeSnapshot() }, 400);
                    return;
                }

                // Execute on UI thread
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

        private bool IsAuthorized(HttpListenerRequest req)
        {
            // Simple local safeguard. If you don't want this, remove it.
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
                        _dte.Debugger.Go(false);
                        return Ok($"Debugger.Go()");

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
                var thread = _dte.Debugger.CurrentThread;
                var frame = _dte.Debugger.CurrentStackFrame;

                // file/line
                try
                {
                    var fname = frame?.FunctionName;
                    // Attempt to get file + line from frame
                    var sf = thread?.StackFrames?.Item(1);
                    // Many times VS provides CurrentStackFrame with file info via FileName/LineNumber only in certain contexts.
                    // Use the current frame properties if available.
                }
                catch { }

                // stack
                try
                {
                    if (thread != null)
                    {
                        foreach (StackFrame f in thread.StackFrames)
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
    }
}
