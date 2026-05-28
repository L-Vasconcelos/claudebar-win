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

Tray icon, by status / pace:

<p align="center"><img src="assets/tray-icons.png" alt="Tray icon badges" width="360"></p>

<sub>Screenshots use synthetic demo data.</sub>

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
- Everything is configurable from the **right-click menu**.

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

## Configuration (all from the right-click menu)

```
Dashboard
Refresh now
Panel window ▶        Position (corners · center · drag) · ☑ Pinned · ☑ Always on top · Opacity ▶
Update frequency ▶    30s · 1min · 5min · 15min
Notifications ▶       ☑ Enabled · Notify at ☑25% ☑50% ☑75% ☑95%
Color threshold ▶     70/90 · 80/95 · 60/85
Settings ▶            ☑ Show estimated spend · ☑ Show service status · ☑ Usage chart
                      Icon mode ▶ % / ▲ / % ▲  ·  ☑ Pace alerts  ·  ☑ Start with Windows
                      Theme ▶ System/Dark/Light/CLI · Import .itermcolors…
                      Language ▶ (System + 8) · Edit config… · Open data folder
Exit
```

Right-click the dashboard itself to open the same menu. Submenus open leftward so they stay on
the primary monitor. "Start with Windows" creates/removes a shortcut in the Startup folder
(no registry). Advanced settings live in `%APPDATA%\ClaudeBarWin\config.json`.

## Build / run

Requires the .NET SDK 9 (a user-local install in `%USERPROFILE%\.dotnet` works, no admin):

```powershell
.\run.ps1            # build + run
.\run.ps1 publish    # self-contained publish\ClaudeBarWin.exe (no .NET needed to run)
```

Diagnostic modes: `--report` (dump current usage + pace to console/temp), `--render-test`
(render the dashboard to `%TEMP%\claudebar-render`), `--db-test`, `--dump-menu`.

For autostart, use the menu's *Start with Windows*, or drop a shortcut to
`publish\ClaudeBarWin.exe` in `shell:startup`.

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
