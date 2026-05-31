# Diseño — Auto-update en Windows (ClaudeBarWin)

Fecha: 2026-06-01
Estado: aprobado (brainstorm) — pendiente de plan de implementación

## Objetivo

Reemplazar el update **semi-manual** actual (comprobar GitHub Releases → descargar el `.exe` a
`Downloads` → reemplazar a mano) por un **auto-update real**: la app comprueba, descarga, **verifica
la firma**, instala y se relanza sola, como hacen Sparkle en macOS (Notchi / Vibe Notch).

Enfoque elegido (brainstorm): **NetSparkleUpdater** (Sparkle reimplementado 100% en C#) con
**appcast firmado EdDSA (Ed25519)**, distribuyendo un **instalador Inno Setup** a través de **GitHub
Releases**. **Sin firma Authenticode** de momento (SmartScreen avisará pero deja continuar; se añade
un certificado más adelante si hace falta).

Razón: NetSparkle nos da el canal firmado estilo Sparkle (lo que usan las apps comparables) sin meter
una DLL nativa (descartado WinSparkle); el instalador da experiencia "pro" (instalar/desinstalar,
`winget upgrade`) y hace el reemplazo del exe limpio mientras la app está cerrada.

## Decisiones del brainstorm

- **Mecanismo:** `NetSparkleUpdater` (C# puro), no WinSparkle (nativo) ni Velopack ni updater casero.
- **Formato de entrega:** **instalador Inno Setup** (`ClaudeBarWin-Setup-x.y.z.exe`), no exe portable suelto.
- **Feed:** **GitHub Releases**, sin web propia. AppCast en `releases/latest/download/appcast.xml`.
- **Firma del feed:** **Ed25519 (Strict)** — clave pública embebida en la app, privada fuera del repo.
- **Firma del binario (Authenticode):** **NO por ahora.** Asumimos aviso de SmartScreen.
- **Instalación:** **per-user** (`%LOCALAPPDATA%\Programs\ClaudeBarWin`), sin admin.
- **Datos del usuario:** `%APPDATA%\ClaudeBarWin` (config.json, history.db, last-state.json) **NO se
  tocan** ni en install ni en uninstall ni en update — sobreviven a las actualizaciones.
- **Self-contained single-file** se mantiene (cada update baja ~110 MB; sin deltas — trade-off aceptado).
- **winget:** el manifest pasará de `portable` a `installer` (`InstallerType: inno`) en el próximo release.

## Arquitectura

### Componente nuevo: `Services/SparkleUpdateService.cs`
Envoltorio único sobre `SparkleUpdater`. Aísla a NetSparkle del resto de la app.

- Construcción:
  ```csharp
  _sparkle = new SparkleUpdater(
      "https://github.com/Yovancas/claudebar-win/releases/latest/download/appcast.xml",
      new Ed25519Checker(SecurityMode.Strict, PublicKeyBase64))   // pública embebida (const)
  {
      UIFactory = new NetSparkleUpdater.UI.WinForms.UIFactory(appIcon),
      RelaunchAfterUpdate = false,        // lo relanza el instalador, no NetSparkle
      CustomInstallerArguments = "/VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART",
  };
  ```
- API expuesta a `TrayAppContext`:
  - `Task CheckQuietly()` → `CheckForUpdatesQuietly()` (al arrancar; marca el menú si hay versión nueva).
  - `Task CheckInteractive()` → `CheckForUpdatesAtUserRequest()` (desde el menú; muestra la UI de NetSparkle con changelog/descarga/instalación).
  - `void StartLoop()` → `_sparkle.StartLoop(true, TimeSpan.FromHours(6))` (check inicial + cada 6 h).
  - Evento `UpdateAvailable?` (relayado desde `UpdateDetected`) para que el menú muestre "⬇ {tag}".
- Paquetes NuGet: `NetSparkleUpdater.SparkleUpdater` + `NetSparkleUpdater.UI.WinForms`.
- La clave **pública** Ed25519 (base64) vive como constante en este archivo.

### Cambios en `TrayAppContext.cs`
- Sustituir el flujo propio (`CheckUpdatesAsync` + descarga a Downloads) por llamadas a `SparkleUpdateService`.
- Ctor: `_updates.StartLoop()` en vez de `_ = CheckUpdatesAsync(silent: true)`.
- Menú *"Comprobar actualizaciones"* → `await _updates.CheckInteractive()`.
- Mantener el ítem *"⬇ Update available {tag}"* alimentado por el evento `UpdateAvailable`.

### Retirada de `Services/UpdateChecker.cs`
- Se elimina (o queda como muerto) el `UpdateChecker` actual: NetSparkle cubre check + descarga +
  verificación + instalación. Se borran los strings de UI ya no usados (`UpdateDownloadedFmt`, etc.)
  o se reutilizan en la UI de NetSparkle vía `IUIFactory` si se quiere localización.

## Instalador — `installer/ClaudeBarWin.iss` (Inno Setup)

- `AppId` estable (GUID), `AppName=ClaudeBar for Windows`, `AppVersion` inyectada por el script de release.
- `PrivilegesRequired=lowest`, `DefaultDirName={localappdata}\Programs\ClaudeBarWin` (per-user, sin admin).
- `[Files]`: el único `ClaudeBarWin.exe` self-contained.
- `[Icons]`: acceso directo en Menú Inicio. **No** gestionar "Iniciar con Windows" desde el instalador —
  lo sigue gestionando `StartupManager` dentro de la app (su `.lnk` apunta a `Environment.ProcessPath`,
  que tras el update sigue siendo la misma ruta instalada → no se rompe).
- `CloseApplications=yes` + `RestartApplications=no`: cierra ClaudeBar antes de reemplazar el exe.
- `[Run]`: relanzar tras instalar — `Filename: {app}\ClaudeBarWin.exe; Flags: nowait postinstall skipifsilent runascurrentuser`
  y, para el modo silencioso del auto-update, un `[Run]` equivalente sin `skipifsilent` para que NetSparkle
  obtenga el relanzamiento. (Detalle exacto de flags a fijar en el plan.)
- **No** crea ni borra `%APPDATA%\ClaudeBarWin`. El `[UninstallDelete]` solo limpia `{app}`.

## Pipeline de release — `scripts/release.ps1` (nuevo)

Hoy no hay CI; el release es manual. Este script lo automatiza en local (Windows de Yovan):

1. Leer versión del parámetro (`-Version 0.1.1`) y **bumpear** `<Version>` en `ClaudeBarWin.csproj`.
2. `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish` → `ClaudeBarWin.exe`.
3. Compilar el instalador: `ISCC.exe installer\ClaudeBarWin.iss /DMyVersion=0.1.1` → `dist\ClaudeBarWin-Setup-0.1.1.exe`.
4. Generar y **firmar** el appcast:
   `netsparkle-generate-appcast --binaries dist --search-binary-subdirectories=false
    --base-url https://github.com/Yovancas/claudebar-win/releases/download/v0.1.1/
    --key-path .sparkle-keys` → `dist\appcast.xml` (firma Ed25519 + changelog desde un `.md`/release body).
5. Crear el GitHub Release `v0.1.1` (`gh release create`) subiendo **`ClaudeBarWin-Setup-0.1.1.exe`** y
   **`appcast.xml`** como assets. (Este paso es push/publicación → **requiere OK explícito de Yovan**.)

- Clave **privada** Ed25519 en `.sparkle-keys/` (o variable de entorno `SPARKLE_PRIVATE_KEY`),
  **gitignored**, nunca commiteada. Generada una vez con `netsparkle-generate-appcast --generate-keys`.
- Herramientas de build (en la máquina de Yovan): **Inno Setup** (`ISCC.exe`) y el dotnet tool
  `NetSparkleUpdater.Tools.AppCastGenerator`.

## Flujo de update en runtime

1. **Arranque:** `StartLoop(true, 6h)` hace un check; si hay versión nueva, el evento `UpdateAvailable`
   marca el menú "⬇ {tag}". Sin diálogos intrusivos (respeta el modo silencioso del arranque).
2. **Periódico:** cada 6 h re-comprueba.
3. **Usuario** (menú o clic en "⬇"): NetSparkle muestra su ventana → changelog (del appcast) → descarga el
   instalador → **verifica la firma Ed25519** (Strict) → ejecuta el instalador silencioso → Inno cierra
   la app, reemplaza el exe y la **relanza**.

## Manejo de errores

- **Sin red / appcast inaccesible:** NetSparkle falla en silencio en el check quiet; no rompe la app.
- **Firma inválida o ausente:** `SecurityMode.Strict` → NetSparkle **rechaza** y no instala.
- **App en ejecución durante el reemplazo:** Inno `CloseApplications=yes` la cierra antes; el Mutex
  single-instance se libera al salir. Tras instalar, `[Run]` relanza.
- **Update a medias:** el instalador es transaccional; si falla, queda la versión anterior instalada.
- **Datos:** nunca se tocan; un downgrade/upgrade conserva config.json + history.db.

## Seguridad

- Integridad del binario garantizada por **firma Ed25519 del appcast** (la app trae la pública; la
  privada nunca sale de la máquina de Yovan).
- **Sin Authenticode** → Windows SmartScreen mostrará "editor desconocido" al instalar/actualizar; se
  acepta por ahora. Añadir cert OV/EV en el futuro elimina el aviso sin tocar el resto del diseño.
- La clave privada Ed25519 es el secreto crítico: si se filtra, alguien podría firmar un update malicioso.
  Se guarda fuera del repo y se documenta en el README de release.

## Migración portable → instalado

- Yovan es hoy el único usuario real; migración trivial (instalar la primera versión con instalador).
- El **PR de winget `portable` (#380749)** queda obsoleto para la nueva versión; cuando se publique la
  primera release con instalador, se prepara un manifest `installer` nuevo (no se toca el PR actual aquí).
- Los datos en `%APPDATA%\ClaudeBarWin` se conservan al pasar de portable a instalado (misma carpeta).

## Testing

- **Ciclo completo (manual, en la máquina de Yovan):** publicar una `v0.1.1` de prueba, tener instalada
  `v0.1.0`, verificar detección → descarga → verificación de firma → instalación silenciosa → relanzado.
- **Firma inválida:** alterar el appcast/binario y comprobar que Strict **rechaza** el update.
- **Sin red:** desconectar y confirmar que el arranque no se bloquea ni muestra error intrusivo.
- **Persistencia de datos:** confirmar que `config.json` + `history.db` siguen tras un update.
- **Unitario (donde aplique):** `SparkleUpdateService` con un appcast de fixture y clave de test
  (verificar parseo de versión nueva/igual/anterior). NetSparkle en sí no se testea (lib de terceros).

## Dependencias nuevas

- NuGet: `NetSparkleUpdater.SparkleUpdater`, `NetSparkleUpdater.UI.WinForms` (entran en el single-file).
- Build (máquina de Yovan): Inno Setup 6 (`ISCC.exe`); dotnet tool `NetSparkleUpdater.Tools.AppCastGenerator`.

## Puntos abiertos / trade-offs

- **Tamaño:** self-contained ⇒ ~110 MB por update (sin deltas). Alternativa futura: build
  framework-dependent (más pequeño, requiere .NET 9 runtime en destino). Se mantiene self-contained por
  simplicidad y porque hoy "no requiere .NET instalado".
- **Authenticode** pendiente (coste/decisión futura).
- **Localización** de la UI de NetSparkle: usa su UI por defecto (inglés). Si se quiere en los 9 idiomas
  de ClaudeBar habría que implementar un `IUIFactory` propio — fuera de alcance de esta primera versión.
