# Auto-update en Windows (ClaudeBarWin) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sustituir el update semi-manual de ClaudeBar por auto-update real (comprueba → descarga → verifica firma → instala → relanza) con NetSparkleUpdater + appcast firmado EdDSA, distribuyendo un instalador Inno Setup vía GitHub Releases.

**Architecture:** Un servicio wrapper (`SparkleUpdateService`) aísla NetSparkleUpdater del resto. La app comprueba un `appcast.xml` firmado (clave pública embebida) servido en `releases/latest/download/`, descarga el instalador Inno, verifica la firma Ed25519 y lo ejecuta en silencio; el instalador cierra la app, reemplaza el exe per-user y la relanza. Los datos en `%APPDATA%\ClaudeBarWin` no se tocan nunca.

**Tech Stack:** C#/.NET 9 WinForms (self-contained single-file), NetSparkleUpdater.SparkleUpdater + NetSparkleUpdater.UI.WinForms, Inno Setup 6, dotnet tool `NetSparkleUpdater.Tools.AppCastGenerator`, GitHub Releases (`gh`).

**Nota sobre TDD:** ClaudeBar no tiene proyecto de tests y esta feature es integración con una librería de terceros + un instalador + red. No hay lógica unitaria significativa que aislar, así que la verificación de cada tarea es **funcional** (compilar / ejecutar / observar), no test unitario. Es lo honesto para este dominio; el spec ya lo anticipó.

**Build local:** usar el SDK user-local. Prefijo en todos los comandos `dotnet`:
`$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" ...`

**Línea roja:** las Tareas 0 (instalar tool global) y 7 (publicar release en GitHub) requieren acción/OK de Yovan. El resto es local y reversible.

---

### Task 0: Claves Ed25519 + herramienta de appcast (prep, una vez)

**Files:**
- Create: `.sparkle-keys/` (carpeta local, **gitignored**, NO se commitea)
- Modify: `.gitignore`

- [ ] **Step 1: Instalar el dotnet tool generador de appcast (global)**

Run:
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" tool install --global NetSparkleUpdater.Tools.AppCastGenerator
```
Expected: "You can invoke the tool using the following command: netsparkle-generate-appcast". Si ya está instalado, `tool update` en su lugar. (Asegurar que `%USERPROFILE%\.dotnet\tools` está en PATH.)

- [ ] **Step 2: Ignorar la carpeta de claves ANTES de generarlas**

Edit `.gitignore`, añadir al final:
```
# NetSparkle signing keys — NUNCA commitear la privada
.sparkle-keys/
```

- [ ] **Step 3: Generar el par Ed25519**

Run:
```powershell
$env:SparkleKeyPath = "C:\Users\zorro\Proyectos\claudebar-win\.sparkle-keys"
netsparkle-generate-appcast --generate-keys --key-path $env:SparkleKeyPath
```
Expected: crea `NetSparkle_Ed25519.priv` y `NetSparkle_Ed25519.pub` en `.sparkle-keys`. Imprime la **clave pública en base64**.

- [ ] **Step 4: Anotar la clave pública**

Run:
```powershell
Get-Content "C:\Users\zorro\Proyectos\claudebar-win\.sparkle-keys\NetSparkle_Ed25519.pub"
```
Copiar el base64 → se usa literal en la Tarea 2 (`PublicKeyBase64`). Confirmar que `git status` **no** lista `.sparkle-keys/`.

---

### Task 1: Añadir paquetes NetSparkleUpdater

**Files:**
- Modify: `ClaudeBarWin.csproj:19-21` (el `<ItemGroup>` de PackageReference)

- [ ] **Step 1: Añadir los dos PackageReference**

En `ClaudeBarWin.csproj`, dentro del `<ItemGroup>` existente (junto a Microsoft.Data.Sqlite):
```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
    <PackageReference Include="NetSparkleUpdater.SparkleUpdater" Version="2.7.0" />
    <PackageReference Include="NetSparkleUpdater.UI.WinForms" Version="2.7.0" />
  </ItemGroup>
```
(Si 2.7.0 no resuelve, usar la última 2.x estable que devuelva `dotnet add package`.)

- [ ] **Step 2: Restaurar y compilar**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal
```
Expected: "Compilación correcta. 0 Errores".

