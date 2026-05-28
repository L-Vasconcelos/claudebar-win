# Diseño — Histórico de % real + Pace (ClaudeBarWin)

Fecha: 2026-05-28
Estado: aprobado (brainstorm) — pendiente de plan de implementación

## Objetivo

Añadir a ClaudeBarWin dos capacidades inspiradas en CodexBar:

1. **Histórico de % real**: muestrear periódicamente la utilización real (`five_hour`/`seven_day`
   de `/api/oauth/usage`) y guardarla, para graficar el **% real de cuota a lo largo del tiempo**
   (no el proxy de coste-equiv). La gráfica gana un toggle **% real ↔ $ equiv**.
2. **Pace / predicción**: a partir del % actual + el histórico, estimar el **ritmo de quemado**,
   el **ETA de agotamiento** de cada ventana, y avisar si se proyecta agotar **antes del reset**.

Razón: hoy la gráfica usa coste-equiv de los `.jsonl` (un proxy) porque la API solo da un snapshot.
Muestreando el % real lo convertimos en una serie temporal verdadera. El pace responde a la pregunta
operativa real: "¿me va a durar la cuota o freno?".

## Decisiones del brainstorm

- **Gráfica:** un **toggle** `% real` ↔ `$ equiv` en la misma gráfica (no reemplazar, no dos gráficas).
- **% real:** **selector [5h | 7d]**, una línea cada vez (no superponer).
- **Pace:** mostrar **ambas cosas** — ETA de agotamiento **y** ritmo vs ideal.
- **Dónde:** panel **+ aviso proactivo + pace en el icono** (modo percent/pace/both).
- **Almacenamiento:** **SQLite** (`Microsoft.Data.Sqlite`).
- **Cálculo de pace:** **híbrido** (ritmo robusto desde el día 1 + ETA por pendiente cuando haya histórico).
- **Modo por defecto de la gráfica:** `$ equiv` (tiene histórico ya); el usuario cambia a `% real`.

## Componentes

### `UsageHistoryStore` (nuevo, SQLite)
- DB: `%APPDATA%\ClaudeBarWin\history.db`.
- Tabla `usage_samples`:
  - `ts` INTEGER (unix ms) — índice
  - `five_pct` REAL NULL, `seven_pct` REAL NULL, `opus_pct` REAL NULL, `sonnet_pct` REAL NULL
  - `five_reset` INTEGER NULL (unix ms), `seven_reset` INTEGER NULL
- API:
  - `Append(RealUsage usage, DateTime nowUtc)` — inserta una muestra. **Throttle ≥60s** (no inserta
    si la última muestra es de hace <60s).
  - `QueryPercent(DateTime fromUtc, DateTime toUtc, int maxPoints)` → lista de puntos
    `(tsUtc, fivePct?, sevenPct?)`, **downsampleada** a ~`maxPoints` (≈120) por media en píxel-bucket.
  - `RecentForRate(string window, TimeSpan span)` → muestras recientes para calcular pendiente.
  - `Prune(int days=40)` — `DELETE WHERE ts < now-40d`. Se llama al arrancar y ~1/hora.
- Solo guarda **% real**. El `$ equiv` sigue saliendo de los `.jsonl` (fuente con histórico completo).
- Native lib en el exe self-contained vía `SQLitePCLRaw.bundle_e_sqlite3` + `IncludeNativeLibrariesForSelfExtract` (ya activo).

### `PaceCalculator` (nuevo)
Para cada ventana `W ∈ {5h, 7d}` con `u`=% actual, `R`=reset (UTC), `L`=largo (5h / 7d):
- `timeUntilReset = R − now`; `elapsed = L − timeUntilReset`; `e = clamp(elapsed/L, 0..1)`.
- `idealUsed = e·100`.
- **`paceRatio` = `u / idealUsed`** (si `e>0`). >1 = vas pasado. Estado: ≤1.0 verde · 1.0–1.3 ámbar · >1.3 rojo.
- **`rate`** (%/ms): pendiente de las muestras de `RecentForRate` (ventana reciente: ~1h para 5h, ~6h para 7d),
  **ignorando tramos donde el % baja** (reset). Si no hay datos suficientes o sale ≤0 → fallback `rate = u/elapsed`.
