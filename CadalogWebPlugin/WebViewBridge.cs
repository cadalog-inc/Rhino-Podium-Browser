using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Eto.Forms;
using Microsoft.Web.WebView2.Core;
using Rhino;

namespace CadalogWebPlugin
{
    /// <summary>
    /// Hooks the underlying CoreWebView2 inside an Eto WebView so the plug-in
    /// can receive <c>chrome.webview.postMessage</c> calls from the page
    /// (used to drive the Insert flow).
    /// </summary>
    internal static class WebViewBridge
    {
        public static void Attach(WebView etoWebView)
        {
            if (etoWebView == null) throw new ArgumentNullException(nameof(etoWebView));

            RhinoApp.WriteLine("[Cadalog] WebViewBridge.Attach()");

            // Eto's Wpf/WinForms WebView2 handlers initialize CoreWebView2
            // asynchronously. The exact moment it becomes available varies, so
            // hook every event Eto exposes and run our idempotent installer
            // on each one. The HashSet in EnsureBridgeInstalledAsync guarantees
            // we only register once per CoreWebView2 instance.
            async void TryInstall(string trigger)
            {
                try
                {
                    await EnsureBridgeInstalledAsync(etoWebView, trigger);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[Cadalog] bridge install failed ({trigger}): {ex.Message}");
                }
            }

            etoWebView.DocumentLoading    += (_, __) => TryInstall("DocumentLoading");
            etoWebView.DocumentLoaded     += (_, __) => TryInstall("DocumentLoaded");
            etoWebView.Navigated          += (_, __) => TryInstall("Navigated");

            // Also try immediately and once again on the UI loop in case the
            // CoreWebView2 is already up by the time we attach.
            TryInstall("immediate");
            Eto.Forms.Application.Instance.AsyncInvoke(() => TryInstall("async"));
        }

        private static Task EnsureBridgeInstalledAsync(WebView etoWebView, string trigger)
        {
            var coreWebView2 = TryGetCoreWebView2(etoWebView);
            if (coreWebView2 == null)
            {
                // Not ready yet; another event will retry.
                return Task.CompletedTask;
            }

            // Prevent reinstalling on every event / navigation.
            lock (_installed)
            {
                if (_installed.Contains(coreWebView2)) return Task.CompletedTask;
                _installed.Add(coreWebView2);
            }

            RhinoApp.WriteLine($"[Cadalog] installing bridge (trigger={trigger})");

            // Make sure web -> host messaging is enabled.
            try { coreWebView2.Settings.IsWebMessageEnabled = true; } catch { /* older builds */ }

            coreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    var json = args.WebMessageAsJson;
                    if (!string.IsNullOrEmpty(json))
                    {
                        RhinoApp.WriteLine($"[Cadalog] page msg: {json}");
                        AssetImporter.HandleWebMessage(json);
                    }
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[Cadalog] message handler error: {ex.Message}");
                }
            };

            // The REAL Podium Browser site never postMessages. When a user clicks
            // download it points window.location at
            //   https://v4.pdm-plants-textures.com/.secret/files/{hash}.{ext}
            // which is served as application/octet-stream, so WebView2 turns it
            // into a download. We intercept that, redirect it into our staging
            // folder, and import it into the active document when it finishes.
            try
            {
                coreWebView2.DownloadStarting += OnDownloadStarting;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Cadalog] DownloadStarting unavailable: {ex.Message}");
            }

            RhinoApp.WriteLine("[Cadalog] bridge installed");
            return Task.CompletedTask;
        }

        // Redirects a WebView2 download into our staging folder and imports the
        // resulting file into Rhino once the transfer completes.
        private static void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                var op = e.DownloadOperation;
                var uri = op.Uri ?? string.Empty;
                RhinoApp.WriteLine($"[Cadalog] download starting: {uri}");

                var fileName = AssetImporter.FileNameFromUrl(uri, e.ResultFilePath);
                var stagingDir = Path.Combine(
                    PluginConfig.DownloadStagingDir,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                Directory.CreateDirectory(stagingDir);

                e.ResultFilePath = Path.Combine(stagingDir, fileName);
                e.Handled = true; // suppress WebView2's built-in download UI

                op.StateChanged += (_, __) =>
                {
                    switch (op.State)
                    {
                        case CoreWebView2DownloadState.Completed:
                            RhinoApp.WriteLine($"[Cadalog] download complete: {op.ResultFilePath}");
                            RhinoApp.InvokeOnUiThread(new Action(
                                () => AssetImporter.ImportLocalFile(op.ResultFilePath)));
                            break;
                        case CoreWebView2DownloadState.Interrupted:
                            RhinoApp.WriteLine($"[Cadalog] download interrupted: {op.InterruptReason}");
                            break;
                    }
                };
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Cadalog] DownloadStarting handler error: {ex.Message}");
            }
        }

        // Tracks CoreWebView2 instances we've already hooked so we don't
        // double-register listeners across navigations.
        private static readonly System.Collections.Generic.HashSet<CoreWebView2> _installed = new();

        private static bool _typesDumped;

        private static CoreWebView2? TryGetCoreWebView2(WebView etoWebView)
        {
            var handler = etoWebView.Handler;
            if (handler == null) return null;

            if (!_typesDumped)
            {
                _typesDumped = true;
                RhinoApp.WriteLine($"[Cadalog] handler type: {handler.GetType().FullName}");
            }

            // Different Eto handlers expose the underlying WebView2 differently:
            //   - Eto.Wpf:        handler.WebView2     (Microsoft.Web.WebView2.Wpf.WebView2)
            //   - Eto.WinForms:   handler.WebView2     (Microsoft.Web.WebView2.WinForms.WebView2)
            //   - generic:        handler.Control / handler.ControlObject
            var candidates = new[] { "WebView2", "Control", "ControlObject", "Browser", "NativeControl" };
            foreach (var name in candidates)
            {
                var prop = handler.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                var val = prop?.GetValue(handler);
                if (val == null) continue;
                var core = ExtractCoreWebView2(val);
                if (core != null) return core;
            }

            // Last resort: scan all properties for a CoreWebView2.
            foreach (var prop in handler.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                object? val = null;
                try { val = prop.GetValue(handler); } catch { continue; }
                if (val == null) continue;
                var core = ExtractCoreWebView2(val);
                if (core != null) return core;
            }

            return null;
        }

        private static CoreWebView2? ExtractCoreWebView2(object obj)
        {
            if (obj is CoreWebView2 c) return c;
            var coreProp = obj.GetType().GetProperty("CoreWebView2",
                BindingFlags.Public | BindingFlags.Instance);
            if (coreProp == null) return null;
            try { return coreProp.GetValue(obj) as CoreWebView2; }
            catch { return null; }
        }
    }
}
