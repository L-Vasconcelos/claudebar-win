# ClaudeBar v0.3.5 — Plan de implementación: fix mascota + rediseño ajustes (Apple) + arreglos visuales

- Rama: `feat/v035-debug-settings`
- Spec: `docs/superpowers/specs/2026-06-03-claudebar-v035-debug-settings-redesign-design.md`
- Fecha: 2026-06-03
- Método: TDD donde aplica (helpers con invariante medir/pintar, anti-truncamiento, ActionFor), **commit por tarea**, en orden de dependencia. Solo `commit`, NUNCA `push`.
- Invariante transversal en CADA tarea de render: `draw=false` (mide) y `draw=true` (pintan) devuelven el MISMO `y`. Tokens de `Spacing`/`Theme`, sin literales nuevos.
- Baseline de tests a no romper: ~227 (≈212 métodos `[Fact]/[Theory]`). Cada tarea deja la suite verde.

> ## ⚙️ DECISIÓN DE YOVAN (2026-06-03): **UNA PANTALLA AGRUPADA, SIN PESTAÑAS**
> El panel de ajustes es **una sola vista vertical** (no tabs/panes navegables). Todo visible, ordenado por secciones con **cabecera en MAYÚSCULAS + divisor 1px + aire generoso** entre grupos. Esto **ANULA** la parte de "tab-bar de 4 panes" de la spec: **NO** se implementa navegación por pestañas ni `_settingsTab`. Se conservan TODOS los componentes (SectionHeader, TogglePill, anti-truncamiento, StatusBadge, MultiSegmentRow, master/dependientes) y la eliminación de duplicados. Orden de secciones (arriba = más usado):
> 1. **MOSTRAR** — Gasto estimado · Estado del servicio · Gráfica de uso
> 2. **SESIONES EN VIVO** — Activar (master, StatusBadge) → [dependientes indentados] Mostrar mascota · Tamaño · Silenciar con foco
> 3. **NOTIFICACIONES** — Notificaciones (master) → [dep] Avisos de ritmo · Avisar a (hitos 25/50/75/95)
> 4. **ACTUALIZACIÓN** — Frecuencia (30s/1m/5m/15m)
> 5. **ICONO DE BANDEJA** — Contenido (%/▲/%▲) · Umbral de color (70/90·80/95·60/85)  ← el umbral va AQUÍ (color del icono), separado de los hitos de notificación, así no duplica
> 6. **APARIENCIA** — Tema · Posición · Opacidad · Fijar al perder foco · Siempre encima · Reducir movimiento
> 7. **SISTEMA** — Arrancar con Windows · Idioma
> 8. **ACERCA DE** — Versión · Importar tema .itermcolors · Abrir carpeta de logs · Ver en GitHub

Trailer de commit en TODAS las tareas:
```
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```

---

## T0 — Fix de visibilidad de la mascota (desacoplar de LiveSessionsEnabled)

- **Objetivo**: que la mascota se vea con `ShowMascot=on` aunque `LiveSessionsEnabled=off`, manteniendo el gato Idle estático (ambiente) y la reactividad cuando hay hooks.
- **Pasos**:
  1. (TDD) Test de header: con `ShowMascot=true, LiveSessionsEnabled=false`, el bloque de mascota reserva alto > 0 y `textX` queda indentado (sprite + 12). Falla con el código actual.
  2. `UI/dashboard/DashboardHeader.cs:80`: cambiar `if (cfg.LiveSessionsEnabled && cfg.ShowMascot)` → `if (cfg.ShowMascot)`.
  3. Verificar que NO se tocan las puertas de animación (bote/fast-tick/mascotAlive siguen exigiendo `LiveSessionsEnabled`): el gato Idle es estático con live off.
  4. Test de invariante: medir==pintar del bloque header con mascota on/off (alto idéntico).
- **Archivos**: `UI/dashboard/DashboardHeader.cs`, `ClaudeBarWin.Tests/` (header/mascota).
- **Commit**: `fix(mascot): mostrar mascota Idle con ShowMascot on aunque LiveSessions off (desacople en DashboardHeader)`

---

## T1 — Infra de helpers del panel: tokens de espaciado + SectionHeader (mayús + divider)

