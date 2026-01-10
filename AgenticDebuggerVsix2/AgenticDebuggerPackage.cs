using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.LanguageServices;

namespace AgenticDebuggerVsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Agentic Debugger Bridge", "Local HTTP bridge for agentic debugging", "0.1")]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(PermissionsOptionsPage), "Agentic Debugger", "Permissions", 0, 0, true)]
    public sealed class AgenticDebuggerPackage : AsyncPackage
    {
        public const string PackageGuidString = "1c5b3c47-4d41-40f7-bf8a-cdb6c0f7f9a1";

        private HttpBridge? _bridge;
        internal DTE2? Dte { get; private set; }

        internal PermissionsModel GetPermissions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var optionsPage = (PermissionsOptionsPage)GetDialogPage(typeof(PermissionsOptionsPage));
                if (optionsPage != null)
                {
                    // Ensure settings are loaded from storage
                    // This is critical - GetDialogPage may return a page that hasn't loaded yet
                    optionsPage.LoadSettingsFromStorage();
                    return optionsPage.GetPermissions();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgenticDebugger] Failed to load permissions: {ex.Message}");
            }

            // Fallback: return safe defaults
            return new PermissionsModel();
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            if (Dte == null) return;

            // Start bridge
            var permissions = GetPermissions();
            _bridge = new HttpBridge(this, Dte, permissions);
            _bridge.Start();

            // Show first-run info if needed
            await ShowFirstRunInfoIfNeededAsync();

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

        private async Task ShowFirstRunInfoIfNeededAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Check if user has seen welcome message
                var settingsManager = new ShellSettingsManager(this);
                var store = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                const string collectionName = "AgenticDebugger";
                const string propertyName = "HasSeenWelcome";

                // Create collection if it doesn't exist
                if (!store.CollectionExists(collectionName))
                {
                    store.CreateCollection(collectionName);
                }

                // Check if welcome has been shown
                bool hasSeenWelcome = false;
                if (store.PropertyExists(collectionName, propertyName))
                {
                    hasSeenWelcome = store.GetBoolean(collectionName, propertyName);
                }

                if (!hasSeenWelcome)
                {
                    // Show MessageBox (simpler fallback instead of info bar)
                    ShowPermissionsDialog();

                    // Mark as seen
                    store.SetBoolean(collectionName, propertyName, true);
                }
            }
            catch (Exception ex)
            {
                // Don't block initialization if settings fail
                System.Diagnostics.Debug.WriteLine($"First-run check failed: {ex}");
            }
        }

        private void ShowPermissionsDialog()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var result = MessageBox.Show(
                    "Agentic Debugger requires permissions to function.\n\n" +
                    "The extension can:\n" +
                    "  • Control debugging (start, stop, step through code)\n" +
                    "  • Set and clear breakpoints\n" +
                    "  • Trigger builds\n" +
                    "  • Analyze code semantics\n" +
                    "  • Read debugger state and errors\n\n" +
                    "Enable all permissions now?\n\n" +
                    "(You can change these later in Tools > Options > Agentic Debugger)",
                    "Agentic Debugger - First Run",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    // Enable all permissions immediately
                    var optionsPage = (PermissionsOptionsPage)GetDialogPage(typeof(PermissionsOptionsPage));
                    if (optionsPage != null)
                    {
                        optionsPage.EnableAllPermissions();
                        optionsPage.SaveSettingsToStorage();

                        System.Diagnostics.Debug.WriteLine("[AgenticDebugger] All permissions enabled on first run");

                        // Show confirmation
                        MessageBox.Show(
                            "All permissions have been enabled!\n\n" +
                            "The Agentic Debugger is now fully operational.\n" +
                            "You can modify permissions anytime in:\n" +
                            "Tools > Options > Agentic Debugger > Permissions",
                            "Agentic Debugger",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                }
                else
                {
                    // User declined - show how to enable later
                    System.Diagnostics.Debug.WriteLine("[AgenticDebugger] User declined permissions on first run");

                    MessageBox.Show(
                        "Permissions were not enabled.\n\n" +
                        "Only read-only operations (Code Analysis, Observability) will be available.\n\n" +
                        "To enable full functionality later:\n" +
                        "Tools > Options > Agentic Debugger > Permissions",
                        "Agentic Debugger",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                // Fallback: Just log, don't crash
                System.Diagnostics.Debug.WriteLine($"Permission dialog failed: {ex}");
            }
        }
    }
}
