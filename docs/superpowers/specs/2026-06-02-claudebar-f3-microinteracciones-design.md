# ClaudeBar-win — Fase 3: Microinteracciones / motor de easing (design)

**Fecha:** 2026-06-02
**Estado:** spec en revisión (deriva del roadmap `2026-06-02-claudebar-apple-roadmap.md`, Fase 3).
**Hereda de:** F1 (tokens `Theme`, `Spacing`, `ColorMath`, `Typography`, `Shapes`) + F2 (`QuotaBar` unificada, `PaceResult.IdealPct`, formas a11y, `UsageFormat`). Build base: `feat/live-sessions`, **103 tests verdes**, tag `v0.3.3`.

> Objetivo: que la app **deje de saltar en seco**. Hoy todo repinta 1×/seg (frame de la mascota) y la apertura del panel, los números, las barras y los estados aparecen sin transición. F3 introduce un **motor de easing** y lo aplica a números/barras, hover, apertura del panel, entrada de secciones y a la **vida de la mascota** — con una **rama "off" obligatoria** (toggle "reducir movimiento"). Es la palanca nº2 del roadmap.

---

## 0. Restricción rectora: CPU en reposo (app 24/7)

ClaudeBar corre **todo el día** lanzada en `feat/live-sessions`. La animación NO puede convertirse en un bucle que queme CPU en reposo. Tres invariantes:

1. **Todo lo animado vive en el panel**, y el panel solo repinta **mientras es visible** (hoy `_tick` ya hace `if (Visible)` y se para en `OnVisibleChanged`). El panel se autocierra al perder foco (~600 ms). ⇒ con el panel oculto, **0 trabajo extra**.
2. El **tray icon NO se anima por frame**: sigue regenerándose en la cadencia de `RefreshSeconds` (60 s por defecto). La "vida" va en el panel, no en el badge de 16px. (El estado por forma/color del tray, de F2, se queda como está.)
3. El reloj de animación es **bajo demanda**: tick rápido (~33 ms ≈ 30 fps) **solo** mientras (a) hay un tween en vuelo, (b) la mascota está en una fase animada, o (c) hay una entrada/bounce/celebración activa **y** el panel es visible. Cuando todo se asienta, **baja a 1 s** (refresco de countdown) o se para. Esta decisión es una función pura testeable (`MotionScheduler`).

> Consecuencia de diseño: el coste de F3 se paga **solo mientras el usuario tiene el panel abierto**, que son segundos. El idle (panel cerrado) queda intacto. Esto es lo que hace la fase viable en un proceso permanente.

## 1. Arquitectura del motor (núcleo puro + capa GDI+)

El render es **inmediato, 2 pasadas** (medir `draw=false` / pintar `draw=true`); no hay árbol retenido ni binding. Por tanto los valores animados se **muestrean en tiempo de pintado** desde un estado que vive en `DashboardForm`. Todo el cálculo es **puro y elapsed-driven** (recibe `elapsedMs`, nunca lee el reloj ni `Math.Random` por dentro) → 100% testeable sin GDI+, sin red, sin reloj real.

Fuente de tiempo: un `System.Diagnostics.Stopwatch` (monótono, inmune a cambios de hora) propiedad del form; cada tick pasa el `elapsedMs` al estado.

### Piezas nuevas (todas puras salvo donde se note)
- `Services/Motion/Easing.cs` — funciones de easing puras: `OutCubic`, `OutQuad`, `InOutCubic`, `OutBack` (para el bounce). `t∈[0,1]→valor`.
- `Services/Motion/AnimatedValue.cs` — valor que **avanza** hacia un objetivo con easing y duración. `Set(target)` rearma; `Advance(elapsedMs)` lo acerca; `Value` lo muestrea. `Snap()` o `reduceMotion ⇒ Value==target` al instante.
- `Services/Motion/MotionScheduler.cs` — puro: dado (¿algo animando?, ¿panel visible?, ¿mascota viva?) → `DesiredIntervalMs` (33 si activo, 1000 si solo countdown, parado si oculto) y `WantsFastTick`.
- `Services/Motion/Stagger.cs` — puro: `Alpha(tSinceOpenMs, index, staggerMs, durMs)`∈[0,1] y `OffsetY(alpha, maxPx)` para la entrada escalonada (traslación, sin alfa por glifo).
- `Services/Motion/Bounce.cs` — puro: `OffsetY(elapsedMs, amplitudePx, periodMs, repeats)` para el bounce de atención (ease-out-back que decae).
- `Services/Motion/ResetDetector.cs` — puro: detecta que una ventana **se ha reseteado** (el `ResetsAt` salta hacia adelante o la utilización cae en picado) → dispara la celebración una sola vez.
- `Services/Mascot/MascotAnimator.cs` — puro: dada (fase, `elapsedMsEnFase`, semilla determinista) → `{ frameIndex, blinking, spinnerGlyph, verbIndex }`. Encapsula tempos por fase + blink con jitter determinista (sin `Random`: jitter = hash de un contador) + spinner de glifos + selección de verbo.
- `Services/Mascot/MascotMood.cs` — puro: máquina de **humor** con **histéresis** (dwell mínimo antes de cambiar) y **decay** (vuelve a neutro tras N ms). Eventos: atención requerida, reset celebrado, procesado largo.

