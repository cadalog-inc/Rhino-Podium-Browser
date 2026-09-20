using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;

namespace CadalogWebPlugin
{
    /// <summary>
    /// Receives <c>{"type":"insert3dm", url, name, sidecars[]}</c> messages from
    /// the web page, downloads the asset (and any sidecar files such as textures)
    /// into a per-asset folder under <see cref="PluginConfig.DownloadStagingDir"/>,
    /// then runs Rhino's <c>-Import</c> command and starts an interactive Move so
    /// the user drops the geometry where they click.
    /// </summary>
    internal static class AssetImporter
    {
        // Single HttpClient instance; safe for concurrent use.
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        /// <summary>
        /// Entry point. Parses the JSON message and dispatches by <c>type</c>.
        /// </summary>
        public static void HandleWebMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;
                var type = typeProp.GetString();

                switch (type)
                {
                    case "insert3dm":
                        _ = HandleInsert3dmAsync(doc.RootElement);
                        break;
                    case "ready":
                        // Diagnostic ping — no action needed.
                        break;
                    default:
                        RhinoApp.WriteLine($"[Cadalog] unknown message type: {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Cadalog] message parse error: {ex.Message}");
            }
        }

        private static async Task HandleInsert3dmAsync(JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("url", out var urlProp))
                {
                    RhinoApp.WriteLine("[Cadalog] insert3dm: missing 'url'");
                    return;
                }
                var url = urlProp.GetString();
                if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    RhinoApp.WriteLine("[Cadalog] insert3dm: invalid url");
                    return;
                }

                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) name = "model.3dm";
                name = SanitizeFileName(name);

                var sidecars = new List<string>();
                if (root.TryGetProperty("sidecars", out var sidecarsProp)
                    && sidecarsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in sidecarsProp.EnumerateArray())
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) sidecars.Add(s);
                    }
                }

                // Per-call staging folder so multiple inserts don't collide.
                var stagingDir = Path.Combine(
                    PluginConfig.DownloadStagingDir,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                Directory.CreateDirectory(stagingDir);

                // Download the primary file.
                var localFile = Path.Combine(stagingDir, name);
                await DownloadAsync(new Uri(url), localFile);

                // Download sidecar files (resolved relative to the .3dm URL's folder).
                var baseUri = new Uri(url);
                foreach (var sidecar in sidecars)
                {
                    try
                    {
                        var safeName = SanitizeFileName(sidecar);
                        var sidecarUri = new Uri(baseUri, safeName);
                        var sidecarLocal = Path.Combine(stagingDir, safeName);
                        await DownloadAsync(sidecarUri, sidecarLocal);
                    }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine($"[Cadalog] sidecar '{sidecar}' failed: {ex.Message}");
                    }
                }

                // Hand off to Rhino on the UI thread.
                RhinoApp.InvokeOnUiThread(new Action(() => RunImportForFile(localFile)));
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Cadalog] insert3dm failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Imports an already-downloaded local file into the active document,
        /// choosing the Rhino command by extension. Called by the WebView2
        /// <c>DownloadStarting</c> interceptor once the real Podium Browser has
        /// finished streaming the asset to disk. Must run on the Rhino UI thread.
        /// </summary>
        public static void ImportLocalFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                RhinoApp.WriteLine($"[Cadalog] import: file not found: {path}");
                return;
            }
            RunImportForFile(path);
        }

        /// <summary>
        /// Derives a safe local file name from a download URL, falling back to
        /// WebView2's suggested path or a generic name.
        /// </summary>
        public static string FileNameFromUrl(string? url, string? suggestedPath = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url)
                    && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var segment = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(segment)) return SanitizeFileName(segment);
                }
            }
            catch { /* fall through to suggested/default */ }

            if (!string.IsNullOrWhiteSpace(suggestedPath))
            {
                var fromSuggested = Path.GetFileName(suggestedPath);
                if (!string.IsNullOrWhiteSpace(fromSuggested)) return SanitizeFileName(fromSuggested);
            }
            return "asset.skp";
        }

        private static void RunImportForFile(string localFile)
        {
            var ext = Path.GetExtension(localFile).ToLowerInvariant();

            // One path for every format (.3dm, .skp, …): Rhino 8 imports all of
            // them natively. The importer drops geometry at the file's own
            // coordinates and leaves it selected, so we immediately start a Move
            // that lets the user drop the asset wherever they click (SketchUp
            // "place component" feel). Simpler and more predictable than
            // -Insert's block-definition flow, which we used for .3dm before.
            var doc = RhinoDoc.ActiveDoc;
            RhinoApp.WriteLine($"[Cadalog] importing ({ext}): {localFile}");
            RhinoApp.RunScript($"_-Import \"{localFile}\" _Enter", echo: false);

            if (doc != null) BeginInteractivePlacement(doc);
        }

        /// <summary>
        /// Starts an interactive Move on the just-imported (selected) objects,
        /// using their bounding-box center as the grab point so the asset tracks
        /// the cursor until the user clicks a destination point. If nothing is
        /// selected (e.g. the importer changed behavior), the geometry is simply
        /// left where it was imported.
        /// </summary>
        private static void BeginInteractivePlacement(RhinoDoc doc)
        {
            var bbox = BoundingBox.Empty;
            foreach (var obj in doc.Objects.GetSelectedObjects(false, false))
            {
                var b = obj.Geometry?.GetBoundingBox(false) ?? BoundingBox.Empty;
                if (b.IsValid) bbox.Union(b);
            }

            if (!bbox.IsValid) return; // nothing to place

            var c = bbox.Center;
            var from = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2}", c.X, c.Y, c.Z);

            // The imported objects are already selected, so _Move uses them; we
            // supply the grab point and leave the destination point for the user.
            RhinoApp.WriteLine("[Cadalog] placing: pick a point to drop the asset");
            RhinoApp.RunScript($"_Move {from}", echo: false);
        }

        private static async Task DownloadAsync(Uri url, string destPath)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var input = await resp.Content.ReadAsStreamAsync();
            await using var output = File.Create(destPath);
            await input.CopyToAsync(output);
        }

        private static string SanitizeFileName(string name)
        {
            // Strip any path components & illegal chars.
            var raw = Path.GetFileName(name) ?? "model.3dm";
            foreach (var c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
