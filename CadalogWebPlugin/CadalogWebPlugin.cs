using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;

namespace CadalogWebPlugin
{
    /// <summary>
    /// Main plug-in entry point. Rhino discovers this class by reflection and
    /// instantiates a single instance per Rhino session.
    /// </summary>
    [Guid("8C9B3F6E-4E7A-4F2B-9B6E-1C3D2A5F7B10")]
    public sealed class CadalogWebPlugin : PlugIn
    {
        public CadalogWebPlugin()
        {
            Instance = this;
        }

        public static CadalogWebPlugin? Instance { get; private set; }

        // Load with Rhino so the toolbar is there on every launch, not only
        // after the first PodiumBrowser command.
        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // Register the dockable panel so it can be summoned by the command.
            Panels.RegisterPanel(
                this,
                typeof(WebPanel),
                "Podium Browser",
                null,
                PanelType.System);

            // After a fresh install, show the panel once. Rhino remembers where
            // the user docks it from then on, so this must not repeat.
            if (!Settings.GetBool(FirstRunKey, false))
                RhinoApp.Idle += OnFirstIdle;

            return LoadReturnCode.Success;
        }

        private const string FirstRunKey = "InitialPanelShown";

        private void OnFirstIdle(object? sender, EventArgs e)
        {
            RhinoApp.Idle -= OnFirstIdle;
            Settings.SetBool(FirstRunKey, true);
            if (!Panels.IsPanelVisible(WebPanel.PanelId))
                Panels.OpenPanel(WebPanel.PanelId);
        }
    }
}
