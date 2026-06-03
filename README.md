# ClaudeBar for Windows

*[Español](README.es.md)*

A Windows **system-tray** monitor for your **Claude Code** usage — a Windows take on the
macOS [ClaudeBar](https://github.com/tddworks/ClaudeBar). It shows your **real 5h / 7d quota**,
predicts when you'll run out, and charts your usage over time.

C#/.NET 9 + WinForms. No external dependencies except `Microsoft.Data.Sqlite`.
Data approach inspired by [CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor)
and [ccstatusline](https://github.com/sirmalloc/ccstatusline); UI ideas from
[steipete/CodexBar](https://github.com/steipete/CodexBar).

## Screenshots

<p align="center">
  <img src="assets/dashboard-dark.png" alt="Dashboard — dark theme" width="300">
  <img src="assets/dashboard-light.png" alt="Dashboard — light theme" width="300">
  <img src="assets/dashboard-cli.png" alt="CLI theme — Quota % chart" width="300">
</p>

The 5h/7d bars and their values are coloured by **pace** (burn-rate), the chart toggles between
**Spend $** (stacked by model) and **Quota %** (real utilisation over time), and the panel auto-sizes
to whatever sections you enable.

Everything expanded at once — the live-session **mascot**, both quota bars, per-model spend, and the
usage chart:

<p align="center"><img src="assets/dashboard-full.png" alt="Full dashboard with the live-session mascot" width="320"></p>

**Drag it anywhere, dial in the opacity** — the panel is a movable, see-through widget:

<p align="center">
  <img src="assets/move.gif" alt="Drag the panel anywhere on screen" width="380">
  <img src="assets/opacity.gif" alt="Adjustable window opacity" width="380">
</p>

**Microinteractions** — the panel fades and staggers in, numbers and bars tween to their targets, the
mascot blinks and spins while it works, rows light up on hover, and a quota reset gets a little flash
(all respect a *reduce motion* toggle):

<p align="center">
  <img src="assets/f3-apertura.gif" alt="Panel open: fade, staggered entry, number/bar tween" width="300">
  <img src="assets/f3-mascota.gif" alt="Mascot life: blink, braille spinner, playful verb" width="300">
</p>
<p align="center">
  <img src="assets/f3-hover.gif" alt="Hover highlight fading in over a section" width="300">
  <img src="assets/f3-celebracion.gif" alt="Quota-reset celebration flash" width="300">
</p>

Tray icon, by status / pace:

<p align="center"><img src="assets/tray-icons.png" alt="Tray icon badges" width="360"></p>

<sub>Screenshots/animations use synthetic demo data.</sub>

## What it shows

- **Tray icon** with the higher of your two windows (5h session / 7d weekly), colour-coded
  (🟢 ok · 🟠 ≥70% · 🔴 ≥90%). Icon can show the **percentage**, the **pace**, or **both**.
- **Tooltip** with each window's % and reset countdown.
- **Dashboard** (click the icon):
  - **5h** and **7d** bars with **real %** and *"resets in Xh Ym"*, each coloured by its **pace**.
  - **Pace line** — burn-rate vs the ideal, plus an ETA and a ⚠ if you're projected to run out
    before the reset.
  - Per-model weekly limits (Opus/Sonnet) when present.
  - **Usage chart** with a `Spend $` ↔ `Quota %` toggle:
    - **Spend $** — stacked area of cost-equivalent by model (from local transcripts).
    - **Quota %** — your real utilisation over time, with a `5h`/`7d` selector.
    - Ranges: last **1H / 5H / 24H / 7D / 30D**.
  - **Estimated spend** by model (7d), and an **Anthropic service-health** indicator.
- **Proactive notifications**: usage milestones (25/50/75/95%, 🟢→🔴) and a pace alert when
  you're projected to exhaust a window before its reset.
- **Themes** (System / Dark / Light / CLI + import `.itermcolors`) and **9 languages**
  (System + English, Español, Nederlands, Français, Deutsch, 日本語, 한국어, 繁體中文) — both
  default to your Windows settings.
- **Live sessions (opt-in)**: an ASCII mascot in the dashboard reacts to your Claude Code sessions
  in real time (idle / working / waiting for approval / waiting for input / compacting / ended),
  driven by Claude Code hooks over a local named pipe; the tray icon adds an amber badge when a
  session needs your attention. Toggle it from **Settings → Live sessions** — it installs/removes
  the hooks in `~/.claude/settings.json` (with a backup and a confirmation prompt).
- Everything is configurable from the in-dashboard **⚙ settings panel**.

## How the data works

1. **Real quota (primary):** `GET https://api.anthropic.com/api/oauth/usage` with your local
   OAuth token (`Authorization: Bearer …` + `anthropic-beta: oauth-2025-04-20`). Returns
   `five_hour` / `seven_day` with `utilization` (%) and `resets_at` — the *same limits Claude
   Code respects*. On HTTP 429 it backs off and serves the last good value.
2. **Real-% history:** each successful poll is sampled into SQLite (`%APPDATA%\ClaudeBarWin\history.db`)
   so the `Quota %` chart can show your true utilisation over time. The API only gives a
   snapshot, so this history starts empty and fills in as it runs.
3. **Pace:** *ritmo* = used% vs the ideal for how much of the window has elapsed (works from
   minute one); *ETA* extrapolates from the recent slope of the % history.
4. **Token refresh:** if the local token is expired, it `POST`s to
   `platform.claude.com/v1/oauth/token` (Claude Code's public client_id) and rewrites
   `~/.claude/.credentials.json`, preserving the rest. Falls back to a headless `claude -p .`.
   Only runs when expired.
5. **Service health:** `GET status.claude.com/api/v2/status.json` (no auth).
6. **Estimated spend (secondary):** parses local `.jsonl` transcripts (`ccusage` method) into a
   USD-equivalent by model. It's an *estimate* of API cost, not what your subscription charges.

> The token is only used to read **your own** usage. It is never stored, logged, or sent anywhere else.

## Install

**Option 1 — Download (recommended)**
Download and run `ClaudeBarWin-Setup-x.y.z.exe` from the [latest release](https://github.com/Yovancas/claudebar-win/releases/latest).
It installs per-user (no admin), self-contained — **no .NET required**. The icon lands in the system tray
(Windows 11: drag it out of the `^` overflow to pin it). **Updates install themselves** — the app checks on
launch, or you can trigger it from the right-click menu → *Check for updates*.

> Windows SmartScreen may warn because the .exe is unsigned → **More info → Run anyway**.

**Option 2 — winget**
```powershell
winget install Yovancas.ClaudeBarWin
```
*(Available once the manifest is merged into the winget community repo.)*

**Option 3 — build from source** — see [Build from source](#build-from-source).

Auto-start on login: right-click the tray icon → **Settings → Start with Windows**.

## Requirements

- **Windows 10/11 (x64)**.
- **Claude Code** (CLI or app) installed and signed in — the app reads your local OAuth token
  (`~/.claude/.credentials.json`) to fetch your real quota. Nothing leaves your machine.
- Nothing else to run the release build. To build from source: **.NET SDK 9** (a user-local install
  in `%USERPROFILE%\.dotnet` works, no admin).

## Configuration (in-dashboard settings panel)

Open the dashboard and click the **⚙** (top-right) — **all settings live there**, grouped:

```
Sections          ☑ Estimated spend · ☑ Service status · ☑ Usage chart
Live sessions     ☑ Mascot · Mascot size (compact/large) · ☑ Suppress when focused
                  [ Enable/Disable — installs/removes Claude Code hooks (with confirmation) ]
Notifications     ☑ Enabled · ☑ Pace alerts · milestones ☑25 ☑50 ☑75 ☑95
Update frequency  30s · 1min · 5min · 15min
Icon              mode % / ▲ / %▲ · colour threshold 70/90 · 80/95 · 60/85
Appearance        Theme System/Dark/Light/CLI · Import .itermcolors… · Position · Opacity · ☑ Pinned · ☑ Always on top
Language          System + 8
System            ☑ Start with Windows
```

The **right-click menu** (tray icon or panel) is now minimal — *Dashboard · Settings · Live sessions ·
Check for updates · Exit*. "Start with Windows" creates/removes a shortcut in the Startup folder
(no registry). Settings persist to `%APPDATA%\ClaudeBarWin\config.json`.

## Build from source

Requires the **.NET SDK 9** (a user-local install in `%USERPROFILE%\.dotnet` works, no admin):

```powershell
git clone https://github.com/Yovancas/claudebar-win.git
cd claudebar-win
.\run.ps1            # build + run
.\run.ps1 publish    # self-contained publish\ClaudeBarWin.exe (no .NET needed to run)
```

## Useful commands

| Command | What it does |
|---|---|
| `.\run.ps1` | Build and run (debug) |
| `.\run.ps1 publish` | Self-contained single-file `publish\ClaudeBarWin.exe` |
| `ClaudeBarWin.exe --report` | Print current quota + pace to console/`%TEMP%` (no GUI) |
| `ClaudeBarWin.exe --render-test` | Render the dashboard to `%TEMP%\claudebar-render` |
| `ClaudeBarWin.exe --render-demo` | Render the README screenshots (synthetic data) |
| `ClaudeBarWin.exe --render-gif` | Dump the README GIF frame sequences (synthetic data) to `%TEMP%\claudebar-gif` |
| `ClaudeBarWin.exe --db-test` | Smoke-test the SQLite history store |
| `ClaudeBarWin.exe --dump-menu` | Print the right-click menu structure |

Everything else is in the **right-click menu** (on the tray icon or the panel itself). Settings persist
to `%APPDATA%\ClaudeBarWin\config.json`; the real-% history lives in `%APPDATA%\ClaudeBarWin\history.db`.

## Uninstall

- Quit from the tray menu (**Exit**), then delete `ClaudeBarWin.exe`.
- Remove settings + history: delete the `%APPDATA%\ClaudeBarWin\` folder.
- If you enabled *Start with Windows*, untick it first (or remove the shortcut from `shell:startup`).
- Installed via winget: `winget uninstall Yovancas.ClaudeBarWin`.

## Notes

- On Windows 11 a new tray icon goes to the overflow (`^`) — drag it onto the taskbar to pin it.
- The `claude -p .` refresh only fires if the token is expired; with Claude Code running it
  stays fresh on its own and rarely runs.

## Credits

Inspired by [ClaudeBar](https://github.com/tddworks/ClaudeBar) (macOS),
[CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor),
[ccstatusline](https://github.com/sirmalloc/ccstatusline) and
[CodexBar](https://github.com/steipete/CodexBar). Not affiliated with Anthropic.

## License

[MIT](LICENSE)