### Capa GDI+ / estado (modificada)
- `DashboardForm` posee el `Stopwatch`, el `MotionScheduler`, el diccionario de `AnimatedValue` por clave (`"num:crit"`, `"bar:5h"`, `"bar:7d"`, `"pace"`), el `_hoveredKey`, el `_openedAtMs`, el `MascotAnimator`/`MascotMood`. Sube el `_tick.Interval` de 1000→adaptativo. Aplica fade de opacidad al abrir, Esc para cerrar, hover highlight, stagger por sección.
- `QuotaBar.Draw` y `DashboardHeader`/`DashboardDataView` reciben un **sampler opcional** (`MotionState? motion`) para leer el valor eased; si es `null` (render-test, reduce-motion) caen al valor crudo (estado final). Invariante medir/pintar intacto: las animaciones **no cambian el `y` de layout** (mueven píxeles dentro de su celda vía `TranslateTransform`/opacidad, nunca el alto reservado).

## 2. Las microinteracciones (diseño detallado)

### 2.1 Tween de números y barras (ease-out ~200 ms)
- El `%` crítico de la cabecera, el **ancho de relleno** de cada barra (5h/7d/crítica) y el `pace %` pasan por un `AnimatedValue` con `OutCubic`, `dur≈220 ms`. Al llegar un `snapshot` nuevo (`UpdateData`), se hace `Set(target)`; el paint lee `Value`.
- `QuotaBar.Draw` admite un `displayUtil` eased (override del `win.UtilizationPct`) para el ancho y el número; el color (`RiskColor`/pace) se calcula con el valor **objetivo** (no parpadea de color durante el tween) salvo el degradado, que puede seguir el eased para suavidad. Decisión: **número y ancho eased; color por objetivo**.
- Render-test ⇒ sin motion ⇒ valor final (igual que hoy).

### 2.2 Hover states sutiles
- `DashboardForm` rastrea `_hoveredKey` = clave del rect interactivo bajo el cursor (reusa los diccionarios ya existentes: `_closeRect`, `_gearRect`, `_sectionRects`, `_tabRects`, `_modeRects`, `_pctWinRects`, `_liveRowRects`, `_backRect`, `_settingsRects`). `OnMouseMove` lo recalcula; si cambia, `Invalidate()`.
- Hit-test puro y testeable: `HoverHitTest.Resolve(point, rects)` → clave o null.
- Render: fondo redondeado sutil (`theme.BgElevated`, `Shapes.FillRounded`) **detrás** del elemento bajo el cursor, con un fade-in corto (≤120 ms, `AnimatedValue` de alfa/intensidad). Cursor `Hand` sigue como hoy.

### 2.3 Apertura del panel como superficie de sistema
- **Fade de entrada** ≤120 ms: en `ShowConfigured`, arrancar `Opacity` por debajo del objetivo (respetando `DashboardOpacity`) y animarla 0→objetivo con `OutQuad`. Opcional y sutil: 6 px de deslizamiento hacia arriba (`Location`) durante el mismo tramo.
- **Dismiss**: el de **foco** ya existe (`OnDeactivate → Hide`). F3 añade **Esc** vía `ProcessCmdKey(Keys.Escape) → Hide()`. (Click fuera/✕ ya funcionan.)
- **reduce-motion ⇒** `Show()` directo a opacidad objetivo, sin slide.

### 2.4 Entrada escalonada de secciones
- Al abrir, cada sección "se asienta": traslación vertical `OffsetY` de ~6 px → 0 con desfase por índice (`staggerMs≈40`, `durMs≈180`), vía `g.TranslateTransform` alrededor del draw de cada bloque (el `y` de layout NO se toca; solo se desplaza el dibujo). Orden: cabecera → secciones de datos (cuota/sesiones/gasto/gráfica) → footer.
- `DashboardDataView.Draw` recibe el `tSinceOpenMs` y aplica el stagger por sección con el índice que ya maneja. `Stagger.Alpha`/`OffsetY` son puros.
- Sin alfa por glifo (GDI+ lo complica): el efecto es **traslación + el fade global de opacidad** del 2.3. reduce-motion ⇒ todo a 0 (estático).

