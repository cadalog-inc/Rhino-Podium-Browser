# Podium Browser for Rhino

A Rhino 8 plug-in that puts the Podium Browser content library inside Rhino as a
dockable panel. Browse or search the library, switch between the Rhino (`.3dm`)
and SketchUp (`.skp`) catalogs, and click a model to download it and place it in
the open document — no manual downloading, unzipping or importing.

Built for Cadalog, Inc. Windows only.

## Requirements

- Windows 10 or 11 (64-bit)
- **Rhino 8** — ships the .NET 7 runtime, Eto and WebView2 that the plug-in
  builds against
- .NET SDK, to build
- Microsoft Edge WebView2 Runtime — already present on Windows 11 and installed
  with Rhino 8

## Build

```powershell
cd CadalogWebPlugin
dotnet build -c Release          # -> bin\Release\CadalogWebPlugin.rhp
```

**Rhino must be closed.** It locks the `.rhp` while running; the build checks for
this and fails immediately with a clear message rather than sitting in MSBuild's
retry loop.

## Install

Drag `CadalogWebPlugin.rhp` onto an open Rhino viewport and confirm the security
prompt. Rhino remembers the location, so it loads by itself from then on.

Then run the **`PodiumBrowser`** command to toggle the panel. (`CadalogPlants`
still works as an alias.)

For end users, build the distributable package instead — see below.

## Project layout

| File | Purpose |
| --- | --- |
| `CadalogWebPlugin.cs` | Plug-in entry point; registers the panel on load |
| `WebPanel.cs` | The dockable panel: WebView2 plus the catalog switcher |
| `WebViewBridge.cs` | Hooks `CoreWebView2` for page messages and download interception |
| `AssetImporter.cs` | Downloads an asset, imports it, and starts interactive placement |
| `PluginConfig.cs` | Home-URL resolution and the download staging folder |
| `PodiumBrowserCommand.cs` | The `PodiumBrowser` command (and the `CadalogPlants` alias) |
| `packaging/` | `pack.ps1`, the yak manifest, and the end-user install guide |

### How placement works

The live Podium Browser site never calls a host API — clicking download simply
points `window.location` at an `application/octet-stream` URL. The plug-in
intercepts that with WebView2's `DownloadStarting` event, redirects the file into
`%TEMP%\Cadalog\downloads\`, imports it when the transfer finishes, and then
starts a `Move` from the geometry's bounding-box centre so the asset tracks the
cursor until the user clicks a spot.

That means **no change is needed on the website** for the plug-in to work.

The panel also accepts `{"type":"insert3dm", url, name, sidecars[]}` over
`chrome.webview.postMessage`, which is how a page can drive an insert directly.

### Pointing the panel somewhere else

`CADALOG_PODIUM_URL` overrides the panel's home URL:

```powershell
setx CADALOG_PODIUM_URL http://127.0.0.1:5250/   # then restart Rhino
setx CADALOG_PODIUM_URL ""                        # back to the live site
```

Otherwise the panel uses the catalog last chosen in the dropdown, stored in
`%APPDATA%\Cadalog\podium-url.txt`, falling back to the Rhino catalog.

## Building the customer package

```powershell
powershell -ExecutionPolicy Bypass -File CadalogWebPlugin\packaging\pack.ps1
```

Produces `dist\Podium Browser for Rhino\`:

- `1 - Package Manager (recommended)\` — the `.yak` package
- `2 - Manual (rhp)\` — the raw plug-in files to drag onto Rhino
- `Install and Use` — the end-user guide as `.pdf`, `.html` and `.txt`
- `Podium Browser for Rhino.zip` — all of the above, ready to send

> **No `.rhi` is produced, deliberately.** The Rhino Installer Engine has been
> obsolete since Rhino 7 and cannot inspect .NET 7 plug-in assemblies, so an
> `.rhi` of this plug-in fails on Rhino 8 with "not compatible with the Rhino
> Installer Engine". Yak is the supported path.

### Minimum Rhino version

Yak derives the package's minimum Rhino version from the **RhinoCommon version
the project references**. It is pinned to `8.0.23304.9001` so the package is
tagged `rh8_0-win` and installs on *any* Rhino 8. Bumping that reference to a
newer service release would silently lock out everyone on an older Rhino 8, so
change it only on purpose.

## Publishing to the Rhino Package Manager

```powershell
"C:\Program Files\Rhino 8\System\Yak.exe" login
"C:\Program Files\Rhino 8\System\Yak.exe" push podium-browser-1.0.0-rh8_0-win.yak
```

Push to `--source https://test.yak.rhino3d.com` first — it is wiped nightly and
costs nothing to get wrong.

Three things about the package server are permanent and worth knowing before the
first push:

- **The first push claims the package name** for that account, forever.
- **Only owners can push updates.** Add others with
  `Yak.exe owner add podium-browser <email>`; they must already have an account
  on the server.
- **Published versions can never be deleted or overwritten.** A bad release is
  fixed by publishing a higher version, not by replacing it.

## Backend

See [docs/podium-backend-notes.md](docs/podium-backend-notes.md) for the verified
API contract, asset URL scheme, and a finding about paid-asset access that needs
a decision from Cadalog.

## Note on the license file

The Rhino NFR developer license key lives in
`rhino developer license and info.txt` **one directory above this repo**, so it
sits outside the working tree entirely and no `git add` in here can reach it. It
has never been committed. A backstop rule in `.gitignore` covers the filename in
case a copy ever lands inside the repo.
