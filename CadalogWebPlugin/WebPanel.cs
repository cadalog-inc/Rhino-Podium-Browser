using System;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace CadalogWebPlugin
{
    /// <summary>
    /// A Rhino dockable panel that embeds the Podium Browser
    /// (PDM Plants &amp; Textures) library, with a switch between the Rhino
    /// (.3dm) and SketchUp (.skp) catalogs.
    /// </summary>
    [Guid("F1B2C3D4-5E6F-4A7B-8C9D-0E1F2A3B4C5D")]
    public sealed class WebPanel : Panel
    {
        public static Guid PanelId => typeof(WebPanel).GUID;

        private readonly WebView _webView;
        private readonly DropDown _catalog;
        private bool _suppressChange;

        public WebPanel()
        {
            var homeUrl = PluginConfig.GetHomeUrl();

            _webView = new WebView { Url = new Uri(homeUrl) };

            // Hooks CoreWebView2 so downloads from the site are staged and
            // imported into the active document.
            WebViewBridge.Attach(_webView);

            _catalog = new DropDown { ToolTip = "Which Podium Browser catalog to browse" };
            foreach (var source in PluginConfig.Sources)
                _catalog.Items.Add(new ListItem { Text = source.Label, Key = source.Url });

            // -1 when a dev/override URL is in play, so we don't imply a catalog.
            _suppressChange = true;
            _catalog.SelectedIndex = Array.FindIndex(
                PluginConfig.Sources, s => PluginConfig.SameUrl(s.Url, homeUrl));
            _suppressChange = false;

            _catalog.SelectedIndexChanged += OnCatalogChanged;

            var bar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Padding = new Padding(6, 6, 6, 0),
                Items =
                {
                    new Label { Text = "Catalog:" },
                    new StackLayoutItem(_catalog, expand: true),
                },
            };

            Content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Spacing = 6,
                Items =
                {
                    bar,
                    new StackLayoutItem(_webView, expand: true),
                },
            };
        }

        private void OnCatalogChanged(object? sender, EventArgs e)
        {
            if (_suppressChange) return;

            var url = (_catalog.SelectedValue as ListItem)?.Key;
            if (string.IsNullOrWhiteSpace(url)) return;

            RhinoApp.WriteLine($"[Cadalog] switching catalog: {url}");
            _webView.Url = new Uri(url);

            if (!PluginConfig.SavePreferredUrl(url))
                RhinoApp.WriteLine("[Cadalog] couldn't save the catalog choice; it resets next launch.");
            else if (PluginConfig.HasEnvOverride)
                RhinoApp.WriteLine("[Cadalog] note: CADALOG_PODIUM_URL is set and wins on next launch.");
        }
    }
}