- **Objetivo**: cimentar el ritmo Apple. Sustituir avances mágicos por `Spacing`; nuevo `SectionHeader` (MAYÚS, `smallFont`, `TextMuted`, Divider 1px `Theme.Separator`, `Spacing.Md` arriba / `Spacing.Sm` abajo).
- **Pasos**:
  1. (TDD) Test: `SectionHeader` devuelve el mismo `y` en draw=false/true; el avance es `Md + altoTexto + Sm`. Divider dentro de `[x, x+w]`.
  2. Implementar `SectionHeader` reemplazando `GroupHeader`; pintar la línea con `Pen(Theme.Separator, 1)`.
  3. Reescribir las constantes de avance de `ToggleRow/ActionRow/CycleRow/SegmentedRow/ButtonRow` en términos de `Spacing` (fila = alto + `Spacing.Sm`; ButtonRow = `h + Spacing.Sm`).
  4. Eliminar el `y += 22` hardcodeado de hitos (lo absorberá el MultiSegmentRow de T5).
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): SectionHeader Apple (mayús + divider) y ritmo vertical sobre rejilla 8pt`

---

## T2 — TogglePill dibujado (sustituye ☑/☐) + Row con subtítulo

- **Objetivo**: control toggle como cápsula+knob (ON=Accent, OFF=Separator) a la DERECHA; fila de dos partes con subtítulo opcional (`smallFont`/`TextMuted`).
- **Pasos**:
  1. (TDD) Test: `TogglePill` mide==pinta; knob a la izquierda si OFF, derecha si ON; hit-test = rect completo de la fila; estado ON usa `Theme.Accent`, OFF `Theme.Separator`.
  2. Implementar `TogglePill` con `Shapes.FillRounded` (track) + círculo (knob).
  3. Refactor `ToggleRow`: título (`labelFont`/`TextPrimary`) izquierda + subtítulo opcional debajo + pill derecha. Sin glifos Unicode. Alto = 1 o 2 líneas según subtítulo.
  4. Migrar todas las llamadas `ToggleRow` actuales al nuevo helper (mismas claves `toggle:*`).
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): TogglePill GDI + fila título/subtítulo/control (fuera glifos ☑/☐)`

---

## T3 — Anti-truncamiento en CycleRow y SegmentedRow (medir y elidir/envolver)

- **Objetivo**: ningún texto cortado. Medición en `draw=false`; elipsis medido o fallback a 2 líneas; primer chip nunca a la izquierda de `contentLeft`.
- **Pasos**:
  1. (TDD) Tests: CycleRow con `PosCustom` ("Personalizada (arrastra el panel)") elide el valor derecho y NO solapa la etiqueta; SegmentedRow que excede `w` envuelve a 2 filas o abrevia; gutter `≥ Spacing.Md` label↔control; margen derecho `≥ Spacing.Sm`.
  2. `SegmentedRow`: calcular ancho total de segmentos; si excede ancho de contenido → 2 filas; nunca `x < contentLeft`.
  3. `CycleRow`: medir etiqueta + valor; si suma+gutter > `w` → elidir valor con elipsis medido o caer a 2 líneas.
  4. Acortar etiquetas de **Frecuencia** a `30s / 1m / 5m / 15m` (en `Strings`, todos los idiomas).
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `Services/Localization.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `fix(settings): anti-truncamiento medido en Cycle/Segmented + frecuencia compacta (30s/1m/5m/15m)`

---

## T4 — StatusBadge (sin tab-bar — decisión: una pantalla)

- **Objetivo**: StatusBadge semántico para la fila maestra de hooks. **NO** hay tab-bar (Yovan eligió una sola pantalla agrupada). El panel se dibuja como UNA vista con todas las secciones en orden.
- **Pasos**:
  1. (TDD) Test: `StatusBadge` mide==pinta; mini `RoundedRectPath` + texto 1 línea centrado (`StringFormat` center), color `Theme.Ok` ("Activas") / `Theme.Warn` ("Instalar"), truncado tail dentro de su ancho.
  2. Implementar `StatusBadge` helper en `DashboardSettingsView`.
  3. (No se toca `DashboardForm` para tabs; no hay `_settingsTab`.)
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): StatusBadge semántico (Activas/Instalar) para la fila maestra de hooks`

---

