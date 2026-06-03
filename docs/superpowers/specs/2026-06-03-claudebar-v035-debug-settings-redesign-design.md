# ClaudeBar v0.3.5 — Fix mascota + rediseño del panel de ajustes (Apple) + arreglos visuales · SPEC DE DISEÑO

- Rama: `feat/v035-debug-settings`
- Fecha: 2026-06-03
- Tipo: spec de diseño (qué y por qué). El cómo, por tareas, va en el plan hermano `docs/superpowers/plans/2026-06-03-claudebar-v035-debug-settings-redesign.md`.
- Restricción transversal (no negociable): **todo es GDI+ a mano, render en 2 pasadas** (`draw=false` mide / `draw=true` pinta). **AMBAS pasadas deben devolver el MISMO `y`.** Controles dibujados (pills/toggles/rows/badges), NADA de controles WinForms ni XAML.
- Sistema de diseño existente (NO inventar literales): `Theme` (Accent #CC785C, BgElevated, TextMuted, Separator, Ok, Warn, Critical), `Spacing` (`Xs`=4 / `Sm`=8 / `Md`=12 / `Lg`=16 / `Xl`=24 / `Xxl`=32), `ColorMath` (Lerp/RiskColor/Contrast), `Typography` (Segoe UI Variable + Mono), `Shapes` (FillRounded/RoundedRectPath), `QuotaBar`, `Motion` (easing F3), `DashboardDataView.DrawSegments` (segmentos con activo=Accent + Contrast).

---

## 0. Objetivos rectores de Yovan

1. **BUG**: la mascota no se ve.
2. Rediseñar el menú de **AJUSTES** inspirándose en **Apple** y en las apps de muestra.
3. **Más minimalista** tipo Apple e **intuitivo**.
4. **No cosas dobles** (sin duplicados/redundancias).
5. Sin textos **cortados**.
6. **Espaciado** entre elementos.
7. **Totalmente legible**.

Esta spec cruza las 4 auditorías Opus (mascota, auditoría de ajustes, inspiración Apple, auditoría visual de render) y fija la estructura final. No es un volcado de cada auditoría: donde discrepan, se decide aquí.

---

## 1. BUG de la mascota

### 1.1 Causa raíz (confirmada, doble verificación)

`UI/dashboard/DashboardHeader.cs` dibuja TODO el bloque de la mascota (sprite + verbo + bote) dentro de una sola condición:

```csharp
if (cfg.LiveSessionsEnabled && cfg.ShowMascot)   // línea 80
```

El `&&` con `LiveSessionsEnabled` es el bug. En `Config/AppConfig.cs`:

- `LiveSessionsEnabled = false` por defecto (línea 60). Es el **master de la feature de hooks/Named-Pipe**, y el ÚNICO control que lo pone a `true` es el botón "Activar (instalar hooks)…" (`special:hooktoggle` → `TrayAppContext.ToggleHooks`).
- `ShowMascot = true` por defecto (línea 62).

Resultado: el usuario activa "Mostrar mascota" en el panel (que solo muta `ShowMascot`), pero como `LiveSessionsEnabled` sigue en `false` y es **inalcanzable** desde el panel salvo instalando hooks, la mascota NO se dibuja. **Activa el toggle y no pasa nada.** Esa es la causa real de "la mascota no se ve".

Hechos que descartan otras causas (verificado):

- **Altura reservada > 0 en Idle**: el sprite Idle reserva 4 líneas (compacta) / 7 (grande) vía `MascotRenderer.Draw` (mono.GetHeight). Una vez relajada la puerta, el bloque tiene tamaño real.
- **Color ≠ fondo en Idle**: Idle + Neutral → `PhaseColor(Idle)` → `theme.Neutral` (#52525B dark sobre #18181B; #A1A1AA light sobre #FAFAFA). Contraste fuerte, sin bug de color-sobre-color.
- **Posición en pantalla**: la mascota se pinta en `(x, y+18)` dentro del área de contenido; `textX` indenta la columna derecha tras el sprite (+12). Con la puerta en `false`, `textX` se quedaba en `x`. Tras el fix se indenta correctamente.
- **`live.GlobalPhase` es Idle por defecto** (`Models/LiveSessionsView.cs:6`): `DashboardForm` solo sustituye `_liveView` desde el provider cuando `LiveSessionsEnabled==true` (DashboardForm.cs:358/384); si no, `_liveView = new()` queda Idle. Con la puerta relajada se pinta un gato Idle limpio.
- **Sin NRE nuevo**: `live` siempre no-null, `mascot`/`mascotMood` inicializados.

### 1.2 Fix de render (T0, mínimo y correcto)

Cambiar la puerta de visibilidad para **desacoplar de `LiveSessionsEnabled`**:

```csharp
// DashboardHeader.cs:80
if (cfg.ShowMascot)   // antes: if (cfg.LiveSessionsEnabled && cfg.ShowMascot)
```

Reglas que se MANTIENEN intactas (no se tocan) para que el gato sea **ambiente cuando los hooks están off y reactivo cuando están on**:

- **Bote** (`SyncBounce`, DashboardForm.cs:125-126) ya exige `LiveSessionsEnabled && ShowMascot && NeedsAttention()` → con live off, sin bote; `MascotBounceOffsetY()` = 0; el transform del header es no-op.
- **Fast-tick / mascotAlive** (DashboardForm.cs:310-311 y 366-367) ya exigen `LiveSessionsEnabled && IsAnimatedPhase` → Idle nunca califica, sin repaint desperdiciado. El frame Idle estático se pinta una vez por layout normal.
- `SampleMascot()` con phase=Idle → FrameIndex 0 (Idle = 1 frame, no animado), sin spinner, verbo "napping" calmado. reduce-motion colapsa a StaticState.

Invariante de layout preservado: el bloque reserva alto idéntico en medir y pintar (el bote se fuerza a 0 con `draw=false`, y el alto del verbo se reserva en ambas pasadas). La puerta es independiente de `draw`, así que medir==pintar.

**Comportamiento deseado tras T0**: con `ShowMascot=on` y `LiveSessionsEnabled=off`, se ve un gato Idle estático y silencioso (napping). Cuando se instalan hooks (`LiveSessionsEnabled=on`), el mismo gato cobra vida y reacciona a las fases.

### 1.3 Segundo frente del bug (layout del header con mascota — entra en T0/Tn+1)

La auditoría visual confirma que cuando la mascota está activa, **roba ancho** a la columna de texto del header y la línea de salud se corta en el borde derecho (`⚠ mié 13:4` en vez de `13:40`). Es violación de objetivos 5 y 6. Decisión de diseño:

- **Reservar un ancho mínimo** para la columna de texto del header, o (preferido, más Apple) **medir y elidir** la línea de salud con elipsis medido en `draw=false` garantizando margen derecho ≥ `Spacing.Md` (12px) antes del borde. La línea de salud NUNCA debe rebasar `rightEdge - padding`.
- Dar aire al spinner de proceso (`⋮•`) respecto al borde del ASCII (margen ≥ `Spacing.Sm`).

Este arreglo de layout del header se trata como parte del "se ve bien" (no solo "se ve"): T0 resuelve la visibilidad; el ajuste de medición/elipsis del header va junto a los arreglos visuales (Tn+1).

---

## 2. Rediseño del panel de ajustes (Apple, minimalista, intuitivo)

### 2.1 Estado actual (auditoría) — qué está mal

El panel `UI/dashboard/DashboardSettingsView.cs` es **una sola columna de ~20 controles en 8 grupos** (Secciones, Sesiones en vivo, Notificaciones, Frecuencia, Icono, Apariencia, Idioma, Sistema), con:

- **Ritmo plano**: `GroupHeader` avanza 20px, filas 20/24px, SIN aire extra entre grupos ni antes de cada header → no se percibe agrupación (anti-Apple). `y += 22` hardcodeado en hitos y `ButtonRow` (h+8) rompen la cadencia.
- **Jerarquía invertida**: el `GroupHeader` usa `TextSecondary` (más tenue) a casi el mismo tamaño que el contenido (`TextPrimary`). El título de sección no "manda".
- **Cortes reales**: la fila de **Frecuencia** (`30 segundos / 1 minuto / 5 minutos / 15 minutos`, 4 segmentos largos right-aligned) ronda ~300-320px sobre un contenido útil de ~304px → el primer segmento toca o pasa el borde izquierdo (render lo confirma: `gundos`). **Tema** va al límite. **Posición/Idioma** (CycleRow) pintan el valor a la derecha con `MeasureString` sin truncar y pueden solaparse con la etiqueta (sobre todo `PosCustom` "Personalizada (arrastra el panel)").
- **Checkboxes** con glifos Unicode `☑/☐` en la fuente de texto: poca affordance, peso/alineación inconsistentes.
- **Duplicados/cosas dobles**:
  - **Mascota huérfana** (causa del bug, también de UX): "Mostrar mascota" se ofrece suelto y POR ENCIMA del botón maestro de hooks, sin deshabilitarse cuando el master está off.
  - **Umbral de color** (70/90 · 80/95 · 60/85) vs **hitos de notificación** (25/50/75/95): dos sitios distintos para "a partir de qué % me preocupo".
  - **Dos patrones de selección** sin criterio: SegmentedRow vs CycleRow.
  - **Acciones dispersas**: "Importar .itermcolors" perdido dentro de Apariencia; "Sistema" sobrecargada.
- **i18n roto**: título de grupo `"Sistema"` HARDCODEADO (DashboardSettingsView.cs, llamada a `GroupHeader` con literal), no sale de `Strings`.

### 2.2 Patrones de inspiración adoptados (Apple / apps de muestra)

De CodexBar (PreferencesView/Panes), ClaudeBar-macOS (SettingsView), notchi (PanelSettingsView/SettingsLayout) y Buddi (NavigationSplitView) — **inspiración conceptual, cero copia de código (son GPL/Swift)**:

1. **Panes navegables** en vez de una lista larga: dominios separados, navegados por una **tab-bar de pills** en la cabecera del panel. En GDI: reutilizar `DashboardDataView.DrawSegments` como tab-bar (activo en `Theme.Accent`). En 2ª pasada se dibuja **solo el pane activo** → menos altura, sin scroll, sin textos apretados, y cada pasada devuelve el mismo `y` del pane visible.
2. **Section header** = caption en MAYÚSCULAS, tenue, `smallFont` (más pequeña que el body), en `TextMuted`, **separada por un Divider de 1px** (`Theme.Separator`) con `Spacing.Md` arriba y `Spacing.Sm` abajo. El título NO compite con las filas.
3. **Fila de dos partes**: izquierda = título (`labelFont`/`TextPrimary`) + subtítulo opcional debajo (`smallFont`/`TextMuted`, una línea corta); derecha = UN control. "Una acción por fila". El subtítulo mata los textos crípticos.
4. **Toggle pill dibujado a mano**: cápsula (`Shapes.FillRounded`) + knob circular que se desliza; track `Theme.Accent` cuando ON, `Theme.Separator` cuando OFF. Sustituye `☑/☐`. Más legible, sin glifos Unicode frágiles.
5. **Master + dependientes**: un control maestro y debajo sub-ajustes **indentados** (`Spacing.Lg`) y **atenuados (opacity ~0.5) + inertes** cuando el master está off. Resuelve los controles muertos y "lo activé y no pasa nada".
6. **StatusBadge** a la derecha de la fila de integración (mini RoundedRect + texto 1 línea centrado, color semántico `Theme.Ok`/`Warn`/`Critical`, truncado). Comunica el estado real de algo accionable fuera de la app.
7. **Picker vs Segmented por heurística formalizada**: 2-4 opciones cortas → segmentos; >4 o etiquetas largas → CycleRow `< valor >`. Evita meter 5 segmentos que no caben.
8. **Acciones del sistema al FINAL**, separadas, fuera del flujo de toggles (versión, logs, GitHub, importar tema, arrancar con Windows).
9. **Tokens de layout centralizados**: mapear todo a la rejilla `Spacing` (sustituir los +20/+22/+24/+8 mágicos).
10. **Un único lenguaje de "seleccionado"** (Accent + Contrast) y un único toggle (pill) en todo el panel.

### 2.3 Estructura final (decisión del coordinador)

**4 panes navegables** por una tab-bar de pills en la cabecera del panel (debajo de la fila "‹ Ajustes"). Orden y contenido:

#### TAB-BAR (chrome del panel)
`[ General · Pantalla · En vivo · Sistema ]` — fila de segmentos (`DrawSegments`), activo en `Theme.Accent`. El pane activo se guarda en estado de UI del form (`_settingsTab`), default "General". La tab-bar registra rects `tab:general|display|live|system`. **No persiste en `AppConfig`** (es navegación de UI, no preferencia); vive en el form y se resetea al cerrar el panel.

#### PANE 1 — GENERAL (lo de cada día)
- **Sección "PANEL"** (qué se ve en el dashboard):
  - Estimación de gasto (`toggle:ShowSpend`) — sub: "Coste equivalente por modelo"
  - Estado del servicio (`toggle:ShowHealth`)
  - Gráfica de uso (`toggle:ShowChart`)
- **Sección "ACTUALIZACIÓN"**:
  - Frecuencia (SegmentedRow `freq`) — **etiquetas acortadas a `30s / 1m / 5m / 15m`** (resuelve el corte; ver §2.5)
- **Sección "NOTIFICACIONES"**:
  - Notificaciones (`toggle:Notifications`) — **MASTER**
    - └ (dependientes, indentados `Spacing.Lg`, atenuados+inertes si master OFF) Alertas de ritmo (`toggle:PaceAlerts`)
    - └ Avisar al llegar a… (MultiSegment 25/50/75/95 sobre `NotifyMilestones`) — vía helper unificado (ver §2.6)

> **Decisión sobre "Umbral de color" (duplicado vs hitos)**: el umbral warn/crit (70/90·80/95·60/85) controla el **color del icono/barras** (cuándo el % se pone ámbar/rojo), mientras que los hitos (25/50/75/95) controlan **cuándo notifica**. Son funciones distintas pero la auditoría las marca como solapadas conceptualmente. Se **separan por dominio** para deshacer la confusión: el umbral de color es de presentación → va al pane **Pantalla** bajo "ICONO DE BANDEJA" (junto a Contenido), NO en Notificaciones. Así cada uno vive donde corresponde y no compiten en la misma pantalla.

#### PANE 2 — PANTALLA (cómo se ve)
- **Sección "ICONO DE BANDEJA"**:
  - Contenido (SegmentedRow `icon` %/▲/%▲) — sub: "Qué muestra el icono de la bandeja"
  - Umbral de color (SegmentedRow `threshold` 70/90·80/95·60/85) — sub: "% en que el icono pasa a ámbar / rojo"
- **Sección "TEMA"**:
  - Tema (SegmentedRow `theme` Sistema/Oscuro/Claro/CLI)
- **Sección "VENTANA DEL PANEL"**:
  - Posición (CycleRow `cycle:position` — 5 opciones)
  - Opacidad (SegmentedRow `opacity` 100/85/70)
  - Fijar al perder foco (`toggle:Sticky`)
  - Siempre visible (`toggle:OnTop`)
  - Reducir movimiento (`toggle:ReduceMotion`) — sub: "Desactiva animaciones"

#### PANE 3 — EN VIVO (resuelve el bug de UX de la mascota + duplicados)
- **Sección "SESIONES EN VIVO"**:
  - **FILA MAESTRA** = Activar sesiones en vivo (`special:hooktoggle`, instala/quita hooks en `~/.claude/settings.json` y enciende `LiveSessionsEnabled`) con **StatusBadge** a la derecha: verde "Activas" si `HookInstaller.IsInstalled()`, ámbar "Instalar" si no. Sub: "Recibe el estado de tus sesiones de Claude Code".
    - └ (DEPENDIENTES, indentados `Spacing.Lg`, atenuados opacity ~0.5 + **inertes** si `!HookInstaller.IsInstalled()`):
      - Mostrar mascota (`toggle:ShowMascot`)
      - Tamaño de la mascota (SegmentedRow `mascotsize` Compacta/Grande)
      - Silenciar con la app enfocada (`toggle:Suppress`)

> Esto elimina el control huérfano: "Mostrar mascota" deja de ser una fila suelta por encima del master. Pasa a depender visiblemente de "Activar sesiones en vivo". **Nota**: T0 ya hace que el gato se vea con `ShowMascot=on` aunque live esté off; pero la mascota Idle estática solo cobra vida (verbos cambiantes, bote) cuando hay hooks. La jerarquía master→dependiente comunica eso correctamente.

#### PANE 4 — SISTEMA / ACERCA DE (acciones, al final)
- **Sección "SISTEMA"** (título **localizado**, sale de `Strings`, fin del hardcode):
  - Arrancar con Windows (`toggle:Startup`)
  - Idioma (CycleRow `cycle:lang`)
- **Sección "ACERCA DE"**:
  - Versión (texto, no clicable)
  - Importar tema .itermcolors (`special:importtheme`, ButtonRow) — sacado de Apariencia para no mezclar acción con preferencias
  - Abrir carpeta de logs (`special:openlogs`, ButtonRow) — si no existe la acción en el host, queda fuera del MVP; ver plan
  - Ver en GitHub (`special:opengithub`, ButtonRow) — idem

> Las acciones que requieran un `special:*` nuevo en el host (logs/github) son **opcionales** en este v0.3.5: si el host aún no las enruta, NO se dibujan (no pintar un botón muerto). "Importar tema" y "Arrancar con Windows" ya existen y se reubican.

### 2.4 Reglas de espaciado (rejilla 8pt)

Sustituir TODOS los avances mágicos por constantes derivadas de `Spacing`, manteniendo simetría medir/pintar:

- **Tab-bar**: alto de pills + `Spacing.Md` debajo antes del primer header.
- **SectionHeader**: `Spacing.Md` (12) de aire ARRIBA + texto MAYÚS tenue + **Divider 1px** (`Theme.Separator`) + `Spacing.Sm` (8) abajo.
- **Fila estándar**: alto de contenido (1 línea, o 2 si hay subtítulo) + `Spacing.Sm` (8) de separación entre filas del mismo grupo.
- **Dependientes indentados**: `x + Spacing.Lg` (16) de sangría; mismo ritmo vertical.
- **ButtonRow**: mantiene su h, pero el avance se expresa como `h + Spacing.Sm`.
- **Gutter mínimo label↔control en la misma fila**: reservar `≥ Spacing.Md` entre el final de la etiqueta y el inicio del control/segmentos. Si no cabe en una línea → fallback a 2 líneas (label arriba / control debajo).
- **Margen derecho de seguridad**: ningún chip/segmento llega a `x+w`; reservar `≥ Spacing.Sm` de margen derecho interno.
- **Cierre del panel**: tras `Draw`, el colchón inferior pasa a `Spacing.Lg`.

### 2.5 Anti-truncamiento (medir y envolver/crecer, NUNCA cortar)

Regla general: **ningún helper confía en que el texto entra en `w`**. Todo texto de longitud variable se mide en `draw=false` y, si excede el ancho útil:

1. **Frecuencia**: etiquetas compactas `30s / 1m / 5m / 15m` (estilo Apple). Si aun así no caben, reducir padding interno del chip antes de dibujar; nunca dibujar el primer chip con `x < contentLeft`.
2. **CycleRow (Posición / Idioma)**: medir la etiqueta izquierda y el valor derecho; si la suma + gutter `Spacing.Md` excede `w`, **elidir el valor derecho con elipsis medido** (caso `PosCustom`), o caer a 2 líneas (label arriba, valor debajo). Nunca solapar.
3. **SegmentedRow**: calcular ancho total de segmentos en `draw=false`; si excede el ancho de contenido, **envolver a 2 filas** o abreviar. El primer segmento jamás se pinta a la izquierda de `contentLeft`.
4. **Filas con label + control que no caben** (p.ej. label largo + chips): fallback a 2 líneas con gap `≥ Spacing.Md`.
5. **Header con mascota** (§1.3): línea de salud elidida con elipsis medido, margen derecho `≥ Spacing.Md`.
6. **StatusBadge**: texto corto de 1 línea, `truncationMode tail`, nunca multi-línea.

Invariante: la decisión de elidir/envolver se toma con la MISMA medición en `draw=false` y `draw=true`, de modo que el `y` resultante es idéntico en ambas pasadas.

### 2.6 Eliminación de duplicados (obj 4)

- **Mascota**: "Mostrar mascota" deja de duplicar/competir con el gate de hooks → pasa a dependiente bajo el master "Activar sesiones en vivo" (pane En vivo).
- **Hitos de notificación**: encapsular un único **`MultiSegmentRow`** helper (multi-activo nativo) en vez del re-pintado manual por encima de `DrawSegments`. Un solo estilo de pill.
- **Acciones**: importar tema, (logs, github) y arrancar con Windows consolidadas en el pane Sistema/Acerca de, no esparcidas por Apariencia.
- **Selección**: un único lenguaje "seleccionado" (Accent + Contrast) y un único toggle (pill) en todo el panel.
- **Plan subtítulo** (header): "Max · Max 5x" repite "Max" → mostrar una sola vez ("Plan Max · 5x").

---

## 3. Arreglos visuales del dashboard (de la auditoría visual)

Aplicar junto al rediseño (Tn+1), todos medidos en `draw=false`:

1. **[blocker] Header con mascota — corte de la fecha de salud**: ver §1.3. Elidir la línea de salud con elipsis medido, margen derecho `≥ Spacing.Md`; aire al spinner.
2. **[major] Gráfica — eje X primer label cortado por la izquierda** (`Jun 00h` → `un 00h`): alinear la primera etiqueta a la IZQUIERDA del plot (`StringAlignment.Near`, `x = plotLeft`) en vez de centrarla bajo su tick, o empujar `plotLeft` si la etiqueta más a la izquierda no cabe con margen `≥ Spacing.Sm`. Medir el ancho del primer label en `draw=false`.
3. **[minor] Línea Sonnet 7d pegada a la línea de salud**: añadir gap `Spacing.Sm` y/o alinear el `%` de Sonnet a la misma rejilla derecha que los demás porcentajes; considerar separador sutil entre bloque de salud y bloque Sonnet.
4. **[minor] Header — plan duplicado y bloque de estado pegado a X/engranaje**: mostrar el plan una vez ("Plan Max · 5x"); dar margen superior `Spacing.Sm` al bloque de estado.
5. **[minor] Footer wrap feo**: acortar textos para que quepan en una línea (ya hay `FooterLayout` puro); subir un punto el contraste del gris del footer.
6. **[minor] Tema claro — contraste pobre** (engranaje, gris tenue, verdes): verificar `TextMuted` y colores de estado en `Theme.Light` con `ColorMath.Contrast` contra fondo claro; subir el muted y oscurecer el verde de éxito hasta ratio `≥ 4.5:1` para texto pequeño.

Prioridad: 1 y 2 (cortes, blocker/major) antes que 3-6 (pulido).

---

## 4. Criterios de aceptación

1. **Build limpio**: `dotnet build ClaudeBarWin.sln` sin errores ni warnings nuevos.
2. **Suite verde**: `dotnet test` pasa (baseline ~227 incluyendo casos `[Theory]`; ~212 métodos `[Fact]/[Theory]`). Tests nuevos para: simetría medir/pintar de los nuevos helpers, no-truncamiento (medición), MultiSegment multi-activo, dependientes inertes cuando el master está off, `ActionFor` de las claves migradas, navegación de tabs (rect activo → pane correcto).
3. **Render sin textos cortados**: `--render-test` / `--render-demo` / `--render-gif` no muestran ningún texto cortado (ni `gundos`, ni `un 00h`, ni `mié 13:4`). Verificación visual de los PNG del pane de ajustes (los 4 panes) y del header con mascota.
4. **Espaciado y legibilidad**: se percibe la agrupación (aire entre secciones, headers en mayúsculas tenues con divider); ningún chip pegado al borde; pills de toggle legibles sin glifos Unicode.
5. **Mascota visible**: con `ShowMascot=on` y `LiveSessionsEnabled=off`, la mascota Idle **se ve** en el header (gato napping estático). Con hooks instalados, reacciona a las fases. Verificado en render y, si es viable, manualmente.
6. **Invariante 2 pasadas**: para cada pane y helper nuevo, `draw=false` y `draw=true` devuelven el MISMO `y` (test + aserción de layout). El pane inactivo no contribuye al alto.
7. **i18n**: ningún título de grupo hardcodeado; "Sistema" sale de `Strings` en los idiomas existentes.
8. **Sin duplicados**: una sola forma de toggle (pill), un solo estilo de "seleccionado", mascota como dependiente, acciones consolidadas en Sistema/Acerca de.

---

## 5. Archivos afectados (referencia)

- `UI/dashboard/DashboardHeader.cs` — fix puerta mascota (T0) + elipsis línea de salud + aire spinner/plan.
- `UI/dashboard/MascotRenderer.cs` — (solo lectura/verificación de alto>0; no debería requerir cambio).
- `UI/dashboard/DashboardSettingsView.cs` — reescritura a 4 panes: tab-bar, SectionHeader (mayús+divider), Row con subtítulo, TogglePill, StatusBadge, MultiSegmentRow, dependientes atenuados, heurística picker/segmented, anti-truncamiento, espaciado por `Spacing`, "Sistema" localizado.
- `UI/dashboard/DashboardDataView.cs` — eje X gráfica (primer label), línea Sonnet spacing, footer; `DrawSegments` reutilizado por tab-bar.
- `UI/DashboardForm.cs` — estado `_settingsTab`, routing de `tab:*`, colchón inferior `Spacing.Lg`, fila tab-bar tras "‹ Ajustes".
- `Services/Localization.cs` — `MenuSystem` (y subtítulos/labels nuevos), strings de tabs y badges, en todos los idiomas.
- `Services/Theme.cs` — ajuste de contraste de `Theme.Light` (muted + verde éxito).
- `Config/AppConfig.cs` — sin cambios de defaults para el fix (el desacople es en el render); revisar solo si algún nuevo `special:*` lo necesita.
- `ClaudeBarWin.Tests/` — tests nuevos descritos en §4.

---

## 6. Fuera de alcance (v0.3.5)

- Nuevos tipos de mascota (`MascotKind` sigue "cat").
- Persistir el pane activo entre aperturas del panel.
- Acciones `special:openlogs` / `special:opengithub` si el host no las soporta ya (se dejan listas en diseño pero no se pintan si no hay routing).
- Sidebar vertical estilo Buddi (se elige tab-bar horizontal por encajar en el ancho de 340px sin robar columna).
