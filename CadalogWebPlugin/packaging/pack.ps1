<#
.SYNOPSIS
  Builds the three distributable forms of the Podium Browser Rhino plug-in and
  the end-user documentation into dist/.

.DESCRIPTION
  Produces:
    1 - Package Manager (recommended)  ->  .yak package (Rhino PackageManager)
    2 - Manual (.rhp)                  ->  raw plug-in files to drag onto Rhino
  plus "Install and Use" in .pdf / .txt / .html, generated from the HTML source
  in packaging/.

  NOTE: the legacy .rhi installer is deliberately NOT produced. The Rhino
  Installer Engine is obsolete as of Rhino 7 and cannot inspect .NET 7 plug-in
  assemblies, so an .rhi of this plug-in fails on Rhino 8 with "not compatible
  with the Rhino Installer Engine". Yak is the supported path.

  Rhino must be CLOSED: it locks the .rhp while running.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File CadalogWebPlugin\packaging\pack.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipDocs
)

$ErrorActionPreference = 'Stop'

$packagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir   = Split-Path -Parent $packagingDir
$repoRoot     = Split-Path -Parent $projectDir
$csproj       = Join-Path $projectDir 'CadalogWebPlugin.csproj'
$buildDir     = Join-Path $projectDir "bin\$Configuration"

$manifest = Join-Path $packagingDir 'manifest.yml'
$version  = ((Get-Content $manifest | Select-String '^version:\s*(.+)$').Matches[0].Groups[1].Value).Trim()

$distRoot = Join-Path $repoRoot 'dist\Podium Browser for Rhino'
$yakDir   = Join-Path $distRoot '1 - Package Manager (recommended)'
$rhpDir   = Join-Path $distRoot '2 - Manual (rhp)'
$staging  = Join-Path $repoRoot 'dist\.staging'

Write-Host "Podium Browser for Rhino - packaging v$version" -ForegroundColor Cyan

# --- 1. Build ---------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "`n[1/4] Building ($Configuration)..." -ForegroundColor Cyan
    & dotnet build $csproj -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed (is Rhino running? it locks the .rhp)." }
}
if (-not (Test-Path (Join-Path $buildDir 'CadalogWebPlugin.rhp'))) {
    throw "No .rhp found in $buildDir - build first."
}

