<#
  Build & run helper for ClaudeBarWin.
  Uses the user-local .NET SDK at %USERPROFILE%\.dotnet (no admin install required).

  Usage:
    .\run.ps1            # build + run (debug)
    .\run.ps1 build      # build only
    .\run.ps1 publish    # self-contained single-file exe -> .\publish\ClaudeBarWin.exe
#>
param([string]$task = "run")

$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { $dotnet = "dotnet" }
    else { throw "No se encontró dotnet. Instala el SDK .NET 9." }
}

$proj = Join-Path $PSScriptRoot "ClaudeBarWin.csproj"

switch ($task) {
    "build"   { & $dotnet build $proj -c Release }
    "publish" {
        & $dotnet publish $proj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -o (Join-Path $PSScriptRoot "publish")
        Write-Host "`nExe: $(Join-Path $PSScriptRoot 'publish\ClaudeBarWin.exe')"
    }
    default   { & $dotnet run --project $proj -c Release }
}
