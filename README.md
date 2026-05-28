# ClaudeBar for Windows

Un equivalente Windows del [ClaudeBar](https://github.com/tddworks/ClaudeBar) de macOS:
una app de **bandeja del sistema** que muestra tu **cuota real de Claude Code** en tiempo real.

C#/.NET 9 + WinForms, sin dependencias externas. Enfoque de datos inspirado en
[CodeZeno/Claude-Code-Usage-Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor).

## Qué muestra

- **Icono de bandeja** con el % más alto (sesión 5h o semana 7d), coloreado:
  🟢 ok · 🟠 alto (≥70%) · 🔴 crítico (≥90%). `!` gris = sin datos/sesión caducada.
- **Tooltip** con % y cuenta atrás de reset de cada ventana.
- **Dashboard** (clic en el icono): barras de **5h** y **7d** con **% real** y
  **"resetea en Xh Ym"**, límites semanales por modelo (Opus/Sonnet) si aplican, y un
  **gasto estimado** local por modelo (lo que CodeZeno no muestra).
- **Notificaciones por hitos** al cruzar 25 / 50 / 75 / 95% (configurables), con
  indicador de color que escala 🟢 → 🟡 → 🟠 → 🔴 según se acerca al 100%.
- **Relanzar el exe** abre el dashboard (instancia única).

## De dónde sale el dato

1. **Cuota real (principal):** `GET https://api.anthropic.com/api/oauth/usage` con tu
   token OAuth local (`Authorization: Bearer …` + `anthropic-beta: oauth-2025-04-20`).
   Devuelve `five_hour` / `seven_day` con `utilization` (%) y `resets_at`. Es **tu límite
   real**, el mismo que respeta Claude Code.
2. **Refresh de token:** si el token local está caducado, hace `POST platform.claude.com/v1/oauth/token`
   (`grant_type=refresh_token`, client_id de Claude Code) y reescribe `~/.claude/.credentials.json`
   preservando el resto. Si falla, fallback a `claude -p .` headless. Solo se dispara en expiry.
3. **Estado del servicio:** `GET status.claude.com/api/v2/status.json` (Statuspage, sin auth) →
   indicador ● operativo/degradado/caído en el dashboard.
4. **Gasto estimado (secundario):** parsea los `.jsonl` locales (método `ccusage`) →
   coste-equivalente USD por modelo en la ventana de 7d. Es una **estimación** de lo que
   costaría por API, no lo que cobra tu suscripción.

> El token solo se usa para leer **tu propia** cuota. No se guarda, loguea ni envía a
> ningún otro sitio.

## Configuración (todo desde el click derecho)

Click derecho en el icono de la bandeja:

```
Dashboard
Actualizar ahora
Ventana del panel ▶             Posición (abajo/arriba dcha/izq · centro · arrastrar)
                                ☑ Fijado (no se cierra solo)  ·  ☑ Siempre encima
Frecuencia de actualización ▶   30s · 1min · 5min · 15min   (radio)
Notificaciones ▶                ☑ Activadas
                                Avisar al llegar a…  ☑25% ☑50% ☑75% ☑95%
Umbral de color ▶               70/90 (def.) · 80/95 · 60/85   (radio)
Ajustes ▶                       ☑ Mostrar gasto estimado · ☑ Mostrar estado del servicio
                                ☑ Iniciar con Windows
                                Tema ▶  Sistema · Oscuro · Claro · CLI · Importar .itermcolors…
                                Idioma ▶  Sistema + English/Español/Nederlands/Français/
                                          Deutsch/日本語/한국어/繁體中文
                                Editar config (avanzado)… · Abrir carpeta de datos
Salir
```

Idioma y Tema por defecto = **Sistema** (siguen el idioma de Windows y el modo claro/oscuro).
Los submenús abren hacia la izquierda para no salirse al segundo monitor.

"Iniciar con Windows" crea/borra un acceso directo en la carpeta de Inicio del usuario
(sin tocar el registro, reversible). Los cambios se aplican al instante.

### config.json (avanzado)

`%APPDATA%\ClaudeBarWin\config.json` (menú → *Editar config (avanzado)…*):

```json
{
  "RefreshSeconds": 60,
  "WarnThresholdPct": 70,
  "CriticalThresholdPct": 90,
  "NotificationsEnabled": true,
  "NotifyMilestones": [25, 50, 75, 95],
  "ShowSpendEstimate": true,
  "SpendWindowDays": 7
}
```

Los cambios se recogen en el siguiente refresco (o *Actualizar ahora*).

## Compilar / ejecutar

Requiere .NET SDK 9 (instalación user-local en `%USERPROFILE%\.dotnet` vale, sin admin).

```powershell
.\run.ps1            # build + run
.\run.ps1 publish    # genera publish\ClaudeBarWin.exe autocontenido (sin .NET instalado)
```

Modos diagnóstico:

```powershell
ClaudeBarWin.exe --report        # vuelca la cuota actual a consola/temp (sin GUI)
ClaudeBarWin.exe --render-test   # renderiza icono+dashboard a %TEMP%\claudebar-render
```

Para arranque automático: acceso directo a `publish\ClaudeBarWin.exe` en `shell:startup`.

## Notas

- En Windows 11 los iconos de bandeja nuevos van al desbordamiento (`^`); arrástralo a la
  barra para fijarlo.
- El refresh vía `claude -p .` solo se dispara si el token está caducado; en un equipo con
  Claude Code abierto el token se mantiene fresco solo y casi nunca se ejecuta.

## Roadmap

- Soporte Codex/ChatGPT (segundo proveedor, como CodeZeno).
- Cuenta atrás en el propio icono / mini-widget anclado a la barra.
- Temas e import de `.itermcolors` (como el ClaudeBar original).
