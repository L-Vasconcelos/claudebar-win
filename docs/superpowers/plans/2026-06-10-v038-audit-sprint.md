# Plan — Sprint "verdad + escaparate" (v0.3.8) · fixes de la auditoría 2026-06-10

**Rama:** `fix/v038-audit-sprint` · **Base:** v0.3.7 (`b6a58de`), 378 tests verdes.
**Spec origen:** `docs/superpowers/specs/2026-06-10-auditoria-completa-ui-mercado.md` (§2 P0, §3 cortes/bordes, §5 arquitectura).
**Metodología:** TDD por tarea, workflow implementador → revisor adversarial → fix-loop. Commit por tarea aprobada. SIN push, SIN release, SIN bump de versión, SIN regenerar assets del README (eso va al cierre con OK de Yovan).

**Entorno:** SDK en `C:/Users/zorro/.dotnet/dotnet.exe` (NO está en PATH). Build/test: `C:/Users/zorro/.dotnet/dotnet.exe test ClaudeBarWin.sln`. Render QA: `C:/Users/zorro/.dotnet/dotnet.exe run --project ClaudeBarWin.csproj -- --render-test` (genera PNGs).

## Tareas (orden de ejecución)

### T1 — P0: escritura atómica de credenciales
`Services/UsageApiClient.cs` `TryOAuthRefreshAsync` (~153-199) hace `File.WriteAllTextAsync` directo sobre `~/.claude/.credentials.json` (archivo de OTRA app). Fix: extraer helper testeable (p.ej. `CredentialsWriter.WriteAtomic(path, json)`) que escribe a `<path>.tmp` + `File.Replace` (con fallback `File.Move(overwrite)` si no existe el destino), valida que el JSON nuevo parsea ANTES de escribir, y re-lee el archivo justo antes para no pisar un refresh concurrente más nuevo (comparar `expiresAt`: si el del disco es más nuevo que el nuestro, NO escribir). Tests con paths temporales inyectados (golden: round-trip, destino inexistente, JSON inválido no se escribe, token en disco más nuevo gana).

### T2 — i18n de formatos (UI inglesa mezclada)
Con `Language=en` salen `$420,50` y `jue 02:12` (CurrentCulture del SO). Fix: `Localization` expone `CultureInfo` por idioma seleccionado (en→en-US, es→es-ES, etc., sistema→CurrentCulture) y TODOS los format strings de números/moneda/fechas/días de la UI (Spend, total/peak, resets `ddd HH:mm`, tween de %) usan esa cultura. Buscar usos de `ToString("` y `$"{...:` en UI/ y Services/UsageFormat. Tests: formato moneda y día abreviado bajo Language=en vs es.

### T3 — QuotaBar: barra que tacha el %, ticks invisibles, triángulo del pace
(a) La fila compacta (Sonnet 7d) pinta el track a todo el ancho y cruza el `12%` right-aligned → acortar track antes del texto (como Session/Week). (b) Ticks warn/crit usan `theme.Separator` ≈ `theme.Track` en los 3 temas (CLI idéntico) → nuevo token `TickOnTrack` por tema (o `TextMuted`) visible sobre el tramo vacío. (c) El triángulo ▾ del pace sube hasta y-5 invadiendo la fila del label → clamp. `UI/dashboard/QuotaBar.cs:52-106`, `Services/Theme.cs`. Tests de geometría (QuotaBarTests) + token nuevo en ThemeTokenTests.

### T4 — Chart: colisiones de labels y leyenda
(a) Primera etiqueta del eje X con 2 textos superpuestos + labels pegadas → lógica de espaciado mínimo entre ticks (medir, saltar las que colisionan). (b) `peak $X` colisiona con el punto del pico cuando cae en el último bucket → offset/reposicionar. (c) Swatches de la leyenda ~2px altos respecto al centro óptico del texto → centrar. (d) `AnnotatePeak` usa color distinto en modo $ (theme:null→dim) vs % (Foreground) → unificar. `UI/dashboard/DashboardDataView.cs` (~370-600). Tests puros donde sea posible (función de filtrado de labels).

### T5 — Jerarquía: headline por peor estado + duplicados
(a) El header colapsable muestra la ventana de mayor % (Week 84% verde) cuando la sesión está 62% con pace crítico → el glance debe elegir la ventana con PEOR estado (Critical > Warn > Ok; a igualdad, mayor %). (b) Con la sección Cuota expandida, el glance duplica la fila Week → ocultar el glance cuando Cuota está expandida (o fusionarlo con el título de sección). (c) Quitar el doble título `▼ Chart` + `Usage chart` (sobra el label). `UI/dashboard/DashboardHeader.cs:86`, `DashboardDataView.cs`. Tests del selector de ventana peor-estado.

### T6 — Contraste WCAG: decisión de color de texto + tokens Warn/Critical de texto
(a) `ColorMath.Contrast` (luma 0.299/0.587/0.114, umbral 140) elige BLANCO sobre verde CLI #00D959 (1.9:1) → decidir con `RelativeLuminance` WCAG (ya existe en `ContrastRatio`): negro si L > ~0.18. Afecta chips activos, knob TogglePill, % del badge del tray. (b) Tokens `WarnText`/`CriticalText` por tema (patrón AccentText): Light.Warn como texto 2.8:1 → más oscuro; Dark.Critical #DC2626 3.7:1 → tipo #F87171 para TEXTO (fills no cambian). `Services/DesignSystem.cs:43`, `Services/Theme.cs`. Test: recorrer TODOS los tokens de texto de los 3 temas con `ColorMath.ContrastRatio ≥ 4.5`.