### 2.5 Vida de la mascota (la pieza con más personalidad)
Hoy: 1-2 frames por fase, swap a 1 Hz, color por fase. F3 le da **vida** mediante `MascotAnimator` + `MascotMood`, **manteniendo el bestiario ASCII clean-room** de `MascotSprite` (no se reemplazan los sprites; se enriquece **cuándo/cómo** se eligen frames y qué texto los acompaña):
- **Tempos por fase**: cadencia de parpadeo/pulso distinta por fase (Processing más vivo, Idle lento, WaitingForApproval pulsa con urgencia). Hoy es un 1 Hz plano.
- **Blink con jitter determinista**: el parpadeo NO es metronómico; el intervalo varía con un jitter calculado de un contador (sin `Random`, para que el test sea determinista).
- **Idle peek**: cada cierto rato en Idle, un "vistazo" breve (ojos/orejas) para que no parezca congelada.
- **Spinner de glifos**: en Processing/Compacting, un pequeño spinner (ciclo de glifos, p.ej. braille `⠋⠙⠹…` o `·∶∷`) junto a la mascota indica trabajo vivo.
- **Verbos con personalidad** (decidido **JUGUETÓN**): etiqueta corta junto a la mascota con un verbo de estado animado con elipsis ("pensando…", "te espera…", "rumiando…"). **Pool de 3-5 por fase** con guiño/carácter (no metronómico: rota con jitter), en los 8 idiomas. Tono propio, clean-room (no copiar los 200+ verbos de Notchi, que es GPL — solo el concepto).
- **Emociones con histéresis/decay** (`MascotMood`, decidido **expresivo**): el humor reacciona a eventos (atención → alerta; reset → contento; procesado largo → concentrado) y **decae** a neutro, con un rango emocional algo más marcado (juguetón). La **histéresis** (dwell mínimo) evita el parpadeo de humor ante cambios rápidos de fase. Es una máquina de estados pura. El **idle peek** es algo **más frecuente** (registro juguetón) pero sin distraer.

### 2.6 Bounce de atención + celebración de reset
- **Bounce de atención**: al entrar la fase global en `WaitingForApproval`/`WaitingForInput`, la mascota da un bote vertical breve (`Bounce.OffsetY`, `OutBack`, pocos px, 2-3 rebotes que decaen); se re-dispara cada cierto intervalo mientras la atención persista (sin volverse molesto). Refuerza la señal que F2 ya da por forma/color.
- **Celebración de reset**: cuando una ventana se resetea (`ResetDetector`: el `ResetsAt` salta adelante o la utilización cae en picado), un destello breve in-panel ("✓ cuota renovada" + humor contento de la mascota). Es el primo in-panel de la noti "cuota renovada" que diseñará F4 (aquí NO se toca el sistema de notis; solo la celebración visual del panel).

### 2.7 Toggle "reducir movimiento" (requisito duro del roadmap)
- `AppConfig.ReduceMotion` (bool). **Default decidido = `false` (animaciones ON)**: F3 arranca con todo animado; el usuario lo apaga si molesta. (Se deja el helper testeable `MotionPrefs.OsReducedMotion()` envolviendo `SPI_GETCLIENTAREAANIMATION` para una posible opción "seguir Windows" futura, pero el default NO depende del SO.)
- **Gate único**: cuando está activo, **toda** animación colapsa a su estado final al instante — sin fade, sin tween, sin stagger, sin bounce/celebración; la mascota queda en frame estático (sin spinner ni jitter), aunque conserva color/forma por fase. El gate se aplica en una sola puerta (`AnimatedValue.Snap` + `MotionScheduler` no pide fast-tick + `MascotAnimator` devuelve frame base).
- Fila nueva en `DashboardSettingsView` + label ×8 idiomas.

### 2.8 (Polish arrastrado de F2) Truncamiento del sello/footer
Pega abierta de F2: el `LocalSeal` y el hint del footer **se truncan** al ancho fijo del panel (340 px) → "…no salen del equipo · si…". F3 lo arregla como parte del trabajo de layout del footer (envolver a 2 líneas o reordenar/medir y crecer el alto). Va en la verificación final (Tarea 8) para no abrir una fase aparte.

