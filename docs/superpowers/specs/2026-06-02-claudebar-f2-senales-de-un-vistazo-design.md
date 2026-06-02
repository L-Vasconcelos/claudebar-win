# ClaudeBar-win — Fase 2: Señales de un vistazo (design)

**Fecha:** 2026-06-02
**Estado:** spec en revisión (deriva del roadmap `2026-06-02-claudebar-apple-roadmap.md`, Fase 2).
**Hereda de:** Fase 1 (tokens semánticos `Theme`, `Spacing`, `ColorMath.RiskColor/Lerp`, `Typography.Mono`). Build base: `feat/live-sessions`, 76 tests verdes, tag `v0.3.2`.

> Objetivo: traducir datos que la app **ya calcula** (pace, reset, frescura) en **señales visuales de un vistazo**, dentro de la barra y el tray, con el lenguaje que hace que CodexBar/ClaudeBar-mac/ccstatusline se vean premium — sin añadir features nuevas de datos.

---

## 1. Alcance de F2

Siete señales (las del roadmap). Cada una mapeada a código real (refs del análisis multi-agente):

1. **Pace marker dentro de la barra** — línea de "dónde deberías ir" según el ritmo.
2. **Ticks de umbral** — marcas finas en Warn/Critical sobre la barra.
3. **Reset en hora local absoluta + explicación del rolling** — "resetea en 2h 13m · hoy 18:42" + qué es la ventana rodante.
4. **Estado "stale"** — "· hace N min" + marcado cuando el dato envejece.
5. **Sello "100% local"** — declaración de privacidad honesta.
6. **Tray icon adaptativo** a barra de tareas clara/oscura.
7. **Estado por forma además de color** (accesible a daltónicos).
8. ~~Barra segmentada estilo batería~~ — **descartada en F2** (decisión Yovan): la barra continua con degradado + pace marker es más legible/Apple. Se mueve a F5.

## 2. Pre-requisito: pagar deuda de duplicación (Tarea 0)

El análisis encontró que **toda la geometría de la barra está duplicada**:
- `DashboardDataView.DrawBar` (cuerpo, 5h+7d) y `DashboardHeader.DrawCriticalBar` (cabecera) son **gemelas casi idénticas**.
- `FillRounded` está copiado en **4 archivos** (`DashboardDataView` internal, `DashboardHeader` private, `TrayIconRenderer` private, + `RoundedRectPath` en `DashboardSettingsView`).
- El cálculo de contraste por luminancia `Pick(bg)` está inline en `DashboardDataView:540`.

**Decisión:** antes de añadir señales, extraer a `Services/DesignSystem.cs`:
- `Shapes.FillRounded(Graphics, Brush, Rectangle, int radius)` (versión canónica con guardas de `DashboardDataView`).
- `Shapes.RoundedRectPath(Rectangle, int radius)` (devuelve `GraphicsPath`).
- `ColorMath.Contrast(Color bg)` → `Color` (la fórmula `0.299/0.587/0.114 < 140 ? White : Black`).

Y **unificar la barra**: extraer la rutina gemela a un único `UI/dashboard/QuotaBar.cs` con
`static int Draw(Graphics g, bool draw, QuotaBarModel m, int x, int y, int w, AppConfig cfg, Strings s, Theme theme, Font label, Font small)` que **ambas** (cuerpo y cabecera) llamen. `QuotaBarModel` lleva `label`, `UsageWindow? win`, `PaceResult? pace`, `Brush fg`, `Brush dim`. Así toda señal nueva se escribe **una sola vez**.

> Invariante a respetar SIEMPRE: el patrón medir(`draw=false`)/pintar(`draw=true`) debe devolver el **mismo `y`** en ambas ramas. Toda geometría nueva que quepa dentro de `barH=11` (marker, ticks) no altera `y`; cualquier línea de texto nueva (hora de reset, sello) debe sumar su alto en ambas ramas.

## 3. Señales — diseño detallado

