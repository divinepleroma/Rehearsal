<#
.SYNOPSIS
    Builds the plugin, runs the tests and (optionally) renders the UI preview.

.DESCRIPTION
    Uses the .NET SDK on PATH, or a user-local one under %LOCALAPPDATA%\Microsoft\dotnet if there
    is none (that is where Microsoft's dotnet-install script puts it).

    The plugin is built against the Dalamud that XIVLauncher installed
    (%APPDATA%\XIVLauncher\addon\Hooks\dev).

.EXAMPLE
    .\build.ps1                 # build + test
    .\build.ps1 -Preview        # also render the UI to artifacts\preview\*.png
    .\build.ps1 -Site           # re-render the product page's pictures into docs\img
    .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    # Render the timeline and the main window to PNG files (needs a local game install).
    [switch] $Preview,

    # UI scale for the preview; match your Dalamud "Global UI Scale" to see it as you would in game.
    [double] $PreviewScale = 1.0,

    # An image to use as the scenery behind the preview (e.g. one of your screenshots).
    [string] $PreviewBackground,

    # Render the pictures of the product page (docs\img) from the real UI code (needs a local game install).
    [switch] $Site,

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$command = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($command) { $command.Source } else { $null }
$hasSdk = $dotnet -and ((& $dotnet --list-sdks) | Measure-Object).Count -gt 0
if (-not $hasSdk) {
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path $local)) {
        throw 'No .NET SDK found. Install the .NET 10 SDK (https://dot.net/download).'
    }
    $dotnet = $local
}

$root = $PSScriptRoot

Write-Host "==> Building the plugin ($Configuration)" -ForegroundColor Cyan
& $dotnet build (Join-Path $root 'Rehearsal\Rehearsal.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if (-not $SkipTests) {
    Write-Host '==> Running the tests' -ForegroundColor Cyan
    & $dotnet test (Join-Path $root 'Rehearsal.Tests\Rehearsal.Tests.csproj') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if ($Preview) {
    Write-Host '==> Rendering the UI preview' -ForegroundColor Cyan
    $previewArgs = @('--scale', $PreviewScale.ToString([cultureinfo]::InvariantCulture))
    if ($PreviewBackground) { $previewArgs += @('--bg', (Resolve-Path $PreviewBackground).Path) }
    & $dotnet run --project (Join-Path $root 'tools\UiPreview\UiPreview.csproj') -c Release -- @previewArgs
    if ($LASTEXITCODE -ne 0) { throw 'Preview failed.' }
}

if ($Site) {
    Write-Host '==> Rendering the pictures for the product page' -ForegroundColor Cyan
    # One and a half times the size they are shown at, so they stay sharp on dense screens.
    & $dotnet run --project (Join-Path $root 'tools\UiPreview\UiPreview.csproj') -c Release -- --scale 1.5 --site (Join-Path $root 'docs\img')
    if ($LASTEXITCODE -ne 0) { throw 'Rendering the site pictures failed.' }
}

$dll = Join-Path $root "Rehearsal\bin\$Configuration\Rehearsal.dll"
Write-Host ''
Write-Host 'Done. To load it in game, add this path under' -ForegroundColor Green
Write-Host '  /xlsettings -> Experimental -> Dev Plugin Locations' -ForegroundColor Green
Write-Host "  $dll"
