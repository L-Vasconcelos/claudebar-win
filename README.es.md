# ClaudeBar for Windows

*[English](README.md)*

Monitor en la **bandeja del sistema** de Windows para tu uso de **Claude Code** — un equivalente
Windows del [ClaudeBar](https://github.com/tddworks/ClaudeBar) de macOS. Muestra tu **cuota real
de 5h / 7d**, predice cuándo te quedarás sin cuota y grafica tu uso en el tiempo.

C#/.NET 9 + WinForms. Sin dependencias externas salvo `Microsoft.Data.Sqlite`. Enfoque de datos
inspirado en [CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor)
y [ccstatusline](https://github.com/sirmalloc/ccstatusline); ideas de UI de
[steipete/CodexBar](https://github.com/steipete/CodexBar).

## Capturas

<p align="center">
  <img src="assets/dashboard-dark.png" alt="Dashboard — tema oscuro" width="300">
  <img src="assets/dashboard-light.png" alt="Dashboard — tema claro" width="300">
  <img src="assets/dashboard-cli.png" alt="Tema CLI — gráfica Cuota %" width="300">
</p>

Las barras de 5h/7d y sus valores se colorean por **ritmo** (pace), la gráfica alterna entre
**Gasto $** (apilado por modelo) y **Cuota %** (utilización real en el tiempo), y el panel se
auto-ajusta a las secciones que actives.

**Arrástralo donde quieras y ajústale la opacidad** — el panel es un widget movible y semitransparente:

<p align="center">
  <img src="assets/move.gif" alt="Arrastra el panel por la pantalla" width="380">
  <img src="assets/opacity.gif" alt="Opacidad ajustable" width="380">
</p>

Icono de bandeja, por estado / ritmo:

<p align="center"><img src="assets/tray-icons.png" alt="Insignias del icono" width="360"></p>

<sub>Las capturas/animaciones usan datos de demo sintéticos.</sub>

## Qué muestra

- **Icono de bandeja** con la ventana más cargada (sesión 5h / semana 7d), coloreado
  (🟢 ok · 🟠 ≥70% · 🔴 ≥90%). El icono puede mostrar el **porcentaje**, el **ritmo** o **ambos**.
- **Tooltip** con el % y la cuenta atrás de reset de cada ventana.
- **Dashboard** (clic en el icono):
  - Barras de **5h** y **7d** con **% real** y *"resetea en Xh Ym"*, cada una coloreada por su **ritmo**.
  - **Línea de pace** — ritmo vs ideal, ETA y ⚠ si proyecta agotarse antes del reset.
  - Límites semanales por modelo (Opus/Sonnet) cuando aplican.
  - **Gráfica de uso** con toggle `Gasto $` ↔ `Cuota %`:
    - **Gasto $** — áreas apiladas de coste-equivalente por modelo (de los transcripts locales).
    - **Cuota %** — tu utilización real en el tiempo, con selector `5h`/`7d`.
    - Rangos: últimas **1H / 5H / 24H / 7D / 30D**.
  - **Gasto estimado** por modelo (7d) e indicador de **estado del servicio** de Anthropic.
- **Notificaciones proactivas**: hitos de uso (25/50/75/95%, 🟢→🔴) y aviso de ritmo cuando
  proyectas agotar una ventana antes de su reset.
- **Temas** (Sistema / Oscuro / Claro / CLI + importar `.itermcolors`) y **9 idiomas**
  (Sistema + English, Español, Nederlands, Français, Deutsch, 日本語, 한국어, 繁體中文) — ambos
  por defecto siguen tu configuración de Windows.
- Todo configurable desde el **menú de click derecho**.

## De dónde sale el dato

1. **Cuota real (principal):** `GET https://api.anthropic.com/api/oauth/usage` con tu token OAuth
   local. Devuelve `five_hour` / `seven_day` con `utilization` (%) y `resets_at` — *el mismo
   límite que respeta Claude Code*. Ante un 429 hace backoff y sirve el último dato bueno.
2. **Histórico de % real:** cada poll con éxito se guarda en SQLite (`%APPDATA%\ClaudeBarWin\history.db`)
   para que la gráfica `Cuota %` muestre tu utilización real en el tiempo. La API solo da un
   snapshot, así que el histórico empieza vacío y se llena al usarlo.
3. **Pace:** *ritmo* = usado% vs el ideal según el tiempo transcurrido de la ventana (funciona desde
   el minuto uno); *ETA* extrapola con la pendiente reciente del histórico de %.
4. **Refresh de token:** si el token local caducó, hace `POST platform.claude.com/v1/oauth/token`
   (client_id público de Claude Code) y reescribe `~/.claude/.credentials.json` preservando el resto.
   Fallback a `claude -p .` headless. Solo se dispara en expiry.
5. **Estado del servicio:** `GET status.claude.com/api/v2/status.json` (sin auth).
6. **Gasto estimado (secundario):** parsea los `.jsonl` locales (método `ccusage`) → USD-equivalente
   por modelo. Es una *estimación* de coste por API, no lo que cobra tu suscripción.

> El token solo se usa para leer **tu propia** cuota. No se guarda, loguea ni envía a ningún otro sitio.

## Instalación

**Opción 1 — Descargar (recomendado)**
Descarga y ejecuta `ClaudeBarWin-Setup-x.y.z.exe` de la [última release](https://github.com/Yovancas/claudebar-win/releases/latest).
Se instala por usuario (sin admin), autocontenido — **no requiere .NET**. El icono aparece en la bandeja
(Windows 11: arrástralo fuera del desbordamiento `^` para fijarlo). **Las actualizaciones se instalan solas** —
la app comprueba al arrancar, o las lanzas desde el menú de click derecho → *Buscar actualizaciones*.

> Windows SmartScreen puede avisar porque el .exe no está firmado → **Más información → Ejecutar de todas formas**.

**Opción 2 — winget**
```powershell
winget install Yovancas.ClaudeBarWin
```
*(Disponible cuando el manifest se mergee en el repo comunitario de winget.)*

**Opción 3 — compilar desde el código** — ver [Compilar desde el código](#compilar-desde-el-código).

Arranque automático: click derecho en el icono → **Ajustes → Iniciar con Windows**.

## Requisitos

- **Windows 10/11 (x64)**.
- **Claude Code** (CLI o app) instalado y con sesión iniciada — la app lee tu token OAuth local
  (`~/.claude/.credentials.json`) para sacar tu cuota real. Nada sale de tu máquina.
- Nada más para ejecutar el build de la release. Para compilar: **.NET SDK 9** (vale user-local
  en `%USERPROFILE%\.dotnet`, sin admin).

## Configuración (todo desde el click derecho)

```
Dashboard
Actualizar ahora
Ventana del panel ▶   Posición (esquinas · centro · arrastrar) · ☑ Fijado · ☑ Siempre encima · Opacidad ▶
Frecuencia ▶          30s · 1min · 5min · 15min
Notificaciones ▶      ☑ Activadas · Avisar al ☑25% ☑50% ☑75% ☑95%
Umbral de color ▶     70/90 · 80/95 · 60/85
Ajustes ▶             ☑ Mostrar gasto estimado · ☑ Mostrar estado del servicio · ☑ Gráfica de uso
                      Modo de icono ▶ % / ▲ / % ▲  ·  ☑ Avisos de ritmo  ·  ☑ Iniciar con Windows
                      Tema ▶ Sistema/Oscuro/Claro/CLI · Importar .itermcolors…
                      Idioma ▶ (Sistema + 8) · Editar config… · Abrir carpeta de datos
Salir
```

Click derecho en el propio dashboard abre el mismo menú. Los submenús abren hacia la izquierda para
quedarse en el monitor primario. "Iniciar con Windows" crea/borra un acceso directo en la carpeta de
Inicio (sin tocar el registro). Ajustes avanzados en `%APPDATA%\ClaudeBarWin\config.json`.

## Compilar desde el código

Requiere el **.NET SDK 9** (vale user-local en `%USERPROFILE%\.dotnet`, sin admin):

```powershell
git clone https://github.com/Yovancas/claudebar-win.git
cd claudebar-win
.\run.ps1            # build + run
.\run.ps1 publish    # publish\ClaudeBarWin.exe autocontenido (no requiere .NET)
```

## Comandos útiles

| Comando | Qué hace |
|---|---|
| `.\run.ps1` | Compila y ejecuta (debug) |
| `.\run.ps1 publish` | Exe autocontenido en `publish\` |
| `ClaudeBarWin.exe --report` | Vuelca la cuota + pace a consola/`%TEMP%` (sin GUI) |
| `ClaudeBarWin.exe --render-test` | Renderiza el dashboard a `%TEMP%\claudebar-render` |
| `ClaudeBarWin.exe --render-demo` | Renderiza las capturas del README (datos demo) |
| `ClaudeBarWin.exe --db-test` | Prueba la base SQLite del histórico |
| `ClaudeBarWin.exe --dump-menu` | Imprime la estructura del menú |

Todo lo demás está en el **menú de click derecho** (icono o panel). Los ajustes se guardan en
`%APPDATA%\ClaudeBarWin\config.json`; el histórico de % real en `%APPDATA%\ClaudeBarWin\history.db`.

## Desinstalar

- Sal desde el menú de bandeja (**Salir**) y borra `ClaudeBarWin.exe`.
- Borra ajustes + histórico: elimina la carpeta `%APPDATA%\ClaudeBarWin\`.
- Si activaste *Iniciar con Windows*, desactívalo antes (o borra el acceso directo de `shell:startup`).
- Si lo instalaste por winget: `winget uninstall Yovancas.ClaudeBarWin`.

## Créditos

Inspirado por [ClaudeBar](https://github.com/tddworks/ClaudeBar) (macOS),
[CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor),
[ccstatusline](https://github.com/sirmalloc/ccstatusline) y
[CodexBar](https://github.com/steipete/CodexBar). Sin afiliación con Anthropic.

## Licencia

[MIT](LICENSE)