- [ ] **Step 3: Commit**

```bash
git add ClaudeBarWin.csproj .gitignore
git commit -m "build: add NetSparkleUpdater packages + ignore sparkle keys"
```

---

### Task 2: Servicio `SparkleUpdateService`

**Files:**
- Create: `Services/SparkleUpdateService.cs`

- [ ] **Step 1: Crear el wrapper**

Create `Services/SparkleUpdateService.cs` (sustituir `PEGAR_CLAVE_PUBLICA_DE_TASK_0` por el base64 de la Tarea 0):
```csharp
using System.Drawing;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WinForms;

namespace ClaudeBarWin.Services;

/// <summary>
/// Auto-update via NetSparkleUpdater. Appcast firmado (Ed25519) servido desde la
/// última release de GitHub; el binario descargado es el instalador Inno, que se
/// ejecuta en silencio, cierra la app, reemplaza el exe y la relanza.
/// </summary>
public sealed class SparkleUpdateService : IDisposable
{
    private const string AppCastUrl =
        "https://github.com/Yovancas/claudebar-win/releases/latest/download/appcast.xml";

    // Clave pública Ed25519 generada en la Tarea 0 (la privada NUNCA va al repo).
    private const string PublicKeyBase64 = "PEGAR_CLAVE_PUBLICA_DE_TASK_0";

    private readonly SparkleUpdater _sparkle;

    /// <summary>Tag de la versión disponible, o null si estamos al día / sin comprobar.</summary>
    public string? AvailableTag { get; private set; }

    /// <summary>Se dispara cuando cambia AvailableTag (para refrescar el menú).</summary>
    public event Action? AvailabilityChanged;

    public SparkleUpdateService(Icon? icon)
    {
        _sparkle = new SparkleUpdater(AppCastUrl, new Ed25519Checker(SecurityMode.Strict, PublicKeyBase64))
        {
            UIFactory = new UIFactory(icon),
            RelaunchAfterUpdate = false, // lo relanza el instalador Inno, no NetSparkle
            CustomInstallerArguments = "/VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART",
        };
        _sparkle.UpdateDetected += (_, e) =>
        {
            AvailableTag = e.LatestVersion?.Version;
            AvailabilityChanged?.Invoke();
        };
    }

    /// <summary>Comprobación silenciosa al arrancar + cada 6 h. Llamar UNA vez.</summary>
    public void StartLoop() => _sparkle.StartLoop(true, TimeSpan.FromHours(6));

    /// <summary>Comprobación a petición del usuario (muestra la UI de NetSparkle).</summary>
    public Task CheckInteractive() => _sparkle.CheckForUpdatesAtUserRequest();

    public void Dispose() => _sparkle.Dispose();
}
```

- [ ] **Step 2: Compilar**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal
```
Expected: 0 errores. (Si `UpdateDetected`/`LatestVersion.Version` no casan con la versión instalada de NetSparkle, ajustar al nombre real del evento/propiedad que exponga la API — verificar con IntelliSense/symbols del paquete restaurado.)

- [ ] **Step 3: Commit**

```bash
git add Services/SparkleUpdateService.cs
git commit -m "feat: SparkleUpdateService (NetSparkle wrapper, appcast firmado)"
```

---

### Task 3: Cablear en `TrayAppContext`

**Files:**
- Modify: `TrayAppContext.cs` (campos 36-37, ctor 98-120, menú ~346-348, `UpdateMenuChecks` ~395-397, método `CheckUpdatesAsync` ~720-760)

- [ ] **Step 1: Reemplazar los campos de update**

En `TrayAppContext.cs`, sustituir las líneas 36-37:
```csharp
    private ToolStripMenuItem _miUpdate = null!;
    private UpdateInfo? _update;
```
por:
```csharp
    private ToolStripMenuItem _miUpdate = null!;
    private SparkleUpdateService _updates = null!;
```

- [ ] **Step 2: Instanciar el servicio y arrancar el loop en el ctor**

En el ctor, DESPUÉS de crear `_tray` (tras la línea 105, ya existe `_currentIcon`), añadir antes de `_timer`:
```csharp
        _updates = new SparkleUpdateService(_currentIcon);
        _updates.AvailabilityChanged += () =>
        {
            if (_tray.ContextMenuStrip is { } m && m.InvokeRequired)
                m.BeginInvoke(new Action(UpdateMenuChecks));
            else
                UpdateMenuChecks();
        };
