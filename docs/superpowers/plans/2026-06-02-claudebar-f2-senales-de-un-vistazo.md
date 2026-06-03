# Plan de implementación — Fase 2: Señales de un vistazo

**Spec:** `docs/superpowers/specs/2026-06-02-claudebar-f2-senales-de-un-vistazo-design.md`
**Rama:** `feat/live-sessions` (continúa F1, build base 76 tests verdes, tag `v0.3.2`).
**Método:** TDD estricto por tarea (test que falla → mínimo para pasar → refactor), commit por tarea, sin push. `dotnet` SDK de usuario en `C:\Users\zorro\.dotnet\` (no está en PATH del Bash → usar PowerShell: `& "$env:USERPROFILE\.dotnet\dotnet.exe" test`).

> Contexto para el ejecutor: app C# .NET 9 WinForms, render GDI+ a mano. F1 dejó `Theme` (tokens), `Spacing`, `ColorMath.Lerp/RiskColor`, `Typography.Mono` en `Services/DesignSystem.cs`/`Services/Theme.cs`. Usar SIEMPRE esos nombres; no inventar literales de color/offset. Invariante: las rutinas de dibujo se llaman con `draw=false` (medir) y `draw=true` (pintar) y deben devolver el mismo `y`.

## Estructura de archivos
- **Nuevo** `UI/dashboard/QuotaBar.cs` — barra de cuota unificada (sustituye a las gemelas `DrawBar`/`DrawCriticalBar`).
- **Nuevo** `Services/Shapes.cs` (o sección en `DesignSystem.cs`) — `FillRounded`, `RoundedRectPath` centralizados.
- Modificados: `Services/DesignSystem.cs` (`ColorMath.Contrast`), `Services/PaceCalculator.cs` (`IdealPct`), `Services/UsageFormat.cs` (`ResetAbsolute`/`Relative`/`IsStale`), `Services/Theme.cs` (`TaskbarIsLight`), `Services/Localization.cs` (strings ×8), `UI/TrayIconRenderer.cs` (adaptativo+forma), `TrayAppContext.cs` (UsageStatus+stale), `UI/dashboard/DashboardDataView.cs` + `DashboardHeader.cs` (delegan en QuotaBar), `UI/dashboard/DashboardSettingsView.cs` (usa Shapes), `UI/DashboardForm.cs` (footer stale+sello), `Program.cs` (QA strip).

---

## Tarea 0 · Centralizar geometría y contraste (refactor, sin cambio funcional)
**Objetivo:** eliminar las 4 copias de `FillRounded`/`RoundedRectPath` y la fórmula de luminancia inline.
**Pasos TDD:**
1. Test (nuevo `ClaudeBarWin.Tests/ShapesTests.cs`): `Shapes.FillRounded` con `Width<=0` no lanza; `radius<=1` cae a rectángulo (verificar vía un Bitmap 1×1 sin excepción). `ColorMath.Contrast(Color.Black)==Color.White` y `Contrast(Color.White)==Color.Black` (umbral luminancia 140).
2. Crear `Services/Shapes.cs` con `public static class Shapes { FillRounded(Graphics,Brush,Rectangle,int); GraphicsPath RoundedRectPath(Rectangle,int); }` (versión canónica con guardas de `DashboardDataView.FillRounded`). Añadir `ColorMath.Contrast(Color bg) => (bg.R*0.299+bg.G*0.587+bg.B*0.114) < 140 ? Color.White : Color.Black;` en `DesignSystem.cs`.
3. Reemplazar las 4 copias (`DashboardDataView`, `DashboardHeader`, `DashboardSettingsView`, `TrayIconRenderer`) por llamadas a `Shapes.*` / `ColorMath.Contrast`. Borrar los métodos locales.
4. Build + suite completa (debe seguir verde, render idéntico).
**Commit:** `refactor(design): centralizar Shapes.FillRounded/RoundedRectPath + ColorMath.Contrast`.

## Tarea 1 · Exponer `IdealPct` en `PaceResult`
**Objetivo:** el "% donde deberías ir" para el pace marker.
**Pasos TDD:**
1. Test (`PaceCalculatorTests`): con una ventana cuyo reset es `now + 2.5h` (mitad de 5h), `IdealPct ≈ 50`. Caso recién reseteado (`elapsed<1`) → `IdealPct ≈ 0`.
2. Añadir `double IdealPct` al record `PaceResult` (PaceCalculator.cs:5-12) y rellenarlo con el `idealUsed` que hoy se descarta (línea ~44). Actualizar el `return new PaceResult(...)`.
3. Build + tests.
**Commit:** `feat(pace): exponer IdealPct (ritmo ideal) en PaceResult`.

## Tarea 2 · Barra de cuota unificada `QuotaBar`
**Objetivo:** una sola rutina para cuerpo y cabecera; firma con `PaceResult?`.
**Pasos TDD:**
1. Test (`QuotaBarTests`): `QuotaBarGeometry.MarkerX(x,w,pct)` y `TickX(x,w,thresholdPct)` puros → posiciones correctas (`MarkerX(0,100,50)==50`, clamp a `[x,x+w]`).
2. Crear `UI/dashboard/QuotaBar.cs`: mover la rutina (idéntica en ambas gemelas) a `static int Draw(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace, int x, int y, int w, AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim)`. El color sigue el mismo criterio (PaceStatus→Ok/Warn/Critical, fallback `RiskColor`). Incluir `QuotaBarGeometry` con las funciones puras.
3. `DashboardDataView.DrawBar` y `DashboardHeader.DrawCriticalBar` pasan a delegar en `QuotaBar.Draw` (la cabecera construye sus brushes fg/muted como hoy). Actualizar call-sites: `DrawQuotaBody` pasa `snap?.PaceFive`/`snap?.PaceSeven` (PaceResult); cabecera pasa el PaceResult crítico.
4. Build + tests + `--render-test` (geometría idéntica a antes; sin marker aún).
**Commit:** `refactor(ui): unificar barra de cuota en QuotaBar (cuerpo+cabecera)`.

## Tarea 3 · Pace marker + ticks de umbral dentro de la barra
**Pasos TDD:**
1. Test: `QuotaBarGeometry.MarkerX` ya cubierto; añadir test de que ticks en warn/crit caen dentro del ancho y ordenados.
2. En `QuotaBar.Draw`, tras pintar el relleno (dentro de `if(draw)`): dibujar ticks (1px, `theme.Separator`) en `MarkerX(x,w,cfg.WarnThresholdPct)` y `...CriticalThresholdPct`; dibujar pace marker (línea 1–2px `theme.TextMuted`, sobresale 2px arriba/abajo) en `MarkerX(x,w,pace.IdealPct)` + triángulo ▾ encima. Solo si `pace` no es null. No alterar `y`.
3. Build + tests + render (marker y ticks visibles en `data.png`).
**Commit:** `feat(ui): pace marker + ticks de umbral dentro de la barra`.

## Tarea 4 · Reset en hora humana + explicación del rolling
**Pasos TDD:**
1. Test (`UsageFormatTests`): `ResetAbsolute(someOffset)` formatea `ToLocalTime():"ddd HH:mm"`; `null`→"".
2. `UsageFormat.ResetAbsolute(DateTimeOffset? resetsAt)`. Añadir `Strings.RollingHint` ("ventana móvil de 5h desde tu 1ª petición") a `Localization.cs` en los **8 idiomas**.
3. Línea de reset en `QuotaBar.Draw`: `"{ResetsIn} {cd} · {abs}"`. `RollingHint` bajo la sección Cuota (`DrawQuotaBody`) en `theme.TextMuted`/`Typography.Caption` — sumar su alto en ambas ramas (medir/pintar).
4. Build + tests + render.
**Commit:** `feat(reset): hora local absoluta + explicación del rolling 5h`.

## Tarea 5 · Estado stale + sello de privacidad honesto
**Pasos TDD:**
1. Test (`UsageFormatTests`): `Relative(UtcNow-5min)` → "hace 5 min" (localizado); `IsStale(UtcNow-10min, 60)` → true; `IsStale(UtcNow-30s,60)` → false. Normalización de `DateTimeKind.Unspecified`→Utc.
2. `UsageFormat.Relative(DateTime utc, Strings s)` + `IsStale(DateTime utc, int refreshSeconds)` (umbral `3×`). Normalizar Kind. Strings nuevos `AgoFormat`/`StaleLabel`/`LocalSeal` ×8. `LocalSeal` = "Tus credenciales y datos no salen del equipo · sin telemetría".
3. Footer `DashboardForm.cs:471-479`: "Actualizado · {Relative}"; si `IsStale`, marcador `theme.Warn`. Añadir línea `LocalSeal` (`Typography.Caption`, `theme.TextMuted`). No marcar stale en los primeros `RefreshSeconds` tras arrancar.
4. Build + tests + render.
**Commit:** `feat(trust): estado stale (hace N min) + sello de privacidad`.

## Tarea 6 · Tray adaptativo a barra clara/oscura + forma por estado (a11y)
**Pasos TDD:**
1. Test: `Tray.ShapeFor(UsageStatus)` puro (Ok→círculo/none, Warn→triángulo, Critical→rombo); `TaskbarIsLight()` no lanza (lee registro con fallback).
2. `ThemeResolver.TaskbarIsLight()` (lee `SystemUsesLightTheme`). `TrayIconRenderer.RenderBadge`: texto con `ColorMath.Contrast(bg)` (no `Brushes.White`); overlay de forma por `UsageStatus` (patrón del badge "pending"). Nuevo parámetro `UsageStatus status` + `bool stale` en `Render`. `TrayAppContext.IconContent`/`UpdateUi` derivan `UsageStatus` (vía `StatusFor`) y `stale` (`UtcNow-snap.UsageAtUtc` o `LatestState!=Ok`) y los pasan. **Dashboard**: glifo de forma de 1 char junto al `%` en `QuotaBar` (mismo mapeo color↔forma).
3. Ampliar QA strip en `Program.cs:434` (`tray-badges.png`): filas barra clara/oscura × 3 formas × stale, a 16px reales. Actualizar las demás llamadas a `Render` (Program previews, `_updateIcon` no se toca).
4. Build + tests + render (validar legibilidad a 16px).
**Commit:** `feat(tray): icono adaptativo a barra clara/oscura + estado por forma (a11y)`.

## Tarea 7 · Verificación final (sin commit propio)
1. Build Release-equivalent + suite completa (≥76 tests, idealmente más). 0 warnings nuevos.
2. `--render-test`: confirmar dashboard (marker+ticks+reset humano+footer stale+sello) y tray strip (claro/oscuro+formas+stale).
3. Reportar al coordinador: nº de tests, commits, y los PNGs para que reinicie la app, taggee `v0.3.3` y mande fotos.

## Notas
- i18n: todo string nuevo en los 8 bloques de `Localization.cs` (EN base + ES :259-266 + NL/FR/DE/JA/KO/ZH).
- Tests sin red: usar muestras/`UsageCache`/`history.db`, nunca forzar fetch real a la API.
- Accent (`#CC785C`) reservado a dots/tab activo; Warn/Critical solo cuota; el pace marker va en `theme.TextMuted` (neutro).