### 3.1 Pace marker + ticks (dentro de la barra)
- **Dato**: `PaceCalculator.For()` ya calcula `idealUsed = e*100` (la fracción de ventana transcurrida ×100) pero **lo descarta**. F2 **expone `double IdealPct`** en el record `PaceResult` y lo rellena.
- **Firma**: `QuotaBar.Draw` recibe `PaceResult? pace` (hoy las gemelas reciben solo `PaceStatus?`). Call-sites a actualizar: `DrawQuotaBody` (DataView:132/134, pasar `snap?.PaceFive`/`snap?.PaceSeven`) y la cabecera (Header:83).
- **Render** (dentro del `if(draw)` de la barra, tras el relleno):
  - **Pace marker**: línea vertical de 1–2px en `x + round(w * IdealPct/100)`, alto = `barH` + 2px de sobresalido arriba/abajo, color `theme.TextMuted` (neutro, **nunca Accent** — F1 reserva Accent para dots/tab y Warn/Critical para cuota). Un pequeño triángulo ▾ encima del marker comunica "ritmo ideal".
  - **Ticks de umbral**: muescas finas (1px) en `x + round(w*cfg.WarnThresholdPct/100)` y `...CriticalThresholdPct/100`, color `theme.Separator`. Se dibujan **después** del relleno para no quedar tapadas por las esquinas redondeadas.
- **Funciones puras testeables**: `QuotaBarGeometry.MarkerX(int x, int w, double pct)` y `TickX(...)`.

### 3.2 Reset en hora humana + explicación del rolling
- **Hoy**: solo `UsageFormat.Countdown(resetsAt, resetting)` → relativo ("2h 13m"), en 3 clones (DataView:229, Header:139, tray tooltip TrayAppContext:437).
- **F2**: nuevo `UsageFormat.ResetAbsolute(DateTimeOffset? resetsAt)` → `resetsAt.Value.ToLocalTime().ToString("ddd HH:mm")` (mismo formato que la ETA ya usa en `DrawPace`). La línea de reset pasa a `"{ResetsIn} {cd} · {abs}"` ("resetea en 2h 13m · hoy 18:42").
- **Explicación rolling**: string nuevo `Strings.RollingHint` ("ventana móvil de 5h desde tu 1ª petición, no a hora fija") en `theme.TextMuted`/`Typography.Caption`, bajo la sección Cuota o como subtítulo. Honesto: la API solo da el **fin** (reset); el inicio es `reset - 5h` (longitud fija asumida).
- **Centralizar**: tocar `UsageFormat` evita editar los 3 clones de forma divergente.

### 3.3 Estado "stale"
- **Dato**: `AppSnapshot.UsageAtUtc` (DateTime UTC) ya llega a la UI pero **no se pinta** (solo el footer muestra la hora absoluta). `_lastUsageAtUtc` solo avanza con `State==Ok`.
- **F2**: en `UsageFormat`:
  - `Relative(DateTime utc, Strings s)` → "hace N min"/"hace N s" (espejo de `Countdown`).
  - `IsStale(DateTime utc, int refreshSeconds)` → `UtcNow - utc > 3 × refreshSeconds`.
- **Footer** (`DashboardForm.cs:471-479`): el "Actualizado HH:mm:ss" pasa a "Actualizado · hace N min"; si `IsStale`, anteponer marcador en `theme.Warn`.
- **Riesgo de Kind**: `UsageCache.Load()` puede deserializar `SavedAtUtc` con `DateTimeKind.Unspecified` → normalizar a UTC al cargar antes de restar con `UtcNow`.
- No marcar stale durante los primeros ~`RefreshSeconds` tras arrancar (mientras llega la 1ª fetch).

### 3.4 Sello "100% local" (redacción honesta)
- **Verdad técnica**: la cuota se obtiene de `https://api.anthropic.com/api/oauth/usage` con el token OAuth que Claude Code guarda en `~/.claude/.credentials.json`. El spend opcional sí sale de los `.jsonl` locales.
- **Claim correcto** (no mentir): `Strings.LocalSeal` = **"Tus credenciales y datos no salen del equipo · sin telemetría"**. NO "se calcula sin internet".
- Render: `Typography.Caption` en `theme.TextMuted`, en el footer del dashboard o en el panel de Ajustes.

### 3.5 Tray icon adaptativo + forma por estado
- **Adaptativo barra clara/oscura**: hoy `RenderBadge` pinta un rectángulo opaco + texto **blanco hardcodeado** (`Brushes.White`, mal contraste en tema claro). `ThemeResolver.OsPrefersDark()` lee `AppsUseLightTheme` (tema de **apps**), NO el de la barra.
  - F2: `ThemeResolver.TaskbarIsLight()` lee `SystemUsesLightTheme` (misma clave HKCU) — controla el color de la **barra de tareas**.
  - El texto del badge usa `ColorMath.Contrast(bg)` (extraído en Tarea 0) en vez de blanco fijo.
  - Re-leer en cada `RefreshAsync` para reaccionar a cambios de tema.