```
Y reemplazar la línea 119:
```csharp
        _ = CheckUpdatesAsync(silent: true);   // silent check on startup; flags the menu if newer
```
por:
```csharp
        _updates.StartLoop();   // comprobación silenciosa al arrancar + cada 6 h
```

- [ ] **Step 3: Cablear el item de menú**

Localizar (≈línea 347) `_miUpdate.Click += async (_, _) => await CheckUpdatesAsync(silent: false);` y sustituir por:
```csharp
        _miUpdate.Click += async (_, _) => await _updates.CheckInteractive();
```

- [ ] **Step 4: Actualizar el texto del menú**

En `UpdateMenuChecks` (≈línea 395-397), sustituir el bloque:
```csharp
        _miUpdate.Text = _update is { IsNewer: true }
            ? string.Format(_s.UpdateAvailableFmt, _update.LatestTag)
            : _s.CheckUpdates;
```
por:
```csharp
        _miUpdate.Text = _updates.AvailableTag is { } tag
            ? string.Format(_s.UpdateAvailableFmt, tag)
            : _s.CheckUpdates;
```

- [ ] **Step 5: Borrar el método viejo `CheckUpdatesAsync`**

Eliminar por completo el método `private async Task CheckUpdatesAsync(bool silent)` (≈líneas 720-760). Sus diálogos los sustituye la UI de NetSparkle. (`Process`/`MessageBox` siguen usándose en otros métodos: no quitar los `using`.)

- [ ] **Step 6: Compilar**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal
```
Expected: 0 errores. (Aún referenciará `UpdateChecker` desde `ShowAbout`/otros — si `CurrentVersion` se usaba en `ShowAbout`, ver Task 4 step 2.)

- [ ] **Step 7: Commit**

```bash
git add TrayAppContext.cs
git commit -m "feat: usar SparkleUpdateService en el tray (retira el update manual)"
```

---

### Task 4: Retirar `UpdateChecker` y reubicar `CurrentVersion`

**Files:**
- Modify: `TrayAppContext.cs` (`ShowAbout`, ≈línea 762-766)
- Delete: `Services/UpdateChecker.cs`

- [ ] **Step 1: Sustituir los usos de `UpdateChecker.CurrentVersion`/`RepoUrl`**

`ShowAbout` usa `UpdateChecker.CurrentVersion` y `UpdateChecker.RepoUrl`. Reemplazar esas referencias por valores locales en `TrayAppContext`:
```csharp
    private static string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
    private const string RepoUrl = "https://github.com/Yovancas/claudebar-win";
```
y en `ShowAbout` cambiar `UpdateChecker.CurrentVersion` → `AppVersion` y `UpdateChecker.RepoUrl` → `RepoUrl`.

- [ ] **Step 2: Borrar el archivo**

```bash
git rm Services/UpdateChecker.cs
```

- [ ] **Step 3: Compilar (verificar que no quedan referencias)**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal
```
Expected: 0 errores. (Si el compilador se queja de `UpdateInfo` u otro símbolo huérfano, eliminar esa referencia.)

- [ ] **Step 4: Smoke-test funcional**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" publish "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "C:\Users\zorro\Proyectos\claudebar-win\publish" --nologo -v minimal
Start-Process "C:\Users\zorro\Proyectos\claudebar-win\publish\ClaudeBarWin.exe"
```
Expected: la app arranca; el menú click-derecho muestra "Buscar actualizaciones" (la comprobación silenciosa contra el appcast aún no existe → fallará en silencio, que es lo correcto). Cerrar la app tras comprobar.

- [ ] **Step 5: Commit**

```bash
git add TrayAppContext.cs
git commit -m "refactor: retirar UpdateChecker, AppVersion/RepoUrl local en el tray"
```

---

### Task 5: Instalador Inno Setup

**Files:**
- Create: `installer/ClaudeBarWin.iss`

- [ ] **Step 1: (Si falta) instalar Inno Setup 6**

