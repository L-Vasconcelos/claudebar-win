# Diseño — Rediseño del dashboard + panel de ajustes (ClaudeBarWin v0.3)

Fecha: 2026-06-01
Estado: aprobado (brainstorm) — pendiente de plan de implementación

## Objetivo

Hacer ClaudeBar **más intuitivo**: recomponer el dashboard con jerarquía visual y prioridad clara, y
mover la configuración (que hoy vive 100% en el menú click-derecho) a un **panel de ajustes dentro del
propio dashboard**, accesible con un botón ⚙. Va junto con la integración de la mascota (sesiones en
vivo) en una sola UI coherente → **v0.3**.

Enfoque elegido (brainstorm): el `DashboardForm` (popup flotante custom-draw) gana dos **modos de
vista** — `Data` y `Settings` — alternables con ⚙ / ‹. La vista Data se recompone con una **cabecera
"de un vistazo"** (mascota grande + estado + cuota crítica + pace) y **secciones plegables**
reordenadas por prioridad. El **menú click-derecho se reduce a acciones**. La mascota crece a un bloque
ASCII con **dos tamaños** (6×6 / 8×8) elegibles desde el panel.

## Decisiones del brainstorm

- **Config en panel ⚙ dentro del dashboard** (no ventana aparte). Toggle de vista Data ↔ Settings.
- **Menú click-derecho minimal**: solo acciones (Dashboard, Ajustes [abre ⚙], Buscar actualizaciones,
  Salir). TODA la configuración pasa al panel — un único sitio, sin duplicación.
- **Integrar en v0.3** sobre la rama `feat/live-sessions`: el rediseño incluye la mascota + lista de
  sesiones. Rediseño y mascota salen juntos.
- **Recomposición de la vista Data** (las 4 acordadas):
  1. **Reordenar por prioridad**: cuota + estado arriba; gasto/gráfica abajo.
  2. **Jerarquía visual**: headers de sección, separadores, pesos/tamaños de fuente diferenciados.
  3. **Cabecera "de un vistazo"**: mascota + estado servicio + cuota crítica (5h/7d) + pace, siempre arriba.
  4. **Secciones plegables**: Cuota / Sesiones / Gasto / Gráfica colapsables (modo compacto), estado persistido.
- **Mascota más grande, 2 tamaños** (6×6 y 8×8), **elegibles en el panel** (`MascotSize` compact/large).
  Bestiario propio (clean-room) × 6 estados × 2 tamaños.

## Contexto del código actual (de la rama feat/live-sessions)

- `UI/DashboardForm.cs` (~800 líneas + sección de sesiones): popup borderless, **100% custom-draw** en
  `OnPaint` → `LayoutContent(g, draw)` que recorre secciones de arriba a abajo devolviendo `y`; la
  ventana auto-ajusta su alto. Sin controles hijos: todo hit-test manual contra `Rectangle`s cacheados.
  Doble pasada: `LayoutContent(draw:false)` mide (en `Relayout`), `draw:true` pinta.
- Secciones actuales (apiladas): título/estado, salud, barras 5h/7d, pace (`DrawPace`), líneas por
  modelo, **mascota + lista de sesiones** (`DrawLiveSessions`), gasto (`DrawSpendBody`), gráfica
  (`DrawChart`/`DrawSpendBody`/`DrawPercentBody` con tabs). Helpers: `FillRounded`, `DrawSegments`, `Pick`.
- Config en `Config/AppConfig.cs` (auto-properties + JSON). Toda la UI de config en `TrayAppContext.BuildMenu`
  (submenús radio/check) + `UpdateMenuChecks` + `MutateConfig` (Load→change→Save→reasigna→RefreshAsync).
- Strings en `Services/Localization.cs` (clase `Strings`, 8 idiomas).
- Mascota: `Services/Mascot/MascotSprite.cs` → `Frames(SessionPhase)` (1 línea hoy).

## Arquitectura

Como `DashboardForm` ya es grande y va a crecer (header + data + settings), se **divide por
responsabilidad** en renderers sin estado, cada uno testeable/entendible en aislamiento. Todos siguen
el contrato del repo: `int Draw(Graphics g, bool draw, int x, int y, int w, ...)` que avanza y devuelve
`y` idéntico en `draw=false` y `draw=true`, y registra sus `Rectangle`s clicables en un diccionario.

### Renderers nuevos (`UI/dashboard/`)
- **`DashboardHeader.cs`** — cabecera "de un vistazo": mascota (vía `MascotRenderer`), estado del
  servicio (●), cuota crítica (la peor de 5h/7d con su barra) y pace+ETA. Devuelve alto + rect del ⚙.
- **`DashboardDataView.cs`** — orquesta las secciones plegables de datos (Cuota, Sesiones, Gasto,
  Gráfica): cada una con su header clicable ▸/▾ y, si expandida, su cuerpo (reusa `DrawBar`,
  `DrawLiveSessions`, `DrawSpendBody`, `DrawChart` existentes). Registra rects de plegado.
- **`DashboardSettingsView.cs`** — el panel ⚙: filas de config agrupadas (Apariencia, Secciones,
  Sesiones, Notificaciones, Icono, Frecuencia, Idioma, Sistema). Cada fila es un control custom-draw
  (toggle ☑, segmented, o "›" que despliega opciones). Emite cambios vía un callback `OnChange(Action<AppConfig>)`
  que `TrayAppContext` conecta a `MutateConfig`.