### T7 — Chips/tabs unificados + hover visible + chips de umbral con estado
(a) Tabs de rango 1H/5H/24H/7D/30D: aplicar borde de chip inactivo (como DrawSegments) + unificar geometría (alto 18, mismo padX/gap/radio) — idealmente reusar DrawSegments. (b) Hover: token `HoverBg` por tema (lerp(Background, Foreground, ~0.06)) en vez de BgElevated puro (invisible en claro/CLI) + aplicar clip del viewport cuando `_viewMode==settings` (hoy sangra sobre el chrome). (c) Chips de umbral 25/50/75/95% de ajustes: estado seleccionado/no-seleccionado visible (patrón del segmented Spend$/Quota%). `DashboardDataView.cs:378-393`, `DashboardForm.cs:962`, `DashboardSettingsView.cs`.

### T8 — Truncamiento: guards que faltan
(a) `ToggleRow` (≈12 filas) no acota label/subtitle → wrap dentro de `[x, pillLeft - Md]` (mismo patrón que MasterToggleRow); ídem etiquetas de `SegmentedRow`/`MultiSegmentRow`. (b) Filas de sesiones en vivo: Ellipsize del nombre de proyecto contra la fase right-aligned; ídem claves de `DrawSpendSection`. (c) `_plan.Display` del chrome, línea de pace y línea de reset por FitLine/Ellipsize. (d) `_backRect` 80×20 fijo → medir el texto localizado. (e) Chip de celebración: elidir en vez de desaparecer. `DashboardSettingsView.cs:649+`, `DashboardDataView.cs:335,237`, `DashboardForm.cs:992,1013`, `DashboardHeader.cs:63`. Tests de wrap/elipsis por locale largo (de/fr).

### T9 — Footer, knob, engranaje, badge "pend"
(a) Footer: el wrap debe tratar " · " como punto de ruptura preferente (romper ANTES del separador y omitirlo a inicio de línea) — `Services/FooterLayout.cs`/`TextWrap.cs` + tests. (b) Knob del TogglePill: blanco (con borde sutil) en vez de color de fondo. (c) Engranaje ⚙: dibujarlo como path GDI+ con AA (no glifo ClearType) — círculo + dientes simple, nítido en claro y oscuro. (d) Badge "pend" del tray: suprimir la forma a11y cuando hay punto de notificación (un solo elemento por esquina) + punto en ámbar Warn de la paleta (no oliva) + que no tape el dígito. `DashboardSettingsView.cs:431`, `DashboardHeader.cs:16`, `TrayIconRenderer.cs:98-125`.

### T10 — Medida central
`Measure()` central con `StringFormat.GenericTypographic` + `Math.Ceiling` para todos los right-align (QuotaBar %, $ de gasto, % de modelo) → columna derecha alineada de verdad; eliminar la mezcla `(int)`/Ceiling. `Services/DesignSystem.cs` o `Shapes.cs` + call sites. Ojo: puede mover 1-3px medidas existentes → ajustar tests de layout afectados con intención (no a ciegas).

### T11 — P0: DPI sc(px) + multi-monitor
(a) Helper `Dpi.Scale(px)` (factor `DeviceDpi/96f`) centralizado; aplicar a las constantes de layout NÚCLEO: tamaño/padding del panel (340×380/18), chrome (y+=50, closeRect, backRect), filas del header (16), QuotaBar (22/16/14/BarH), filas de ajustes (18/20, pill 36×20), ChartH, gearSize, scrollbar. `GearIconFont` de `GraphicsUnit.Pixel` → Point. `OnDpiChangedAfterParent` → relayout. (b) Multi-monitor: `PlaceWindow` usa `Screen.PrimaryScreen` mientras ajustes usa `FromControl` y clamp `FromPoint` → unificar resolviendo el Screen del cursor. Verificación: `--render-test` a 96 DPI debe seguir pixel-perfect (factor 1.0 = sin cambios); tests del helper + de que los rects escalan. ES LA TAREA MÁS GRANDE — si el implementador ve que no cabe bien, que la parta en (a) helper+aplicación y deje (b) para commit aparte.

### T12 — Verificación final
Build Release 0 warnings nuevos + suite completa verde + `--render-test`/`--render-demo` y REVISAR los PNGs A OJO contra la lista §3 del spec (cada defecto: ¿resuelto?). Lista de verificación punto por punto en el output. NO regenerar assets del README, NO bump, NO release.

## Criterios globales
- Cada tarea: tests nuevos/ajustados + suite completa verde ANTES de commit. Mensajes `fix(scope): ...` en español (estilo del repo) + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Respetar tokens/Spacing/Typography existentes (F1). Nada de colores mágicos nuevos: si hace falta color, token en Theme con ratio comentado.
- Invariante medir==pintar (draw:false/true) intacto.
- Los hallazgos exactos (con archivo:línea) están en el spec §2-§3 y en `C:/Users/zorro/Asistente/.tmp/claudebar-audit/audits.json`.

## 🔗 Relacionado
- `docs/superpowers/specs/2026-06-10-auditoria-completa-ui-mercado.md`
- `docs/superpowers/specs/2026-06-02-claudebar-apple-roadmap.md`
