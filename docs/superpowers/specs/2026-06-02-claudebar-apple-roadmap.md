# ClaudeBar-win — Roadmap "más profesional / tipo Apple"

**Fecha:** 2026-06-02
**Estado:** roadmap aprobado (alcance: TODO, 5 fases). Empezamos por Fase 1.
**Origen:** `/brainstorming` pedido por Yovan + investigación multi-agente (12 subagentes: 8 apps de inspiración + foros + competidores + Apple HIG).

> Este documento es el **paraguas**: preserva la investigación y la descomposición. Cada fase tiene su propio spec detallado + plan + ejecución.

---

## 1. Objetivo

Subir ClaudeBar-win de "utilidad completa pero casera" a **producto profesional, intuitivo y "tipo Apple"**, sin perder lo que ya hace bien. Target: el usuario Claude **Max** frustrado que esperaba algo "oficial" — la barra de calidad la pone él.

## 2. Conclusión de la investigación

ClaudeBar-win ya tiene **más features que casi todos los referentes** (cuota real 5h/7d, pace/ETA, gráfica apilada, gasto $, histórico SQLite, salud, 9 idiomas, temas, auto-update, mascota por hook). El salto "tipo Apple" **NO está en añadir features**, sino en tres palancas + tres verdades de comunidad.

### Las 3 palancas
1. **Sistema visual coherente** — hoy los colores van hardcodeados por draw-call y los offsets son arbitrarios (14/16/22 px). Faltan tokens semánticos, rejilla 8 pt, dígitos tabulares, jerarquía por peso/color en vez de cromo.
2. **Microinteracciones** — la app solo repinta 1×/seg (frame de la mascota): números, barras y apertura del panel **saltan en seco**. Falta un motor de easing (tweens, hover, apertura suave).
3. **Datos → señales de un vistazo** — el pace ya se calcula pero va en texto; dibujarlo **dentro de la barra** + color por riesgo interpolado + reset en hora local absoluta. Es el lenguaje que CodexBar/ClaudeBar-mac/ccstatusline usan para verse premium.

### Las 3 verdades de los foros
- **Reset rodante = queja nº1** de toda la comunidad → explicarlo en humano ("se renueva a las 18:42 · 5h rodante desde tu 1ª petición") la elimina.
- **Confianza** = marcar datos viejos (stale) + declarar **"100% local, no envío nada"**.
- **Notis por hitos fijos = spam** → time-aware + cooldown + noti **positiva** de "cuota renovada".

### Huecos de producto (primera impresión)
- No hay **onboarding / empty-state / first-run**.
- No hay **modo mini/compacto** (glance value sin abrir el panel).

## 3. Restricción técnica transversal

Todo el render es **GDI+ a mano** (WinForms, `Graphics.DrawString/FillPath`), **no XAML/WPF/WinUI**. No hay binding declarativo. Cada animación/microinteracción se dibuja con un `Timer`. Las propuestas están filtradas por factibilidad en GDI+. **Cada animación debe tener su rama "off"** (toggle "reducir movimiento", obligatorio para la afirmación "tipo Apple").

## 4. Descomposición en 5 fases (por dependencia)

Cada fase = spec → plan → ejecución con subagentes → **build + 64 tests verdes + `--render-test`** antes de pasar a la siguiente.