## T5 — MultiSegmentRow (hitos multi-activo) — eliminar el re-pintado manual

- **Objetivo**: un único helper que soporta varios activos (25/50/75/95) con el mismo lenguaje visual (Accent + Contrast), sin el parche de re-pintar por encima de `DrawSegments`.
- **Pasos**:
  1. (TDD) Test: `MultiSegmentRow` marca como ON cada valor presente en el array; mide==pinta; un clic alterna ese valor; estilo idéntico a `DrawSegments` (Accent+Contrast).
  2. Implementar `MultiSegmentRow` (acepta `IEnumerable<activeValues>`), retirar el bloque manual de `milestone:*`.
  3. Mantener las claves `milestone:<pct>` y su `ActionFor` (toggle dentro del array) intactas.
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `refactor(settings): MultiSegmentRow para hitos (sin re-pintado manual; un solo estilo de pill)`

---

## T6 — Secciones MOSTRAR + NOTIFICACIONES + ACTUALIZACIÓN (master/dependientes) — una pantalla

- **Objetivo**: en la vista única, poblar las secciones de arriba con el `DrawDependent` (master/dependientes) para notificaciones. (NO panes; secciones consecutivas con `SectionHeader`+divisor entre cada una.)
- **Pasos**:
  1. (TDD) Test: con `Notifications=off`, PaceAlerts e hitos quedan **inertes** (su rect no dispara mutación) y se dibujan atenuados (opacity ~0.5); con `Notifications=on` responden. Helper `DrawDependent` aplica indent `Spacing.Lg` + atenuación + inercia.
  2. Sección **MOSTRAR**: ShowSpend (+sub "Coste equivalente por modelo") / ShowHealth / ShowChart.
  3. Sección **NOTIFICACIONES**: Notifications (master) → PaceAlerts + MultiSegment hitos (25/50/75/95), dependientes indentados `Spacing.Lg`.
  4. Sección **ACTUALIZACIÓN**: Frecuencia (SegmentedRow 30s/1m/5m/15m).
  5. Helper `DrawDependent(...)`: indent `Spacing.Lg` + atenuación + inercia cuando el master está off (no registra rect activo o lo registra inerte).
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): secciones Mostrar/Notificaciones/Actualización + DrawDependent (master/dependientes)`

---

## T7 — Sección SESIONES EN VIVO (master hooks + mascota dependiente) + ICONO + APARIENCIA — quitar duplicados

- **Objetivo**: la sección "SESIONES EN VIVO" con fila maestra = botón hooks + StatusBadge, y Mostrar mascota / Tamaño / Silenciar como dependientes atenuados+inertes cuando los hooks no están instalados. Más las secciones ICONO DE BANDEJA (Contenido + Umbral de color) y APARIENCIA (Tema/Posición/Opacidad/Sticky/OnTop/ReduceMotion).
- **Pasos**:
  1. (TDD) Test: con `HookInstaller.IsInstalled()==false`, los 3 dependientes están inertes+atenuados; el StatusBadge dice "Instalar" (ámbar); con true → "Activas" (verde) y dependientes activos.
  2. Sección **SESIONES EN VIVO**: fila maestra `special:hooktoggle` (+sub "Recibe el estado de tus sesiones de Claude Code") + StatusBadge; dependientes `toggle:ShowMascot`, `mascotsize`, `toggle:Suppress` vía `DrawDependent`.
  3. Sección **ICONO DE BANDEJA**: Contenido (%/▲/%▲) + Umbral de color (70/90·80/95·60/85) — el umbral aquí (color del icono), separado de los hitos de notificación → elimina el duplicado conceptual.
  4. Sección **APARIENCIA**: Tema · Posición (CycleRow) · Opacidad · Fijar al perder foco · Siempre encima · Reducir movimiento (+sub "Desactiva animaciones").
  5. Quitar definitivamente la presentación antigua (mascota suelta encima del botón; toggles de ventana mezclados sin orden).
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `Services/Localization.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): sección En vivo (master hooks + mascota dependiente) + Icono/Apariencia, sin duplicados`

---

## T8 — Secciones SISTEMA + ACERCA DE + i18n "Sistema" + acciones consolidadas (final de la vista)

- **Objetivo**: las dos últimas secciones de la vista única: mover acciones al final, localizar "Sistema", deshacer la dispersión de Importar tema.
- **Pasos**:
  1. Añadir `MenuSystem` a `Strings` (todos los idiomas) y usarlo (fin del literal hardcodeado).
  2. Sección **SISTEMA**: Arrancar con Windows, Idioma (CycleRow). Sección **ACERCA DE**: Versión (texto), Importar .itermcolors (ButtonRow). Logs/GitHub solo si el host enruta su `special:*` (si no, no se pintan).
  3. Retirar "Importar .itermcolors" de Apariencia.
- **Archivos**: `UI/dashboard/DashboardSettingsView.cs`, `Services/Localization.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `feat(settings): secciones Sistema/Acerca de + 'Sistema' localizado + Importar tema consolidado`

