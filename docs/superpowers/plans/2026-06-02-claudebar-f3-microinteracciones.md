# Plan de implementación — Fase 3: Microinteracciones (motor de easing)

**Spec:** `docs/superpowers/specs/2026-06-02-claudebar-f3-microinteracciones-design.md`
**Rama:** `feat/live-sessions` (continúa F2, build base **103 tests verdes**, tag `v0.3.3`).
**Método:** TDD estricto por tarea (test que falla → mínimo para pasar → refactor), **commit por tarea**, sin push. `dotnet` del usuario en `C:\Users\zorro\.dotnet\dotnet.exe` (no está en el PATH del Bash → usar PowerShell: `& "$env:USERPROFILE\.dotnet\dotnet.exe" test ClaudeBarWin.Tests/ClaudeBarWin.Tests.csproj`).

> Contexto para el ejecutor: app C# .NET 9 WinForms, **render GDI+ a mano, 2 pasadas** (`draw=false` mide / `draw=true` pinta; deben devolver el **mismo `y`**). F1/F2 dejaron `Theme`, `Spacing`, `ColorMath`, `Typography`, `Shapes`, `QuotaBar`, `PaceResult.IdealPct`. **Regla de oro de F3**: todo el cálculo de animación es **puro y elapsed-driven** (recibe `elapsedMs`; jamás lee el reloj ni `Math.Random` por dentro) → testeable sin GDI+/reloj/red. Las animaciones desplazan dibujo **dentro de su celda** (`TranslateTransform`/opacidad), **nunca** cambian el alto de layout. **CPU 24/7**: el tick rápido solo corre con el **panel visible** + algo animando (ver Tarea 1). reduce-motion (Tarea 7) colapsa toda animación a su estado final por una **única puerta**.

## Estructura de archivos
- **Nuevos** `Services/Motion/`: `Easing.cs`, `AnimatedValue.cs`, `MotionScheduler.cs`, `Stagger.cs`, `Bounce.cs`, `ResetDetector.cs`, (`Motion.cs` con constantes de duración/amplitud).
- **Nuevos** `Services/Mascot/`: `MascotAnimator.cs`, `MascotMood.cs`.
- **Modificados**: `UI/DashboardForm.cs`, `UI/dashboard/QuotaBar.cs`, `DashboardHeader.cs`, `DashboardDataView.cs`, `MascotRenderer.cs`, `Config/AppConfig.cs`, `UI/dashboard/DashboardSettingsView.cs`, `Services/Localization.cs`, `Program.cs`.

---

## Tarea 0 · Motor base: Easing + AnimatedValue (puros, sin UI)
**Objetivo:** los dos ladrillos de los que cuelga todo. Cero cambios de render.
**Pasos TDD:**
1. Test (`EasingTests`): `Easing.OutCubic(0)==0`, `OutCubic(1)==1`, monótona creciente, `OutCubic(0.5)>0.5` (ease-out adelanta). Igual para `OutQuad`, `InOutCubic` (simétrica en 0.5≈0.5), `OutBack(1)==1` y que `OutBack` sobrepasa (>1 en algún t) para el bounce.
2. Test (`AnimatedValueTests`): `new AnimatedValue(0)`; `Set(100)`; `Advance(0)`→0; `Advance(dur/2)`→entre 0 y 100; `Advance(dur)`→100; `Advance(dur*2)`→100 (clamp). `Set` a media animación rearma desde el valor actual (sin salto). `Snap()`→ `Value==target`. `dur<=0`→ instantáneo. `IsAnimating` true mientras no ha llegado.
3. Implementar `Services/Motion/Easing.cs` (funciones puras) y `AnimatedValue.cs` (campos `current/target/elapsed/dur/easeFn`; `Advance(elapsedMs)` integra; `Value` muestrea). Crear `Motion.cs` con constantes (`TweenMs=220`, `FadeMs=120`, `StaggerMs=40`, `StaggerDurMs=180`, amplitudes bounce…).
4. Build + suite (103 + nuevos, todo verde).
**Commit:** `feat(motion): motor de easing — Easing + AnimatedValue (puros, TDD)`.

