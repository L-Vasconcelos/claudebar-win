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

Todo desplegado a la vez — la **mascota** en su propia banda, las dos barras de cuota, el gasto por
modelo y la gráfica de uso:

<p align="center"><img src="assets/dashboard-full.png" alt="Dashboard completo con la mascota de sesiones en vivo" width="320"></p>

**Una sola pantalla de ajustes** — todas las opciones en una página limpia y agrupada: toggles, filas
maestra/dependiente (activa las sesiones en vivo y elige el tamaño de la mascota), selectores
segmentados, sin ruido:

<p align="center"><img src="assets/settings.png" alt="Panel de ajustes agrupado — todas las opciones en una pantalla" width="300"></p>

**Arrástralo donde quieras y ajústale la opacidad** — el panel es un widget movible y semitransparente:

<p align="center">
  <img src="assets/move.gif" alt="Arrastra el panel por la pantalla" width="380">
  <img src="assets/opacity.gif" alt="Opacidad ajustable" width="380">
</p>

**Microinteracciones** — el panel aparece con un fade y entrada escalonada, los números y las barras
hacen tween hasta su valor, la mascota parpadea y gira su spinner mientras trabaja, las filas se
realzan al pasar el cursor, y un reset de cuota recibe un pequeño destello (todo respeta el toggle de
*reducir movimiento*):

<p align="center">
  <img src="assets/f3-apertura.gif" alt="Apertura del panel: fade, entrada escalonada, tween de números/barras" width="300">
  <img src="assets/f3-mascota.gif" alt="Vida de la mascota: parpadeo, spinner braille, verbo juguetón" width="300">
</p>
<p align="center">
  <img src="assets/f3-hover.gif" alt="Realce de hover apareciendo sobre una sección" width="300">
  <img src="assets/f3-celebracion.gif" alt="Destello de celebración de reset de cuota" width="300">
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
- **Mascota**: un gato ASCII vive en el dashboard, visible por defecto (un gato *Idle* de ambiente) —
  actívala/ocúltala con **Mostrar mascota** y elige **Oculta / Compacta / Grande**.
- **Sesiones en vivo (opt-in)**: actívalas y la mascota reacciona a tus sesiones de Claude Code en
  tiempo real (inactiva / trabajando / esperando aprobación / esperando input / compactando /
  terminada), mediante hooks de Claude Code por un named pipe local; el icono de bandeja añade un
  punto ámbar cuando una sesión necesita tu atención. Se activan/desactivan en **Ajustes → Sesiones
  en vivo** — instala/quita los hooks en `~/.claude/settings.json` (con copia de seguridad y
  confirmación).
- Todo configurable desde el **panel de ajustes ⚙** del dashboard — una sola pantalla limpia y agrupada.

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

## Configuración (panel de ajustes en el dashboard)

Abre el dashboard y pulsa el **⚙** (arriba a la derecha) — **toda la configuración en una sola pantalla agrupada**:

```
Contenido panel   ☑ Gasto estimado · ☑ Estado del servicio · ☑ Gráfica de uso
Sesiones en vivo  ☑ Activadas (instala/quita los hooks de Claude Code, con confirmación)
                  Mostrar mascota · tamaño Oculta / Compacta / Grande · ☑ Silenciar si la terminal tiene foco
Notificaciones    ☑ Activadas · ☑ Avisos de ritmo · hitos ☑25 ☑50 ☑75 ☑95
Frecuencia        30s · 1min · 5min · 15min
Icono             modo % / ▲ / %▲ · umbral de color 70/90 · 80/95 · 60/85
Apariencia        Tema Sistema/Oscuro/Claro/CLI · Importar .itermcolors… · Posición · Opacidad · ☑ Fijado · ☑ Siempre encima
Idioma            Sistema + 8
Sistema           ☑ Iniciar con Windows
```

El **menú de click derecho** (icono o panel) es ahora minimal — *Dashboard · Ajustes · Sesiones en vivo ·
Buscar actualizaciones · Salir*. "Iniciar con Windows" crea/borra un acceso directo en la carpeta de
Inicio (sin tocar el registro). Los ajustes se guardan en `%APPDATA%\ClaudeBarWin\config.json`.

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
| `ClaudeBarWin.exe --render-gif` | Vuelca las secuencias de fotogramas de los GIF del README (datos demo) a `%TEMP%\claudebar-gif` |
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