---

## T9 — Arreglos visuales del dashboard (header con mascota, eje X, Sonnet, plan, footer, tema claro)

- **Objetivo**: cerrar los issues de la auditoría visual (cortes primero).
- **Pasos**:
  1. (TDD) Test: con mascota activa, la línea de salud nunca rebasa `rightEdge - Spacing.Md`; se elide con elipsis medido (medir==pintar).
  2. `DashboardHeader.cs`: elidir línea de salud, aire al spinner (`Spacing.Sm`), plan una sola vez ("Plan Max · 5x"), margen superior `Spacing.Sm` al bloque de estado.
  3. `DashboardDataView.cs`: primera etiqueta del eje X `StringAlignment.Near` con `x=plotLeft` (o empujar `plotLeft` si no cabe, margen `≥ Spacing.Sm`); gap `Spacing.Sm` antes de la línea Sonnet y alinear su `%` a la rejilla derecha; footer en 1 línea + subir contraste del gris.
  4. `Theme.cs`: ajustar `Theme.Light` (muted + verde éxito) a contraste `≥ 4.5:1` verificado con `ColorMath.Contrast`.
- **Archivos**: `UI/dashboard/DashboardHeader.cs`, `UI/dashboard/DashboardDataView.cs`, `Services/Theme.cs`, `ClaudeBarWin.Tests/`.
- **Commit**: `fix(ui): header salud sin corte + eje X primer label + Sonnet/plan/footer/contraste tema claro`

---

## T10 — Verificación final + render

- **Objetivo**: confirmar criterios de aceptación de la spec.
- **Pasos**:
  1. `dotnet build ClaudeBarWin.sln` limpio (sin warnings nuevos).
  2. `dotnet test` verde (baseline ~227, más los tests nuevos).
  3. `--render-test`, `--render-demo` y `--render-gif`: inspeccionar PNG de los 4 panes de ajustes + header con mascota. Confirmar: cero textos cortados (ni `gundos`, ni `un 00h`, ni `mié 13:4`), espaciado perceptible, pills legibles, mascota Idle visible.
  4. Si es viable, verificación manual: abrir el panel, navegar los 4 panes, comprobar dependientes inertes, badge de hooks.
  5. Actualizar `CHANGELOG`/version a v0.3.5 si procede (sin push).
- **Archivos**: render outputs (`%TEMP%\claudebar-*`), `CHANGELOG`/version files.
- **Commit**: `chore(v0.3.5): verificación build+suite+render (panes sin cortes, mascota visible) y changelog`

---

## Orden de dependencia (resumen)

`T0` (fix mascota, independiente) → `T1` (tokens/SectionHeader) → `T2` (TogglePill/Row) → `T3` (anti-truncamiento) → `T4` (StatusBadge, SIN tab-bar) → `T5` (MultiSegmentRow) → `T6` (secciones Mostrar/Notificaciones/Actualización) → `T7` (sección En vivo + Icono + Apariencia) → `T8` (secciones Sistema/Acerca de) → `T9` (arreglos visuales dashboard) → `T10` (verificación + render).

T1–T5 son los componentes; T6–T8 ensamblan las secciones en **UNA sola vista vertical** (decisión Yovan: sin pestañas), en el orden del banner de arriba, con `SectionHeader`+divisor+aire entre cada sección; T9 es ortogonal al panel (dashboard) y puede solaparse con T6–T8; T10 cierra. **El render de T10 valida la vista única (no panes): scroll de todas las secciones sin cortes, mascota Idle visible.**