- **Forma por estado (daltónicos)**: derivar `UsageStatus` (Ok/Warn/Critical) en `TrayAppContext.IconContent` y pasarlo a `RenderBadge`. Indicador de **forma** además del color, reutilizando el patrón del badge "pending" (overlay en esquina):
  - Ok → sin overlay (o punto), Warn → triángulo, Critical → cuadrado/rombo.
  - **En el dashboard** (donde hay espacio), un glifo de forma de 1 carácter junto al `%` (a 16px del tray la forma es marginal; el dashboard es el sitio realmente accesible).
- **QA**: ampliar la tira `tray-badges.png` (`Program.cs:434`) con filas para barra clara y oscura, las 3 formas y el estado stale, validadas a **16px reales**.

### 3.6 Barra segmentada estilo batería — DESCARTADA en F2
Decisión de Yovan: **no entra en F2**. La barra continua con degradado `RiskColor` + pace marker queda como diseño definitivo de la barra. La variante batería se traslada a la **Fase 5** (features). No se añade `AppConfig.BatteryStyleBar` en F2.

## 4. Criterios de aceptación
- Build 0 errores; suite **≥ 76 tests** verdes (se añaden tests nuevos, no se rompe ninguno).
- `--render-test` genera dashboard + tray con: pace marker visible, ticks de umbral, "resetea en … · hora", footer con "hace N min" + sello local, y la tira de tray cubriendo barra clara/oscura + formas + stale.
- La barra del **cuerpo** y la de la **cabecera** son idénticas en geometría (un solo `QuotaBar.Draw`).
- Sin literales de color/offset nuevos: todo vía `theme.*`, `Spacing.*`, `ColorMath.*`, `Typography.*`.
- Strings nuevos (`RollingHint`, `LocalSeal`, `AgoFormat`/`StaleLabel`, hora de reset) en **los 8 idiomas** de `Localization.cs`.
- Sin push/merge: tags locales + reinicio de la app; publicación a GitHub la decide Yovan al cerrar F2.

## 5. Archivos afectados
- `Services/DesignSystem.cs` — nuevos `Shapes.FillRounded/RoundedRectPath`, `ColorMath.Contrast`.
- `Services/PaceCalculator.cs` — `PaceResult.IdealPct`.
- `Services/UsageFormat.cs` — `ResetAbsolute`, `Relative`, `IsStale`.
- `Services/Theme.cs` — `ThemeResolver.TaskbarIsLight()`.
- `Services/Localization.cs` — strings nuevos × 8 idiomas.
- `UI/dashboard/QuotaBar.cs` (**nuevo**) — barra unificada con marker/ticks.
- `UI/dashboard/DashboardDataView.cs`, `DashboardHeader.cs` — delegan en `QuotaBar`; eliminan `FillRounded` duplicado.
- `UI/dashboard/DashboardSettingsView.cs`, `UI/TrayIconRenderer.cs` — usan `Shapes.*`.
- `UI/TrayIconRenderer.cs` + `TrayAppContext.cs` — tray adaptativo + forma por estado.
- `UI/DashboardForm.cs` — footer con stale + sello local.
- `Program.cs` — QA strip ampliada.

## 6. Riesgos (del análisis)
- **Divergencia gemela**: si se toca solo una barra, cuerpo y cabecera divergen → la Tarea 0 (unificar en `QuotaBar`) lo elimina de raíz.
- **Invariante medir/pintar**: cualquier alto nuevo debe sumarse en ambas ramas.
- **API firma pace**: ampliar a `PaceResult?` toca varios call-sites (cambio interno controlado).
- **Contraste tray**: arreglar texto blanco a la vez que el adaptativo, o empeora en tema claro.
- **DateTimeKind**: normalizar `SavedAtUtc`/`UsageAtUtc` a UTC antes de restar.
- **i18n**: strings nuevos a los 8 idiomas o quedan en blanco para no-ES/EN.
- **Tests sin red**: usar `UsageCache`/`history.db`/muestras, nunca forzar fetch real a la API.

## 🔗 Relacionado
- `2026-06-02-claudebar-apple-roadmap.md` (paraguas, 5 fases)
- `2026-06-02-claudebar-f1-cimientos-visuales-design.md` (F1 — lo que F2 hereda)
