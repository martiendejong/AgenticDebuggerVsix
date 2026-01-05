using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.LanguageServices;

namespace AgenticDebuggerVsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Agentic Debugger Bridge", "Local HTTP bridge for agentic debugging", "0.1")]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class AgenticDebuggerPackage : AsyncPackage
    {
        public const string PackageGuidString = "1c5b3c47-4d41-40f7-bf8a-cdb6c0f7f9a1";

        private HttpBridge? _bridge;
        internal DTE2? Dte { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            if (Dte == null) return;

            // Start bridge
            _bridge = new HttpBridge(this, Dte);
            _bridge.Start();

            // Initialize Roslyn integration
            try
            {
                var workspace = await GetServiceAsync(typeof(VisualStudioWorkspace)) as VisualStudioWorkspace;
                if (workspace != null)
                {
                    var roslynBridge = new RoslynBridge(workspace);
                    _bridge.SetRoslynBridge(roslynBridge);
                }
            }
            catch
            {
                // Roslyn integration optional - bridge still works without it
            }

            // Wire debugger events after VS startup completes so debugger subsystem exists
            try
            {
                Dte.Events.DTEEvents.OnStartupComplete += () =>
                {
                    void AttachEvents()
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        var events = Dte.Events as Events2;
                        var dbgEvents = events?.DebuggerEvents;
                        if (dbgEvents != null)
                        {
                            dbgEvents.OnEnterBreakMode += _bridge.OnEnterBreakMode;
                            dbgEvents.OnEnterRunMode += _bridge.OnEnterRunMode;
                            dbgEvents.OnEnterDesignMode += _bridge.OnEnterDesignMode;
                            dbgEvents.OnExceptionThrown += _bridge.OnExceptionThrown;
                        }
                    }

                    if (ThreadHelper.JoinableTaskFactory.Context.IsOnMainThread)
                    {
                        AttachEvents();
                    }
                    else
                    {
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            AttachEvents();
                        });
                    }
                };
            }
            catch
            {
                // If events fail, bridge still works on polling via /state
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _bridge?.Stop();
            }
            catch { }
            base.Dispose(disposing);
        }
    }
}
