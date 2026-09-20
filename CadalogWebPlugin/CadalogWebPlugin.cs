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

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // Register the dockable panel so it can be summoned by the command.
            Panels.RegisterPanel(
                this,
                typeof(WebPanel),
                "Podium Browser",
                null,
                PanelType.System);

            return LoadReturnCode.Success;
        }
    }
}
