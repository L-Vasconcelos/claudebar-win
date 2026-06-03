# Fase 1 · Cimientos visuales — Design

**Fecha:** 2026-06-02
**Fase:** 1 de 5 (ver `2026-06-02-claudebar-apple-roadmap.md`).
**Tipo:** refactor visual transversal (sin cambio funcional).
**Aprobado por Yovan:** alcance, orden de fases y acento (#CC785C) confirmados en brainstorming.

---

## 1. Objetivo

Dar a ClaudeBar-win una **base visual coherente** de la que hereden todas las fases siguientes: una paleta semántica resuelta por tema, una rejilla de espaciado y una escala tipográfica explícitas, números que no "bailan", y color de cuota que avisa de forma gradual. La app debe hacer **exactamente lo mismo** y verse **más limpia y consistente**.

## 2. No-objetivos (de esta fase)

- Animaciones / tweens / hover (Fase 3).
- Pace marker dentro de la barra, ticks, reset humano (Fase 2).
- Features nuevas (Fase 5).
- Cambiar la mascota ASCII (ya pulida).
- Nuevas opciones de usuario (salvo que el refactor lo exija).

## 3. Estado actual (resumen)

- `Services/Theme.cs`: record con 8 colores (`Background, Foreground, Dim, Track, Ok, Warn, Critical, Neutral`), `static readonly Theme Dark`, `StatusColor(Theme, UsageStatus)`, `Resolve(AppConfig)`, `FromImported(...)`. Light/CLI/imported existen.
- Renderers que dibujan con esos colores y offsets ad-hoc (14/16/22/50 px) y `new Font("Segoe UI", 8..13)` por sitio: `UI/dashboard/DashboardHeader.cs`, `DashboardDataView.cs`, `DashboardSettingsView.cs`, `MascotRenderer.cs`, `UI/TrayIconRenderer.cs`, y `UI/DashboardForm.cs` (define las fuentes base).
- El color de cuota salta por umbral (`util >= CriticalThresholdPct ? Critical : util >= WarnThresholdPct ? Warn : Ok`).

## 4. Diseño

### 4.1 Tokens semánticos (`Theme` ampliado)

Ampliar el record `Theme` con el set semántico. Los campos actuales se conservan como **alias** (propiedades de solo-lectura que devuelven el token nuevo) para no romper consumidores de golpe; el refactor migra los call-sites a los nombres nuevos progresivamente.

Set de tokens:
- `TextPrimary` — texto principal (= actual `Foreground`).
- `TextSecondary` — etiquetas/secundario (= actual `Dim`).
- `TextMuted` — terciario/captions (nuevo, más tenue que Secondary).
- `Separator` — líneas divisorias finas (nuevo; hoy se reusa `Track`).
- `Track` — fondo de barras/segmentos (se mantiene).
- `BgBase` — lienzo del panel (= actual `Background`).
- `BgElevated` — tarjetas/ajustes/tooltips/botones (nuevo).
- `Accent` — naranja Claude, dots/tab-segmento activo/detalles (nuevo).
- `Ok` / `Warn` / `Critical` — estados de cuota (se mantienen).
- `Neutral` — badge sin dato (se mantiene).

Valores **dark** (HIG-like, base/elevated):
```
BgBase      #1C1C1E
BgElevated  #2C2C2E
TextPrimary #F5F5F7
TextSecondary #A1A1A6
TextMuted   #8E8E93
Separator   #38383A
Track       #3A3A3C  (track de barras; más claro que BgElevated para que la barra vacía se distinga sobre la tarjeta)
Accent      #CC785C
Ok          #32D74B
Warn        #FFD60A
Critical    #FF453A
Neutral     #8E8E93
```
`Light`, `CLI` e `imported` definen el **mismo set** con sus valores (light invierte claros/oscuros; CLI mantiene su carácter; `FromImported` mapea desde `.itermcolors` con fallbacks: si falta un color, derivarlo —p.ej. `BgElevated` = `BgBase` aclarado ~6%, `TextMuted` = `TextSecondary` al 70% alpha, `Accent` = color de selección del tema importado o el naranja por defecto).

**Regla de uso del acento:** `Accent` tiñe dots, el segmento/tab activo y detalles. **Rojo/ámbar (`Critical`/`Warn`) se reservan exclusivamente para alerta de cuota/pace.**

### 4.2 Sistema de espaciado y tipografía (`Services/DesignSystem.cs`, nuevo)

```
public static class Spacing { public const int Xs=4, Sm=8, Md=12, Lg=16, Xl=24, Xxl=32; }
```
Todos los paddings/gaps/offsets de los renderers se snapean a estas constantes (sustituyen 14/16/22/50 ad-hoc por el múltiplo más cercano que conserve el layout).

Tipografía — una familia (**Segoe UI Variable**; fallback "Segoe UI"), 4 pasos + mono para números:
```
public static class Typography {
  // cacheadas (creadas una vez, dispose al cerrar la app)
  Hero    : "Segoe UI Variable Display", 28, Semibold   // cifras grandes/héroe
  Title   : "Segoe UI Variable Text", 15, Semibold      // cabeceras de sección
  Body    : "Segoe UI Variable Text", 12, Regular
  Caption : "Segoe UI Variable Text", 11, Regular        // muted
  Mono    : "Cascadia Mono" (fallback "Consolas"), 12, Regular  // dígitos tabulares
}
```
- Los valores numéricos (`%`, `$`, tokens, ETA, countdown) se dibujan con `Mono` y se **alinean a la derecha** en columna de ancho fijo → dejan de cambiar de ancho al actualizar.
- Jerarquía por **peso/tamaño/color**, no por adornos. Regla "interno ≤ externo": el gap etiqueta↔valor menor que el gap entre grupos; sustituir algún divisor por whitespace.

### 4.3 Color por riesgo (`DesignSystem.RiskColor`)

```
// pct 0..100; usa los umbrales del tema/config (warn, crit)
static Color RiskColor(double pct, Theme t, double warn, double crit)
  // 0..warn      → lerp(t.Ok,  t.Warn,     pct/warn)         (cálido → ámbar)
  // warn..crit   → lerp(t.Warn, t.Critical, (pct-warn)/(crit-warn))
  // >= crit      → t.Critical
  // clamp pct a [0,100]; lerp por canal ARGB (round, sin overflow)
```
Aplicar donde hoy hay salto por umbral: relleno de la barra de cuota, badge del icono de bandeja y el número de `%`. (El coloreado por **pace** que ya existe se mantiene; `RiskColor` sustituye los saltos por % por una transición continua.)

### 4.4 Refactor por archivo

- `Services/Theme.cs` — añadir tokens nuevos + alias; definir el set completo en Dark/Light/CLI; actualizar `FromImported` con fallbacks.
- `Services/DesignSystem.cs` (nuevo) — `Spacing`, `Typography`, `RiskColor`, helper `Lerp(Color,Color,double)`.
- `UI/DashboardForm.cs` — fuentes base pasan a usar `Typography`; offsets a `Spacing`.
- `UI/dashboard/DashboardHeader.cs`, `DashboardDataView.cs`, `DashboardSettingsView.cs`, `MascotRenderer.cs` — consumir tokens + spacing; números con `Mono`; barra/valor con `RiskColor`.
- `UI/TrayIconRenderer.cs` — número del badge con color por `RiskColor`; usar tokens.

## 5. Aislamiento / interfaces

- `DesignSystem` es **sin estado** y puro (entradas → Color/int/Font); testeable en aislamiento.
- `Theme` sigue siendo un record inmutable resuelto por `Resolve(AppConfig)`.
- Los renderers siguen siendo `static` y reciben `Theme` por parámetro (como hoy): el cambio es **qué** leen, no la firma.

## 6. Testing

Nuevos (xUnit, en `ClaudeBarWin.Tests`):
- `RiskColorTests`: `RiskColor(0)≈Ok`; `RiskColor(100)=Critical`; monotonía (componente rojo no decrece al subir pct); clamp de pct fuera de rango; sin excepción.
- `ThemeTokenTests`: para `Dark`, `Light`, `CLI` — todos los tokens son colores válidos (alpha>0 donde corresponde) y distintos de `default`; en `Dark`, `Accent == #CC785C` y `BgElevated != BgBase`.
- `DesignSystemTests`: `Spacing` son múltiplos de 4; `Lerp` en extremos devuelve los colores origen/destino.

Existentes: los **64 actuales siguen verdes** (el refactor no cambia comportamiento ni firmas públicas testeadas).

## 7. Criterios de aceptación

1. `dotnet build` 0 errores (SDK en `C:/Users/zorro/.dotnet`).
2. **64 tests existentes + nuevos** verdes.
3. `--render-test` genera `data.png`, `settings.png`, `mascot-large.png`, `tray-badges.png` sin romper; visualmente: tipografía consistente, **números monoespaciados alineados** que no bailan, **acento naranja** en dots/tab activo, en dark se aprecian **2 niveles de fondo**, barra/badge/número con **color por riesgo gradual**.
4. **Cero regresión funcional**: mismos toggles, misma información, mismos temas seleccionables.
5. Antes/después en PNG enviados a Yovan para validación visual.

## 8. Riesgos y mitigaciones

- **Temas importados (.itermcolors) con menos colores** → fallbacks deterministas (§4.1).
- **"Segoe UI Variable" / "Cascadia Mono" ausentes en alguna máquina** → fallbacks ("Segoe UI" / "Consolas"); ambas vienen con Win11.
- **Refactor amplio toca 6 archivos** → migrar token a token con alias para mantener verde el build en cada paso; verificar con `--render-test` por archivo.
- **Contraste de secundario/muted en temas custom** → asegurar AppC ≥ legible (se endurece en Fase 2 a11y; aquí solo no empeorar).

## 9. Ejecución (con subagentes)

Tras `writing-plans`: un subagente por unidad aislada (DesignSystem+tests · Theme tokens+tests · refactor Header · refactor DataView · refactor SettingsView+Mascot · refactor Tray), con verificación (build + tests) por tarea y un paso final de `--render-test` + antes/después.

## 🔗 Relacionado
- `2026-06-02-claudebar-apple-roadmap.md`
- `2026-06-01-rediseno-dashboard-panel-design.md`
