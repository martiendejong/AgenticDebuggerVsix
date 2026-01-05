using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace AgenticDebuggerVsix
{
    /// <summary>
    /// Options page for Agentic Debugger permissions
    /// Accessible via Tools > Options > Agentic Debugger > Permissions
    /// </summary>
    [Guid("A9B3C1D2-E5F4-4A7B-9C8D-1E2F3A4B5C6D")]
    public class PermissionsOptionsPage : DialogPage
    {
        private PermissionsModel _permissions = new PermissionsModel();

        // Category: Read-Only Permissions (Safe)

        [Category("Read-Only Permissions (Safe)")]
        [DisplayName("Code Analysis")]
        [Description("Allow semantic code search, go-to-definition, find references, and document outline. (READ-ONLY - Safe)")]
        public bool AllowCodeAnalysis
        {
            get => _permissions.AllowCodeAnalysis;
            set => _permissions.AllowCodeAnalysis = value;
        }

        [Category("Read-Only Permissions (Safe)")]
        [DisplayName("Observability")]
        [Description("Allow reading debugger state, metrics, health, errors, projects, and output logs. (READ-ONLY - Safe)")]
        public bool AllowObservability
        {
            get => _permissions.AllowObservability;
            set => _permissions.AllowObservability = value;
        }

        // Category: Write Permissions (Requires Caution)

        [Category("Write Permissions (Requires Caution)")]
        [DisplayName("Debug Control")]
        [Description("Allow starting/stopping debugger, stepping through code, and controlling execution. (WRITE - Use with caution)")]
        public bool AllowDebugControl
        {
            get => _permissions.AllowDebugControl;
            set => _permissions.AllowDebugControl = value;
        }

        [Category("Write Permissions (Requires Caution)")]
        [DisplayName("Build System")]
        [Description("Allow triggering builds, rebuilds, and cleaning the solution. (WRITE - Use with caution)")]
        public bool AllowBuildSystem
        {
            get => _permissions.AllowBuildSystem;
            set => _permissions.AllowBuildSystem = value;
        }

        [Category("Write Permissions (Requires Caution)")]
        [DisplayName("Breakpoints")]
        [Description("Allow setting and clearing breakpoints in code. (WRITE - Use with caution)")]
        public bool AllowBreakpoints
        {
            get => _permissions.AllowBreakpoints;
            set => _permissions.AllowBreakpoints = value;
        }

        [Category("Write Permissions (Requires Caution)")]
        [DisplayName("Configuration")]
        [Description("Allow changing VS settings, evaluating expressions, and adding watches. (WRITE - Use with caution)")]
        public bool AllowConfiguration
        {
            get => _permissions.AllowConfiguration;
            set => _permissions.AllowConfiguration = value;
        }

        // Category: Advanced

        [Category("Advanced")]
        [DisplayName("API Key")]
        [Description("Custom API key for HTTP header authentication (default: 'dev')")]
        public string ApiKey
        {
            get => _permissions.ApiKey;
            set => _permissions.ApiKey = value;
        }

        /// <summary>
        /// Expose the internal permissions model
        /// </summary>
        public PermissionsModel GetPermissions()
        {
            return _permissions;
        }

        /// <summary>
        /// Reset to safe defaults
        /// </summary>
        public override void ResetSettings()
        {
            _permissions.ResetToDefaults();
            base.ResetSettings();
        }
    }
}