## Tarea 1 · Reloj bajo demanda + apertura del panel (fade + Esc)
**Objetivo:** el `MotionScheduler` que protege la CPU 24/7, el fade de entrada y el cierre con Esc.
**Pasos TDD:**
1. Test (`MotionSchedulerTests`): `WantsFastTick==false` si `visible=false` (¡aunque haya animación!); `true` si `visible && animating`; `DesiredIntervalMs`==33 si activo, ==1000 si visible sin animación (solo countdown), y reporta "parar" si no visible. Histéresis opcional: no oscila si algo acaba de terminar (margen).
2. Implementar `MotionScheduler` (puro). En `DashboardForm`: `Stopwatch` propio; `_tick.Interval` pasa a fijarse desde `scheduler.DesiredIntervalMs(...)` en cada tick (sustituye el 1000 fijo); el tick hace `Advance(elapsedDelta)` de los `AnimatedValue` activos + `Invalidate()` solo si algo cambió. En `OnVisibleChanged`: al ocultar, parar (como hoy); al mostrar, arrancar a 33.
3. **Fade de apertura**: en `ShowConfigured`, `Opacity` arranca por debajo del objetivo (`DashboardOpacity`) y un `AnimatedValue` la lleva a objetivo con `OutQuad` en `FadeMs`. Guardar `_openedAtMs = stopwatch elapsed` para el stagger (Tarea 4).
4. **Esc**: `override ProcessCmdKey` → `Keys.Escape` ⇒ `Hide(); return true;`.
5. Build + suite + `--render-test` (sin regresión visual en estado final).
**Commit:** `feat(motion): reloj bajo demanda (MotionScheduler) + fade de apertura + Esc`.

## Tarea 2 · Tween de números y barras (ease-out ~220 ms)
**Objetivo:** que el `%` y el relleno de las barras **deslicen** al cambiar el dato.
**Pasos TDD:**
1. Test: introducir un `MotionState` (contenedor de `AnimatedValue` por clave) con `Display(key, target, reduceMotion)` que devuelve eased o, si reduce-motion, el target. Test puro: tras `Set` y medio `Advance`, `Display` ∈ (prev,target); con reduce-motion == target.
2. `QuotaBar.Draw` recibe `double? displayUtil` (override eased del ancho y el número). El **color** se calcula con la utilización **objetivo** (no parpadea); el ancho/número usan `displayUtil ?? util`. Si `displayUtil` null (render-test/cabecera sin motion) ⇒ comportamiento idéntico a hoy.
3. `DashboardForm` mantiene los `AnimatedValue` (`bar:5h`, `bar:7d`, `num:crit`, `pace`); `UpdateData` hace `Set(target)` con los nuevos %; el paint pasa el eased a `QuotaBar`/cabecera. Marcar `animating` para el scheduler.
4. Build + suite + render (estado final == hoy; el tween solo se ve en vivo).
**Commit:** `feat(motion): tween de números y barras (ease-out)`.

## Tarea 3 · Hover states sutiles
**Pasos TDD:**
1. Test (`HoverHitTestTests`): `HoverHitTest.Resolve(point, rects)` → clave correcta cuando el punto cae dentro; null fuera; precedencia estable si solapan (orden definido).
2. `DashboardForm._hoveredKey`: `OnMouseMove` lo recalcula sobre los diccionarios existentes; si cambia ⇒ `Invalidate()`. Un `AnimatedValue` de intensidad para el fade-in del realce (≤120 ms).
3. Render: antes de pintar el elemento bajo el cursor, `Shapes.FillRounded` con `theme.BgElevated` (o alfa baja) detrás de su rect, intensidad = eased. No alterar layout. Cursor `Hand` como hoy.
4. Build + suite + render (forzar `_hoveredKey` en render-test sobre una fila para capturarlo).
**Commit:** `feat(ui): hover states con realce eased`.

