using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace CadalogWebPlugin
{
    /// <summary>
    /// Toggles the Podium Browser web panel.
    /// Run in Rhino with: PodiumBrowser
    /// </summary>
    public sealed class PodiumBrowserCommand : Command
    {
        public override string EnglishName => "PodiumBrowser";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var panelId = WebPanel.PanelId;
            if (Panels.IsPanelVisible(panelId))
                Panels.ClosePanel(panelId);
            else
                Panels.OpenPanel(panelId);
            return Result.Success;
        }
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="PodiumBrowserCommand"/>.
    /// </summary>
    public sealed class CadalogPlantsCommand : Command
    {
        public override string EnglishName => "CadalogPlants";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var panelId = WebPanel.PanelId;
            if (Panels.IsPanelVisible(panelId))
                Panels.ClosePanel(panelId);
            else
                Panels.OpenPanel(panelId);
            return Result.Success;
        }
    }
}