- **`etaUtc`** = `rate>0 ? now + (100−u)/rate : null`.
- **`exhaustsBeforeReset`** = `etaUtc != null && etaUtc < R`.
- Devuelve `PaceResult { Window, PaceRatio, EtaUtc, ExhaustsBeforeReset, Status }`.

### Gráfica (en `DashboardForm`)
- **Toggle `% real` ↔ `$ equiv`**: control en la línea del título de la gráfica (un "%/$").
- **Modo `$ equiv`** (actual): áreas apiladas por modelo, suma por bucket, desde `.jsonl`.
- **Modo `% real`** (nuevo): **línea** del % a lo largo del tiempo, con **selector [5h | 7d]**.
  - Datos vía `UsageHistoryStore.QueryPercent` para el rango activo.
  - Si no hay muestras en el rango → texto "recopilando desde hoy".
  - El relleno bajo la línea usa el color de estado por umbral (verde/ámbar/rojo según el % del punto).
- **Pestañas de rango** `1H/5H/24H/7D/30D` aplican a ambos modos (ventana móvil hacia atrás).
- **Línea de pace** en el panel (debajo de las barras 5h/7d):
  `5h ritmo 80% · 7d ritmo 128% · tope vie 15:00 ⚠`, coloreada por estado (peor de las dos manda el color).

### Icono de bandeja (`IconDisplayMode`)
- `percent` (def.): el % máximo actual (comportamiento actual).
- `pace`: indicador de pace de la ventana más apretada — ETA compacto ("2d"/"5h"/"30m") o ▲ tintado por estado.
- `both`: el % + una marca pequeña tintada por estado de pace (▲ si vas pasado).
- Configurable en *Ajustes → (submenú)*; localizado.

### Notificación proactiva
- Cuando una ventana pasa de `exhaustsBeforeReset` false→true → notificar una vez:
  "⚠ A este ritmo te quedas sin cuota {semanal/de sesión} el {ETA}, antes del reset ({R})."
  Emoji/severidad según ventana. Re-arma al salir del estado (ritmo baja o resetea).
- Gated por `NotificationsEnabled` && `PaceAlerts`.

## Config (AppConfig) — nuevo

```jsonc
{
  "ChartMode": "spend",        // "spend" | "percent"
  "ChartPctWindow": "7d",      // "5h" | "7d"  (solo en modo percent)
  "IconDisplayMode": "percent",// "percent" | "pace" | "both"
  "PaceAlerts": true
}
```
Todo configurable desde el click derecho (patrón actual): toggle de modo de gráfica, selector 5h/7d,
submenú de modo de icono, toggle de avisos de pace. Umbrales de pace (1.0 / 1.3) hardcodeados de inicio.

## Flujo de datos

- `RefreshAsync` (cada poll): `FetchAsync` → si `Ok`:
  1. `UsageHistoryStore.Append(usage, now)` (throttle 60s).
  2. `PaceCalculator.Compute(usage, store.RecentForRate(...))` → pace por ventana.
  3. Construye `AppSnapshot` (+ `Pace`).
  4. `UpdateUi`: icono según `IconDisplayMode`; comprueba notificación de pace.
- Gráfica modo `% real`: el dashboard pide puntos a un provider inyectado
  (`Func<ChartRange,Task<List<PctPoint>>>`) que consulta `UsageHistoryStore` en background.
- `Prune` al arrancar y cada hora (timer existente o contador de polls).

## Fuera de alcance (YAGNI por ahora)

- Multi-proveedor (Codex/Gemini/…).
- Backfill del % real (imposible: la API no da pasado; el histórico empieza al activarlo).
- Ventana mensual de % como tab aparte (el rango 30D ya cubre la vista; el reset semanal es el límite real).
- `cost.total_cost_usd` de Claude Code vía statusline (no llega a una app suelta).
- Widget de escritorio.

## Verificación

- `--render-test`: añadir render del panel en modo `% real` con datos sintéticos/sembrados y de la línea de pace.
- `--report`: incluir el pace (ritmo + ETA) por ventana.
- Probar SQLite en el exe self-contained (native lib incluido) — smoke test de `Append`/`Query`.
- Comprobar throttle (no más de 1 fila/min) y `Prune`.
```