## Tarea 4 · Entrada escalonada de secciones
**Pasos TDD:**
1. Test (`StaggerTests`): `Stagger.Alpha(t, index, stagger, dur)`==0 si `t<index*stagger`; sube en el tramo; ==1 pasado `index*stagger+dur`. `OffsetY(alpha,max)`==max en alpha 0, 0 en alpha 1.
2. `DashboardForm` pasa `tSinceOpenMs = elapsed - _openedAtMs` a la cabecera y a `DashboardDataView.Draw`. Cada bloque se dibuja envuelto en `g.TranslateTransform(0, OffsetY(...))` (y lo deshace); el `y` de layout NO cambia. Orden de índices: cabecera=0, secciones de datos=1..n, footer=n+1.
3. `DashboardDataView.Draw` aplica el stagger por sección con su índice. reduce-motion ⇒ offset 0 (estático).
4. Build + suite + render (capturar `tSinceOpen` a medio: 90 ms ⇒ secciones a distinto offset).
**Commit:** `feat(ui): entrada escalonada de secciones al abrir`.

## Tarea 5 · Vida de la mascota (animator + humor)
**Objetivo:** tempos por fase, blink con jitter, idle peek, spinner de glifos, verbos, y humor con histéresis/decay. **Mantener el bestiario ASCII existente** (no se reemplazan sprites).
**Pasos TDD:**
1. Test (`MascotAnimatorTests`): dada (fase, `elapsedMs`, semilla) → `frameIndex` válido; en `Idle` el blink es **esporádico** (no en cada tick) y **determinista** para misma semilla; en `Processing` el `spinnerGlyph` **cicla** por una secuencia; `verbIndex` ∈ rango del pool de la fase. Sin `Random`/reloj dentro.
2. Test (`MascotMoodTests`): `Update(phase, events, elapsedMs)` — entra en `Alert` al pedir atención y **no** sale antes del dwell (histéresis); decae a `Neutral` tras `DecayMs` sin eventos; `Happy` tras evento reset y decae.
3. Implementar `MascotAnimator` (tempos por fase + jitter = hash de contador + spinner + verbo) y `MascotMood` (histéresis + decay). `MascotRenderer.Draw` pasa a recibir el output del animator (frame elegido + color por fase/humor) en vez de `frameIndex % Count` plano; dibuja además el spinner y, junto a la mascota, el **verbo** (string localizado) con elipsis animada.
4. Strings: `Localization.cs` — pool corto de verbos por fase (Processing/WaitingForApproval/WaitingForInput/Compacting/Idle/Ended) en los **8 idiomas**. (Nivel de personalidad acordado con Yovan; por defecto **sobrio**.)
5. `DashboardHeader.Draw` usa el animator (sustituye el `mascotFrame` plano) y reserva el alto del verbo en ambas ramas (medir/pintar).
6. Build + suite + render (`mascot-large.png` con verbo + spinner; varios frames/humores).
**Commit:** `feat(mascot): vida — tempos, blink jitter, spinner, verbos y humor (histéresis/decay)`.

## Tarea 6 · Bounce de atención + celebración de reset
**Pasos TDD:**
1. Test (`BounceTests`): `Bounce.OffsetY(0,...)==0`, pico positivo a media animación, vuelve a 0 al final; decae con los rebotes. `OutBack` usado.
2. Test (`ResetDetectorTests`): `Detect(prevResetsAt, newResetsAt)` ⇒ true cuando el nuevo reset salta hacia adelante (> umbral) o la utilización cae en picado; false en lecturas normales; dispara **una sola vez** (no re-dispara con la misma lectura).
3. `DashboardHeader`/mascota: cuando `GlobalPhase.NeedsAttention()`, aplicar `Bounce.OffsetY` (vía `TranslateTransform`) a la mascota; re-disparo periódico mientras persista. `DashboardForm` mantiene el estado del bounce y alimenta el scheduler (`animating`).
4. Celebración: `ResetDetector` en `UpdateData` (comparar `ResetsAt` previo/nuevo de 5h/7d). Al detectar ⇒ humor `Happy` + destello breve "✓ {QuotaRenewed}" in-panel (string ×8). NO tocar el sistema de notificaciones (eso es F4).
5. Build + suite + render (fotograma de celebración + bounce).
**Commit:** `feat(motion): bounce de atención + celebración de reset (in-panel)`.