# --- 2. Stage the payload ---------------------------------------------------
# Everything Rhino needs at runtime: the plug-in, its dependency manifests and
# the native WebView2 loaders. Debug symbols are deliberately excluded.
Write-Host "`n[2/4] Staging payload..." -ForegroundColor Cyan
# Wipe the whole output folder, not just the folders we are about to write, so
# artifacts from a previous layout (e.g. a retired install option) can't linger.
# A file browser or a shell sitting in dist/ can hold a transient lock, so
# retry briefly instead of failing the whole pack.
for ($i = 0; $i -lt 10 -and (Test-Path $distRoot); $i++) {
    try { Remove-Item $distRoot -Recurse -Force -ErrorAction Stop }
    catch { Start-Sleep -Milliseconds 400 }
}
if (Test-Path $distRoot) { throw "Can't clear $distRoot - close any window or shell sitting in that folder." }
foreach ($dir in @($staging, $yakDir, $rhpDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Get-ChildItem $buildDir -Recurse -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object {
        $rel  = $_.FullName.Substring($buildDir.Length).TrimStart('\')
        $dest = Join-Path $staging $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item $_.FullName $dest -Force
    }

$payload = Get-ChildItem $staging -Recurse -File
Write-Host ("      {0} files, {1:N0} KB" -f $payload.Count, (($payload | Measure-Object Length -Sum).Sum / 1KB))

# --- 3. Yak package ---------------------------------------------------------
Write-Host "`n[3/4] Building .yak package..." -ForegroundColor Cyan
$yakExe = Get-ChildItem 'C:\Program Files\Rhino *\System\Yak.exe' -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if ($yakExe) {
    Copy-Item $manifest $staging -Force
    Copy-Item (Join-Path $projectDir 'assets\icon.png') $staging -Force

    Push-Location $staging
    try { & $yakExe build --platform win } finally { Pop-Location }

    $yak = Get-ChildItem $staging -Filter '*.yak' | Select-Object -First 1
    if (-not $yak) { throw 'yak build produced no package.' }
    Move-Item $yak.FullName (Join-Path $yakDir $yak.Name) -Force
    Write-Host "      $($yak.Name)"
} else {
    Write-Warning 'Yak.exe not found (Rhino not installed?) - skipping the .yak package.'
}

# --- 4. Manual .rhp folder --------------------------------------------------
# The raw plug-in payload, for dragging onto a Rhino viewport.
Write-Host "`n[4/4] Building manual folder..." -ForegroundColor Cyan
Remove-Item (Join-Path $staging 'manifest.yml') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $staging 'icon.png') -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $staging '*') $rhpDir -Recurse -Force
Write-Host "      manual folder ready"

Remove-Item $staging -Recurse -Force

# --- 5. Documentation -------------------------------------------------------
# The HTML in packaging/ is the print master; Edge (headless) renders the PDF.
# The plain-text version is maintained alongside it for anyone without a viewer.
if (-not $SkipDocs) {
    Write-Host "`nGenerating documentation..." -ForegroundColor Cyan
    $html = Join-Path $packagingDir 'Install-and-Use.html'
    $txt  = Join-Path $packagingDir 'Install-and-Use.txt'
    Copy-Item $html (Join-Path $distRoot 'Install and Use.html') -Force
    Copy-Item $txt  (Join-Path $distRoot 'Install and Use.txt')  -Force

    $edge = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($edge) {
        $pdf        = Join-Path $distRoot 'Install and Use.pdf'
        $profileDir = Join-Path $env:TEMP ('edge-pdf-' + [guid]::NewGuid().ToString('N'))
        $uri        = ([uri]$html).AbsoluteUri
        # Note: the *old* headless mode is the one that supports --print-to-pdf.
        # Edge reports success on stderr, so relax the error preference around it.
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $edge --headless --disable-gpu --no-pdf-header-footer `
                "--user-data-dir=$profileDir" "--print-to-pdf=$pdf" $uri *> $null
        $ErrorActionPreference = $prevEap
        Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue

        # Edge can return before its child process has finished flushing the
        # file, so wait for the PDF to appear AND to be complete (%%EOF) rather
        # than testing for it immediately - otherwise the zip ships without it.
        $deadline = (Get-Date).AddSeconds(30)
        $pdfOk = $false
        while ((Get-Date) -lt $deadline) {
            if (Test-Path $pdf) {
                try {
                    $tail = Get-Content $pdf -Tail 1 -Encoding Byte -ErrorAction Stop
                    if ((Get-Item $pdf).Length -gt 1024) { $pdfOk = $true; break }
                } catch { }   # still being written; retry
            }
            Start-Sleep -Milliseconds 300
        }

        if ($pdfOk) { Write-Host '      pdf + txt + html' }
        else { Write-Warning 'Edge did not produce the PDF; the html and txt versions are still there.' }
    } else {
        Write-Warning 'Microsoft Edge not found - skipping the PDF; html and txt were copied.'
    }

}

# --- 6. Zip the whole drop -------------------------------------------------
# This single file is what gets emailed / uploaded to the customer.
$zipPath = Join-Path (Split-Path -Parent $distRoot) 'Podium Browser for Rhino.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $distRoot '*') -DestinationPath $zipPath -Force
Move-Item $zipPath (Join-Path $distRoot 'Podium Browser for Rhino.zip') -Force

Write-Host "`nDone -> $distRoot" -ForegroundColor Green
Get-ChildItem $distRoot -Recurse -File |
    ForEach-Object { '  ' + $_.FullName.Substring($distRoot.Length + 1) }