- **`MascotRenderer.cs`** — dibuja el bloque ASCII de la mascota a tamaño `compact`(6×6)/`large`(8×8)
  con color por fase; consume `MascotSprite.Frames(phase, size)`.

### `DashboardForm` (orquestador, adelgazado)
- Campo `_view` (`DashboardView.Data | Settings`). `OnPaint` delega: en Data → `DashboardHeader` +
  `DashboardDataView`; en Settings → cabecera mínima (título + ‹) + `DashboardSettingsView`.
- Hit-test central en `OnMouseDown`: ⚙ → `_view = Settings`; ‹ → `_view = Data`; toggles de plegado;
  filas de settings; lo existente (tabs gráfica, cerrar, drag). Cada renderer expone sus rects.
- `Relayout()` recalcula alto según vista + secciones expandidas (ya soportado por la doble pasada).
- Nuevos eventos/callbacks: `SettingsChanged(Action<AppConfig>)`, `SectionToggled(string key)`.

### `MascotSprite` (ampliado)
- `Frames(SessionPhase phase, MascotSize size)` → `IReadOnlyList<string[]>` (cada frame = varias líneas).
  Bestiario propio en 6×6 y 8×8 para los 6 estados (idle/processing/waitingApproval/waitingInput/compacting/ended).
- `enum MascotSize { Compact, Large }`.

### `AppConfig` (config nueva)
- `MascotSize` (string "compact"/"large", default "compact").
- Plegado por sección: `CollapsedQuota`, `CollapsedSessions`, `CollapsedSpend`, `CollapsedChart` (bool;
  defaults: Cuota/Sesiones expandidas, Gasto/Gráfica plegadas).
- `DashboardView` **no se persiste** (siempre arranca en Data).

### `TrayAppContext` (menú minimal)
- `BuildMenu` → solo: Dashboard · **Ajustes** (muestra el dashboard y conmuta a vista Settings) ·
  Buscar actualizaciones · Salir. Se retiran los submenús de config (Apariencia/Secciones/Notif/Icono/
  Frecuencia/Idioma/Advanced) — su lógica de mutación se reusa desde el panel vía `MutateConfig`.
- `_dashboard.SettingsChanged += a => MutateConfig(a);` conecta el panel con la persistencia existente.
- `DescribeMenu` se actualiza para reflejar el menú minimal.

### `Localization`
- Strings nuevas para el panel: headers de grupo (ya existen `MenuAppearance/MenuSections/...`),
  etiquetas de filas, `MascotSizeCompact/Large`, `Settings`, `Back`, headers de sección plegable
  (`SectionQuota/Sessions/Spend/Chart`). Default EN + traducción ES; resto fallback EN.

## Flujo de interacción

```
clic ⚙ (header)        → _view=Settings → Relayout → repaint (panel de ajustes)
clic ‹ (settings)      → _view=Data → Relayout → repaint
clic ▸/▾ (sección)     → toggla Collapsed* en config (MutateConfig) → Relayout
clic fila de ajuste    → SettingsChanged(a) → MutateConfig(a) → Save + RefreshAsync → repaint
clic-derecho           → menú minimal (acciones)
```

## Manejo de errores y bordes

- Vista Settings con datos ausentes (sin cuota aún): el panel no depende de datos en vivo (solo config),
  así que funciona siempre.
- Auto-resize: al conmutar vista o plegar/expandir, `Relayout` recalcula; mantener la simetría
  `draw=false`/`draw=true` en todos los renderers (regla dura del repo) para no descuadrar el alto.
- El panel reusa `MutateConfig` (Load→change→Save→reasigna→RefreshAsync): cada cambio persiste y refresca
  como hoy; sin ruta de persistencia nueva.
- Mascota: si `MascotSize` desconocido → fallback a compact.

## Fuera de alcance (YAGNI)

- Ventana de ajustes separada (descartada en el brainstorm).
- Animaciones/transiciones entre vistas.
- Temas nuevos o rediseño de la paleta (se mantiene `Theme`).
- Cambios en el auto-update, modos CLI, hooks de sesiones en vivo, o la lógica de cuota/gasto/pace.
- Drag-resize manual del panel (sigue auto-resize).

## Testing

Reusa el proyecto `ClaudeBarWin.Tests` (xUnit). Lógica pura testeable:
- `MascotSprite.Frames(phase, size)`: todo (phase, size) devuelve ≥1 frame no vacío; large tiene más
  líneas que compact.
- Mapeo plegado: dado un `AppConfig`, qué secciones se consideran expandidas.
- Resolución de `MascotSize` desde string (incl. fallback).
- La selección de "cuota crítica" para la cabecera (peor de 5h/7d).
Verificación funcional: `--render-test` extendido para volcar a PNG **ambas vistas** (Data y Settings)
y ambos tamaños de mascota, para revisión visual sin interacción.

## Versión y entrega

**v0.3** sobre `feat/live-sessions`. Antes de publicar v0.3: prueba viva de sesiones en vivo (T16 del
plan anterior) + esta UI, merge a `main`, bump a 0.3.0, release vía `scripts/release.ps1` (auto-update
ya operativo desde v0.2.0).