### Fase 1 · Cimientos visuales  *(todo lo demás hereda de aquí)*
Tokens semánticos derivados de **1 acento (#CC785C, naranja Claude)** + 2 niveles de elevación en dark · rejilla 8 pt + escala tipográfica (Segoe UI Variable) + dígitos monoespaciados · color interpolado por riesgo (`RiskColor`).
→ Spec: `2026-06-02-claudebar-f1-cimientos-visuales-design.md`.

### Fase 2 · Señales de un vistazo
Pace marker dentro de la barra + ticks de umbral · reset en hora local absoluta + explicación del rolling · barra segmentada estilo batería (opción) · estado "stale" + sello 100% local · tray icon que se adapta a barra clara/oscura + estado por forma (daltónicos) · número "headroom" invertido (opción) · pestaña de rango activa con pill.

### Fase 3 · Microinteracciones  *(motor de easing)*
Tween de números/barras (ease-out ~200 ms) · hover states en filas/botones · apertura del panel como superficie de sistema (fade ≤120 ms + dismiss por foco/Esc) · entrada escalonada de secciones · vida de la mascota (tempos por fase, blink con jitter, idle peek, spinner de glifos, verbos con personalidad, emociones con histéresis/decay) · bounce de atención + celebración de reset · toggle **"reducir movimiento"**.

### Fase 4 · Confianza + notis + onboarding
Notis time-aware + cooldown + arranque silencioso + "cuota renovada" (silenciables individualmente) · empty-state/first-run diseñado + boot animation + microcopy contextual vivo · DPI PerMonitorV2 + `sc(px)` · backoff exponencial + watch credenciales al fallar auth.

### Fase 5 · Features nuevas + profesionalización
Modo mini/compacto · heatmap de actividad (sobre SQLite) · desglose por proyecto + cache hit rate prominente · velocidad de tokens en vivo (tok/s + sparkline) · gasto humanizado ("= N cafés") · export de tarjeta-resumen PNG (Wrapped anual) · hotkey global · consolidación multi-máquina (carpeta sync) · auditoría anti-emoji README/UI + iconografía de un set · salud del servicio como punto discreto solo si hay incidencia.

## 5. Catálogo completo de mejoras (33, por categoría)

> Esfuerzo S/M/L · Impacto low/med/high · Factibilidad GDI+ easy/medium/hard.

### Lenguaje visual / Sistema de diseño
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Tokens semánticos + elevación dark | M | high | easy | Apple HIG, Notch-Pilot, vibe-notch |
| Rejilla 8pt + escala tipográfica | M | high | easy | Apple HIG, iStat Menus 7 |
| Dígitos tabulares | S | med | easy | Apple HIG, CodexBar, Notch-Pilot |
| Barra segmentada estilo batería | S | med | easy | CodeZeno Usage Monitor |
| Color interpolado por riesgo (lerp) | S | med | easy | CodeZeno Usage Monitor |
| Glass card (top-stroke + orbs) | M | med | medium | ClaudeBar-mac, Notchi |

### Jerarquía / Señales de datos
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Pace marker dentro de la barra | S | high | easy | CodexBar, ClaudeBar-mac, ccstatusline |
| Ticks de umbral en la barra | S | med | easy | CodexBar |
| Número "headroom" invertido (icono) | S | med | easy | rjwalters/claude-monitor |
| Reset hora local absoluta + rolling | S | high | easy | foros (jtbr/usagebar), SessionWatcher |
| Pestaña de rango activa con pill | S | low | easy | NN/g, Raycast |
| Estado/forma además de color (a11y) | S | med | easy | Apple HIG, NN/g |

### Microinteracciones
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Tween números/barras (ease-out ~200ms) | M | high | easy | ClaudeBar-mac (numericText), Buddi, Notchi |
| Hover states sutiles en todo | M | high | easy | vibe-notch, Buddi, CleanShot/Raycast |
| Entrada escalonada de secciones | M | med | medium | ClaudeBar-mac, vibe-notch |
| Apertura del panel como superficie sistema | M | high | medium | Apple HIG, Notch-Pilot, Vibe Notch |
| Vida idle de la mascota (blink/tempos) | S | med | easy | Notch-Pilot, Buddi, Notchi |
| Spinner de proceso por ciclo de glifos | S | med | easy | Vibe Notch, Buddi, Notchi |
| Verbos de estado con personalidad | S | low | easy | Notchi (200+ verbos) |
| Reactividad emocional (histéresis/decay) | M | med | medium | Notchi |
| Bounce atención + celebración reset | M | med | medium | vibe-notch, CodexBar, Notchi |

### Onboarding / first-run
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Empty-state / first-run diseñado | M | high | easy | Apple HIG/NN/g, Vibe Notch |
| Boot animation + presentación de marca | M | med | medium | Vibe Notch/Notchi, Buddi |
| Microcopy contextual vivo (onboarding pasivo) | S | med | easy | ccstatusline |

### Confianza / robustez
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Estado "stale" + sello privacidad/local | S | high | easy | foros (jtbr, jens-duttke), SessionWatcher |
| Notis time-aware + cooldown + "renovada" | M | high | easy | jens-duttke, ccseva, Apple HIG |
| DPI PerMonitorV2 + sc(px) | M | med | medium | CodeZeno Usage Monitor |
| Tray icon adaptativo barra clara/oscura | S | med | medium | Apple HIG (template image) |
| Backoff exponencial + watch creds | M | low | medium | CodeZeno Usage Monitor |

### Features nuevas
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Modo mini/compacto siempre-visible | M | high | medium | CodeZeno (46px), iStat Menus |
| Heatmap de actividad (SQLite) | M | med | medium | Notch-Pilot, soulduse/ai-token-monitor |
| Desglose por proyecto + cache hit rate | M | med | medium | CodeBurn, ClaudeBar-mac |
| Velocidad de tokens en vivo (tok/s + sparkline) | L | med | hard | ccstatusline, foros (issue #33978) |
| Gasto humanizado ("= N cafés") | S | low | easy | soulduse/ai-token-monitor |
| Consolidación multi-máquina (carpeta sync) | L | med | hard | SessionWatcher, ccusage #222 |

### Profesionalización / distribución
| Mejora | E | I | Fact | Inspiración |
|---|---|---|---|---|
| Hotkey global mostrar/ocultar | S | med | medium | Raycast |
| Export tarjeta-resumen PNG (Wrapped) | M | med | medium | soulduse/ai-token-monitor |
| Auditoría anti-emoji README/UI + 1 set iconos | M | med | easy | foros (SessionWatcher), Apple HIG |
| Salud como punto discreto solo si incidencia | S | low | easy | ClaudeUsageBar, CodexBar |
| Toggle "reducir movimiento" | S | low | easy | Apple HIG, Notchi |

## 6. Quick-wins (bajo esfuerzo, alto impacto — repartidos por las fases)

Pace marker · ticks de umbral · dígitos tabulares · reset hora absoluta · color por riesgo · estados de carga/error de 1 carácter · stale "· hace N min" · línea "lee solo .jsonl locales" · hover highlight · tray icon adaptativo · pestaña activa con pill · verbos de personalidad · noti "cuota renovada" · snap del panel a bordes.

## 7. Apps de referencia analizadas

CodexBar (steipete) · ClaudeBar macOS (tddworks) · Notch-Pilot · notchi · vibe-notch · Buddi · ccstatusline · Claude-Code-Usage-Monitor (CodeZeno). Descomprimidas en `C:/Users/zorro/Asistente/.tmp/cb-inspiracion/`; zips en `C:/Users/zorro/OneDrive/escritorio/claudebar_inspiracion/`.

## 8. Notas de release

El redesign es una **v0.4** (multi-fase). **No bloquea** el merge/publicación de la v0.3 actual (sesiones en vivo + rediseño dashboard, ya en `feat/live-sessions`). Decidir con Yovan si v0.4 sale de `feat/live-sessions` ya mergeada o de una rama nueva `feat/apple-redesign`.

## 🔗 Relacionado
- `2026-06-02-claudebar-f1-cimientos-visuales-design.md` (spec Fase 1)
- `2026-06-01-rediseno-dashboard-panel-design.md` (v0.3)
- `2026-06-01-sesiones-en-vivo-mascota-avisos-design.md` (v0.3)
