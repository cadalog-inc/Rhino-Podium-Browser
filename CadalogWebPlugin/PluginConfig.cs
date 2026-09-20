using System;
using System.IO;

namespace CadalogWebPlugin
{
    /// <summary>
    /// Centralized configuration. Resolution order for the panel's home URL:
    ///   1. Environment variable <c>CADALOG_PODIUM_URL</c> (highest priority — easy to flip during dev).
    ///   2. <c>%APPDATA%/Cadalog/podium-url.txt</c> (single line containing the URL).
    ///   3. <c>ProductionUrl</c> (the real PDM site).
    ///
    /// To swap to the local dev server, set the env var before launching Rhino:
    ///   <c>setx CADALOG_PODIUM_URL http://127.0.0.1:5174</c>
    /// or place a one-line file at the path above.
    /// </summary>
    internal static class PluginConfig
    {
        // The Rhino-native Podium Browser: serves .3dm assets. This is the
        // correct default for the Rhino plug-in. The original SketchUp catalog
        // serves .skp (Rhino 8 imports those natively too).
        public const string RhinoCatalogUrl = "https://rhino3d.pdm-plants-textures.com/";
        public const string SketchUpCatalogUrl = "https://v4.pdm-plants-textures.com/";
        public const string ProductionUrl = RhinoCatalogUrl;
        public const string DevDefaultUrl = "http://127.0.0.1:5174/";

        /// <summary>A catalog the user can switch between from the panel.</summary>
        public sealed record CatalogSource(string Label, string Url);

        public static readonly CatalogSource[] Sources =
        {
            new("Rhino catalog (.3dm)", RhinoCatalogUrl),
            new("SketchUp catalog (.skp)", SketchUpCatalogUrl),
        };

        /// <summary>True when CADALOG_PODIUM_URL is set — it wins over the saved choice on next launch.</summary>
        public static bool HasEnvOverride =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CADALOG_PODIUM_URL"));

        public static string GetHomeUrl()
        {
            var fromEnv = Environment.GetEnvironmentVariable("CADALOG_PODIUM_URL");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            try
            {
                var line = File.Exists(PreferredUrlFile)
                    ? File.ReadAllText(PreferredUrlFile).Trim()
                    : null;
                if (!string.IsNullOrWhiteSpace(line)) return line;
            }
            catch
            {
                // Non-fatal: fall through to production URL.
            }

            return ProductionUrl;
        }

        /// <summary>
        /// Persists the panel's catalog choice so it survives a Rhino restart.
        /// Returns false (without throwing) if the file couldn't be written.
        /// </summary>
        public static bool SavePreferredUrl(string url)
        {
            try
            {
                var dir = Path.GetDirectoryName(PreferredUrlFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(PreferredUrlFile, url.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Compares two catalog URLs ignoring case and trailing slashes.</summary>
        public static bool SameUrl(string? a, string? b) =>
            string.Equals(
                (a ?? string.Empty).TrimEnd('/'),
                (b ?? string.Empty).TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);

        private static string PreferredUrlFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cadalog",
            "podium-url.txt");

        /// <summary>
        /// Directory used to stage downloads. Cleared lazily; never long-lived.
        /// </summary>
        public static string DownloadStagingDir
        {
            get
            {
                var dir = Path.Combine(Path.GetTempPath(), "Cadalog", "downloads");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
