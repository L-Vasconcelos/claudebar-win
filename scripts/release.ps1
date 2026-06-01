#requires -Version 7
# Release de ClaudeBar: bump version -> publish -> instalador -> appcast firmado -> (release GitHub).
# Uso:  pwsh scripts/release.ps1 -Version 0.1.1
#       pwsh scripts/release.ps1 -Version 0.1.1 -Publish   (sube el release a GitHub; LÍNEA ROJA)
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$Publish   # sin este flag, NO sube nada a GitHub (dry-run local)
)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$appcast = "$env:USERPROFILE\.dotnet\tools\netsparkle-generate-appcast.exe"
$csproj = Join-Path $repo "ClaudeBarWin.csproj"
$dist   = Join-Path $repo "dist"
$keys   = Join-Path $repo ".sparkle-keys"

# Localizar ISCC (Inno Setup) en las rutas habituales (per-user o per-machine).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) no encontrado." }

# 1) Bump <Version> en el csproj
(Get-Content $csproj -Raw) -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>" |
    Set-Content $csproj -NoNewline
Write-Host "Version -> $Version"

# 2) Publish self-contained single-file
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $repo "publish") --nologo -v minimal
if ($LASTEXITCODE) { throw "publish falló" }

# 3) Instalador Inno
New-Item -ItemType Directory -Force $dist | Out-Null
Get-ChildItem "$dist\*.exe","$dist\appcast.xml" -ErrorAction SilentlyContinue | Remove-Item -Force
& $iscc (Join-Path $repo "installer\ClaudeBarWin.iss") "/DMyVersion=$Version"
if ($LASTEXITCODE) { throw "ISCC falló" }
$setup = Join-Path $dist "ClaudeBarWin-Setup-$Version.exe"

# 4) Appcast firmado (Ed25519). base-url = assets de la release del tag.
& $appcast `
    --binaries $dist `
    --ext exe `
    --base-url "https://github.com/Yovancas/claudebar-win/releases/download/v$Version/" `
    --key-path $keys `
    --appcast-output-directory $dist `
    --change-log-path (Join-Path $repo "installer\notes") `
    --file-version $Version
if ($LASTEXITCODE) { throw "generate-appcast falló" }
Write-Host "OK -> $setup  +  $dist\appcast.xml"

# 5) Publicar en GitHub (LÍNEA ROJA: solo con -Publish y tras OK de Yovan)
if ($Publish) {
    $notes = Join-Path $dist "release-notes.md"
    if (-not (Test-Path $notes)) { "ClaudeBar v$Version" | Set-Content $notes }
    gh release create "v$Version" $setup (Join-Path $dist "appcast.xml") `
        --repo Yovancas/claudebar-win --title "v$Version" --notes-file $notes
} else {
    Write-Host "DRY-RUN: nada subido. Revisa $dist y relanza con -Publish para release." -ForegroundColor Yellow
}