Comprobar:
```powershell
Test-Path "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```
Si es `False`, instalar Inno Setup 6 (winget: `winget install JRSoftware.InnoSetup`, o descarga de jrsoftware.org). **Confirmar con Yovan antes de instalar software.**

- [ ] **Step 2: Crear el script del instalador**

Create `installer/ClaudeBarWin.iss`:
```iss
; Instalador per-user de ClaudeBar for Windows (sin admin).
; La versión llega por /DMyVersion=x.y.z desde scripts/release.ps1.
#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}
AppName=ClaudeBar for Windows
AppVersion={#MyVersion}
AppPublisher=Yovan Castro
AppPublisherURL=https://github.com/Yovancas/claudebar-win
DefaultDirName={localappdata}\Programs\ClaudeBarWin
DefaultGroupName=ClaudeBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ClaudeBarWin-Setup-{#MyVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Detecta/cierra la instancia en marcha por el mutex de instancia única de la app.
CloseApplications=yes
RestartApplications=no
AppMutex=ClaudeBarWin_SingleInstance

[Files]
Source: "..\publish\ClaudeBarWin.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"
Name: "{userdesktop}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; Flags: unchecked

[Run]
; Relanzar tras instalar (interactivo y silencioso/auto-update).
Filename: "{app}\ClaudeBarWin.exe"; Description: "Iniciar ClaudeBar"; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "{app}\ClaudeBarWin.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallDelete]
; Solo limpia la carpeta de la app. NO toca %APPDATA%\ClaudeBarWin (config/history.db).
Type: filesandordirs; Name: "{app}"
```

- [ ] **Step 3: Compilar el instalador (necesita `publish/ClaudeBarWin.exe` de la Task 4 step 4)**

Run:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "C:\Users\zorro\Proyectos\claudebar-win\installer\ClaudeBarWin.iss" /DMyVersion=0.1.0
```
Expected: genera `dist\ClaudeBarWin-Setup-0.1.0.exe`. Ejecutarlo a mano una vez para verificar que instala en `%LocalAppData%\Programs\ClaudeBarWin` y la app arranca.

- [ ] **Step 4: Ignorar artefactos de build y commit**

Edit `.gitignore`, añadir:
```
/dist/
```
```bash
git add installer/ClaudeBarWin.iss .gitignore
git commit -m "build: instalador Inno Setup per-user"
```

---

### Task 6: Script de release `scripts/release.ps1`

**Files:**
- Create: `scripts/release.ps1`

- [ ] **Step 1: Crear el script**

Create `scripts/release.ps1`:
```powershell
#requires -Version 7
# Release de ClaudeBar: bump version -> publish -> instalador -> appcast firmado -> (release GitHub).
# Uso:  pwsh scripts/release.ps1 -Version 0.1.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$Publish   # sin este flag, NO sube el release a GitHub (dry-run local)
)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$iscc   = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$keys   = Join-Path $repo ".sparkle-keys"
$csproj = Join-Path $repo "ClaudeBarWin.csproj"
$dist   = Join-Path $repo "dist"

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
& $iscc (Join-Path $repo "installer\ClaudeBarWin.iss") "/DMyVersion=$Version"
if ($LASTEXITCODE) { throw "ISCC falló" }
$setup = Join-Path $dist "ClaudeBarWin-Setup-$Version.exe"