## 3. Criterios de aceptación
- Build 0 errores; suite **≥ 103 tests** verdes (se añaden, no se rompe ninguno). Núcleo del motor (easing/animated-value/scheduler/stagger/bounce/reset/mascot-animator/mood) cubierto por tests **puros** (sin GDI+, sin reloj real, sin red).
- **CPU en reposo invariante**: con el panel **oculto** no hay tick rápido (verificable por diseño: `MotionScheduler.WantsFastTick==false` si no visible). El fast-tick solo corre con panel visible + algo animando.
- `--render-test` captura las microinteracciones en **fotograma fijo**: panel a `tSinceOpen=0/90/200 ms` (fade+stagger+tween a medio camino), hover sobre una fila, mascota en varios frames/humores, y un fotograma de celebración. (Requiere que `PrepareForRender` acepte override de tiempo de motion.)
- Existe el toggle **reducir movimiento** y, activo, el `--render-test` con `ReduceMotion=true` produce el **estado final idéntico** al de hoy (sin offsets/alfa intermedios).
- Sin literales de color/offset nuevos: todo vía `theme.*`, `Spacing.*`, `ColorMath.*`, `Typography.*`, y constantes de motion centralizadas (duraciones/amplitudes en un único sitio, p.ej. `Motion.Durations`).
- Strings nuevos (verbos de mascota, "cuota renovada", label "reducir movimiento") en **los 8 idiomas** de `Localization.cs`.
- El footer/sello **ya no se trunca**.
- Sin push/merge: commits por tarea en `feat/live-sessions` + tag local `v0.3.4` al cerrar. Publicación a GitHub = decisión aparte de Yovan (línea roja).

## 4. Archivos afectados
- **Nuevos**: `Services/Motion/Easing.cs`, `AnimatedValue.cs`, `MotionScheduler.cs`, `Stagger.cs`, `Bounce.cs`, `ResetDetector.cs`; `Services/Mascot/MascotAnimator.cs`, `MascotMood.cs`. (+ opcional `Services/Motion/Motion.cs` con las constantes de duración/amplitud.)
- **Modificados**: `UI/DashboardForm.cs` (clock adaptativo, fade, Esc, hover, stagger, openedAt, footer sin truncar), `UI/dashboard/QuotaBar.cs` (display eased), `UI/dashboard/DashboardHeader.cs` (mascota animada + verbo + bounce + crit% eased), `UI/dashboard/DashboardDataView.cs` (hover highlight + stagger), `UI/dashboard/MascotRenderer.cs` (consume animator), `Config/AppConfig.cs` (`ReduceMotion`), `UI/dashboard/DashboardSettingsView.cs` (fila toggle), `Services/Localization.cs` (strings ×8), `Program.cs` (render-test con tiempos de motion + reduce-motion).
- **Tests nuevos**: `EasingTests`, `AnimatedValueTests`, `MotionSchedulerTests`, `StaggerTests`, `BounceTests`, `ResetDetectorTests`, `MascotAnimatorTests`, `MascotMoodTests`, `HoverHitTestTests`, (+ `AppConfigTests` para `ReduceMotion`).

## 5. Riesgos (y mitigación)
- **CPU 24/7** (el grande): mitigado por el scope panel-visible + `MotionScheduler` bajo demanda + tray sin animar. Verificable por test del scheduler.
- **Invariante medir/pintar**: las animaciones desplazan dibujo dentro de la celda (`TranslateTransform`/opacidad), nunca alteran el `y` reservado → el layout sigue determinista. Cualquier excepción se trata como bug.
- **Determinismo de tests**: nada de `Math.Random`/reloj dentro del núcleo; jitter = hash de contador, tiempo = `elapsedMs` inyectado.
- **Reduce-motion incompleto**: criterio de aceptación exige que el render-test con `ReduceMotion=true` == estado final de hoy; un solo gate evita olvidos.
- **i18n verbos**: pool pequeño y acotado; todos los strings a los 8 bloques o quedan en blanco para no-ES/EN.
- **Mascota = clean-room**: NO se copian sprites/ideas literales de Buddi/Notchi (GPL); el `MascotAnimator`/`MascotMood` y los verbos son propios. (Memoria: refs Buddi/Notchi son GPL → solo inspiración conceptual.)
- **Flicker de color durante tween**: número/ancho eased pero color por objetivo evita arcoíris.

## 🔗 Relacionado
- `2026-06-02-claudebar-apple-roadmap.md` (paraguas, 5 fases — F3 es la palanca de microinteracciones)
- `2026-06-02-claudebar-f2-senales-de-un-vistazo-design.md` (F2 — lo que F3 hereda: QuotaBar, formas, UsageFormat)
- `2026-06-01-sesiones-en-vivo-mascota-avisos-design.md` (v0.3 — la mascota y el hook que F3 anima)