## Tarea 7 · Toggle "reducir movimiento" (gate único)
**Pasos TDD:**
1. Test (`AppConfigTests`): `ReduceMotion` por defecto = valor del helper de SO (mockeable) — o el default acordado (§6 de la spec). Round-trip de serialización.
2. `AppConfig.ReduceMotion` (bool) + helper testeable `MotionPrefs.OsReducedMotion()` (envuelve `SPI_GETCLIENTAREAANIMATION`; fallback false=animaciones on). Default en primer arranque.
3. **Gate único**: `reduceMotion` ⇒ `AnimatedValue.Snap` inmediato (todos), `MotionScheduler.WantsFastTick=false` por config, `MascotAnimator` devuelve frame base sin spinner/jitter, stagger/bounce/fade a estado final. Verificar que **no queda ninguna animación** por una sola comprobación propagada.
4. Fila en `DashboardSettingsView` (toggle) + `DashboardSettingsView.ActionFor` la mapea a `c => c.ReduceMotion = !c.ReduceMotion`. Label "Reducir movimiento" ×8 idiomas.
5. Build + suite + **render-test con `ReduceMotion=true`** ⇒ debe ser **idéntico al estado final** (sin offsets/alfa intermedios).
**Commit:** `feat(a11y): toggle "reducir movimiento" con gate único`.

## Tarea 8 · Verificación final + render-test de motion + fix del sello (sin commit propio aparte del fix)
1. **Render-test ampliado** (`Program.cs`): `PrepareForRender` acepta override de tiempo de motion; generar fotogramas a `tSinceOpen=0/90/200 ms`, hover sobre una fila, mascota en varios frames/humores, fotograma de celebración, y una pasada con `ReduceMotion=true` (== estado final). Guardar PNGs nuevos.
2. **Fix del truncamiento del sello/footer** (pega de F2): en `DashboardForm.LayoutContent`, envolver el `LocalSeal`/hint a 2 líneas o reordenar y **crecer el alto** (medir con `MeasureString` y sumar en ambas ramas) para que no se corte a 340 px. **Commit:** `fix(ui): el sello de privacidad y el hint ya no se truncan`.
3. Build Release-equivalent + suite completa (≥ 103, idealmente +20). 0 warnings nuevos.
4. Reportar al coordinador: nº de tests, lista de commits y los PNGs, para que reinicie la app, taggee `v0.3.4` y mande fotos por Telegram (Bot API si el MCP sigue caído).

## Notas
- **i18n**: todo string nuevo (verbos, "cuota renovada", "reducir movimiento") en los **8 bloques** de `Localization.cs` (EN base + ES + NL/FR/DE/JA/KO/ZH).
- **Tests sin red ni reloj real**: tiempo siempre por `elapsedMs` inyectado; nada de `Math.Random`/`DateTime.UtcNow` dentro del núcleo de motion.
- **Constantes de motion** centralizadas en `Motion.cs` (duraciones/amplitudes); nada de literales sueltos.
- **Mascota = clean-room** (refs Buddi/Notchi son GPL → solo inspiración conceptual; sprites y verbos propios).
- **CPU 24/7**: si en review algo deja el fast-tick corriendo con el panel oculto, es **bug bloqueante**.
- Sin push/merge: tags locales + reinicio. Publicación a GitHub la decide Yovan (línea roja).