# 4) Appcast firmado (Ed25519). base-url = assets de la release del tag.
netsparkle-generate-appcast `
    --binaries $dist `
    --search-binary-subdirectories false `
    --base-url "https://github.com/Yovancas/claudebar-win/releases/download/v$Version/" `
    --key-path $keys `
    --output-directory $dist
if ($LASTEXITCODE) { throw "generate-appcast falló" }
Write-Host "OK -> $setup  +  $dist\appcast.xml"

# 5) Publicar en GitHub (LÍNEA ROJA: solo con -Publish y tras OK de Yovan)
if ($Publish) {
    gh release create "v$Version" $setup (Join-Path $dist "appcast.xml") `
        --repo Yovancas/claudebar-win --title "v$Version" --notes-file (Join-Path $dist "release-notes.md")
} else {
    Write-Host "DRY-RUN: no se ha subido nada. Revisa $dist y relanza con -Publish para release." -ForegroundColor Yellow
}
```

- [ ] **Step 2: Dry-run (sin publicar)**

Run:
```powershell
pwsh "C:\Users\zorro\Proyectos\claudebar-win\scripts\release.ps1" -Version 0.1.1
```
Expected: produce `dist\ClaudeBarWin-Setup-0.1.1.exe` + `dist\appcast.xml` firmado, y el mensaje "DRY-RUN: no se ha subido nada". Abrir `appcast.xml` y verificar que tiene `<enclosure ... sparkle:edSignature="...">` y la URL apuntando a `releases/download/v0.1.1/`.

- [ ] **Step 3: Commit**

```bash
git add scripts/release.ps1
git commit -m "build: script de release (publish + instalador + appcast firmado)"
```

---

### Task 7: Verificación end-to-end del update (LÍNEA ROJA — requiere OK de Yovan)

**Files:** ninguno (validación operativa).

> Publicar releases en GitHub gasta/publica → **parar aquí y pedir OK a Yovan**. Pasos cuando lo dé:

- [ ] **Step 1:** Crear un `dist\release-notes.md` con el changelog de 0.1.1.
- [ ] **Step 2:** Publicar la base: instalar en la máquina el `Setup-0.1.0` (queda como versión instalada "vieja").
- [ ] **Step 3:** Subir la release nueva: `pwsh scripts/release.ps1 -Version 0.1.1 -Publish`.
- [ ] **Step 4:** En la app v0.1.0 instalada: menú → "Buscar actualizaciones" → debe detectar 0.1.1, mostrar changelog, descargar, **verificar firma**, instalar en silencio y relanzar en 0.1.1.
- [ ] **Step 5:** Confirmar que `%APPDATA%\ClaudeBarWin\config.json` + `history.db` siguen intactos tras el update.
- [ ] **Step 6 (negativo):** Editar a mano el `appcast.xml` (cambiar un byte de la firma), reapuntar la app a ese feed de prueba y confirmar que NetSparkle **rechaza** el update (SecurityMode.Strict).

---

### Task 8: Docs + winget

**Files:**
- Modify: `README.md`, `README.es.md`
- Modify: `winget/Yovancas.ClaudeBarWin.installer.yaml` (cuando se publique la primera release con instalador)

- [ ] **Step 1:** En ambos README, en la sección Install, sustituir "descarga el .exe portable" por "descarga e instala `ClaudeBarWin-Setup-x.y.z.exe`" y añadir una línea: "Las actualizaciones se aplican solas desde la app (menú → Buscar actualizaciones)".
- [ ] **Step 2 (diferido):** Preparar un manifest winget de tipo `installer` (`InstallerType: inno`, `InstallerSwitches.Silent: /VERYSILENT /SP-`) para reemplazar el `portable` actual. No tocar el PR #380749 en curso; este manifest es para la primera release con instalador.
- [ ] **Step 3: Commit**
```bash
git add README.md README.es.md winget/
git commit -m "docs: instalación por instalador + auto-update; manifest winget installer"
```

---

## Self-Review

- **Cobertura del spec:** servicio NetSparkle (T2), cableado/retirada del update manual (T3-T4), instalador Inno per-user que preserva %APPDATA% (T5), pipeline de release con appcast firmado (T6), verificación incl. firma inválida y persistencia de datos (T7), winget installer + docs (T8), claves Ed25519 fuera del repo (T0). Cubierto.
- **Placeholders:** el único valor "a rellenar" es `PublicKeyBase64` (T2), que es un valor **generado** por el comando de la T0 (no inventable); el comando que lo produce está especificado. `release-notes.md` se crea en T7. Sin otros placeholders.
- **Consistencia de tipos:** `SparkleUpdateService` expone `AvailableTag` (string?), `AvailabilityChanged` (event Action), `StartLoop()`, `CheckInteractive()` — usados con esos mismos nombres en T3. `AppVersion`/`RepoUrl` definidos en T4 y usados en `ShowAbout`. OK.
- **Riesgo conocido:** los nombres exactos del evento/propiedad de NetSparkle (`UpdateDetected`, `e.LatestVersion.Version`) pueden variar según la versión 2.x del paquete; T2 step 2 indica verificarlo contra los símbolos restaurados y ajustar.
