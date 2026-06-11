---
tipo: nota-canonica
estado: semilla
owner: yovan
ultima_actualizacion: 2026-06-11
tags: [claudebar-win, ui-ux, auditoria, v0.3.9, pulido, winforms, gdi]
---

# ClaudeBar for Windows — Auditoría UX post-v0.3.8 → plan v0.3.9 de pulido

> Build auditada: **v0.3.8+T13** (HEAD `0d094a9`, `Version` aún 0.3.7 por la regla "no bump"). Lente: `ui-ux-pro-max`, verificación adversarial contra código y renders del 2026-06-11 12:20.

## Resumen ejecutivo

El sprint **v0.3.8 (T1-T13b)** cerró lo que dolía: contraste WCAG (negro/blanco por luminancia + tokens WarnText/CriticalText), truncamiento/wrap, **DPI central** (`sc(px)` + OnDpiChanged), **i18n de números/fechas**, badge `pend` limpio, gear a path GDI+, y **pricing dinámico** (catálogo models.dev). La build está **funcionalmente sólida**: nada se corta, solapa ni rompe; el panel cabe sin scroll forzado.

Quedan **47 hallazgos de PULIDO**, ninguno bug duro. Concentrados en 3 ejes:

1. **Semántica de color de la QuotaBar** — el relleno se colorea por *pace* (ritmo) y no por %: una barra al **57% sale ROJA** y otra al **84% VERDE** en la misma columna. Lo más contraintuitivo del producto.
2. **Legibilidad de la gráfica** — área apilada en $ donde Opus copa ~90% y aplasta las minoritarias; sin eje Y ni línea de umbral.
3. **Redistribución §4** (mascota a columna derecha, chrome a 1 fila, ajustes 8→6 cabeceras) — fuera del sprint a propósito, hoy la mayor palanca de densidad.

La mayoría son **quick-wins 1-line/small sobre tokens de Theme/DesignSystem**, riesgo cero. La §4 y el multi-res del tray son medium/large que conviene **decidir como producto**.

**No hay P0 ni P1.** Todo es P2 o minor.

---

## P2 — pulido de alto valor (entran en v0.3.9 salvo los marcados "decisión")

| Título | Problema | Fix | Esfuerzo | Reglas skill |
|---|---|---|---|---|
| QuotaBar por pace, no por % | 57% rojo / 84% verde misma columna; color contradice longitud | Relleno por % real (`RiskColor` en QuotaBar.cs:37), ritmo al pace-marker ▾; o sufijo verbal | medium | color-not-decorative-only, contrast-data |
| Tres semánticas de color en Cuota | Verde/rojo = ritmo en barras, %real en mini-filas | Mini-filas a relleno **neutro** (DataView:377) | small | color-semantic |
| Jerarquía tipográfica plana | Cuota = header de sección = % de modelo (todo 12pt) | Token `QuotaValue` Mono14 bold solo para % de barras; header sección a TextSecondary | medium | visual-hierarchy, weight-hierarchy |
| Gráfica: Opus aplasta minoritarias | Stack en $ absolutos, sin lente proporcional | 3er estado del toggle → área **normalizada 100%** | medium | data-density, chart-type |
| Plot sin eje Y / gridlines | Sin escala de magnitudes intermedias | 1-2 gridlines `theme.Separator` antes del fill; en % → Y(100)+umbrales | medium | axis-labels, gridline-subtle |
| Spinner braille = píxeles muertos | 1-3 puntos grises sin forma | Arco GDI+ por elapsedMs + PhaseColor; anclar a y+lineH | small | motion-meaning |
| Mascota Idle ~2.3:1 (default Dark falla) | Gris Neutral bajo 3:1 en 2/3 temas | **1-line**: MascotIdleOverride → TextMuted | 1-line | color-not-decorative-only, icon-contrast |
| Celebración de reset subexpresa | Gato Happy = Processing (verde+o.o); sin bounce | Happy → Accent (1-line) + chip alpha 64 + Bounce en celebración | small | motion-meaning, delight |
| Badge `pend` borra la forma daltónica | Critical+pend solo-color | Forma dentro del punto (FillPolygon) | small | color-not-decorative-only |
| '99+' del tray a 18px | Peor caso de legibilidad, literal sin token | Tokenizar + fit-to-box (cabe a ~22-24px) | small | consistent-icon-sizing |
| Rampa clay de marca (idea #19) — **decisión** | Badge indistinguible de ccusage | Tokens Tray* clay; trade-off afordancia verde | medium | color-palette-from-product |
| Redistribución §4 mascota a columna — **decisión** | ~110px de chrome antes del 1er dato (revierte fix deliberado) | Header 2 columnas; reservar ancho en medir==pintar | medium | content-priority, data-density |
| Ajustes 8→6 + umbral fuera de 'ICONO' — **decisión** | Threshold global enterrado bajo glifo de bandeja | Reordenar Draw() + strings ×8 locales | medium | field-grouping, nav-hierarchy |
| Pulgar de scroll no arrastrable — **decisión** | Afordancia falsa (alpha 160) | Rama de drag con inversa de ThumbRect | medium | gesture-feedback |
| Footer 4 líneas + sello — **decisión** | Sello marketing en cada apertura, jerarquía invertida | Sacar sello a Acerca de; footer 1 línea | medium | whitespace-balance, content-priority |

---

## minor — nits y consistencia

| Título | Problema | Fix | Esfuerzo | Reglas skill |
|---|---|---|---|---|
| Línea pace tiñe todo del peor estado | 7d 77% sano sale rojo | Color por segmento; o línea neutra + ⚠ coloreado | small | color-semantic |
| 'Sonnet 7d 0%' en foreground | El dato más vacío resalta más | % a TextMuted bajo epsilon (1-line) | small | data-density, empty-data-state |
| Línea pace: flecha ↗ fija | ↗ sobre ventana por debajo del ritmo | PaceArrow(status) por segmento | small | color-not-decorative-only |
| Línea pace: separador ETA pobre | 3 espacios + hora sin rótulo | ' · ⚠' + micro-rótulo localizado | small | whitespace-balance |
| Modo % sin línea de umbral | No se ve el cruce warn/crit | Gridlines en cfg.Warn/Crit (patrón QuotaBar.TickX) | small | axis-labels |
| Leyenda sin valor por familia | Solo color↔nombre | Gasto desde `chartData.Cost(family)`, no Spend(7d) | small | direct-labeling |
| Orden leyenda invertido | Mapeo izq→der vs abajo→arriba | `.Reverse()` en la leyenda (trade-off) | 1-line | legend-visible |
| Empty-state pobre | Texto a la izquierda, sin guía | Centrar + 2ª línea de guía + glifo | small | empty-data-state |
| Área % alpha 70 lavada | ~1.4:1 en claro; fix Lerp es no-op | Color fijo más oscuro o borde superior 1px | medium | contrast-data |
| Tabs sin granularidad rotulada | No dice 'cada barra = 1h' | Microcopy de Spec(range) en TextMuted | small | time-scale-clarity |
| Spinner en la oreja | Descolgado de la cara animada | y → y+lineH (1-line) | 1-line | motion-consistency |
| Approval ≡ Input visualmente | Solo cara ~6px difiere | Verbo+notif ya separan; backlog | small | color-not-decorative-only |
| Dos filas 'Activadas' | Sin sujeto fuera de la cabecera | Strings autodescriptivas ×8 locales | small | input-labels |
| Scroll se reinicia a 0 | Reabrir vuelve arriba | Quitar `_settingsScroll = 0` (1-line) | 1-line | state-preservation |
| FRECUENCIA = sección de 1 fila | Cromo de cabecera para 4 chips | Mover a SISTEMA con label **inline** (¡vacío hoy!) | 1-line | field-grouping |
| Apagar Sesiones sin énfasis | Reescribe config global, fila neutra | Subtítulo en WarnText cuando hooks ON (1-line) | small | destructive-emphasis |
| '‹ Ajustes' afordancia de back débil | Lee como título; s.Back muerto | _s.Settings → _s.Back (revive string ×8) | small | back-behavior |
| Badge stale lavado en claro | Silueta ~2:1, no stale-específico | Contorno 1px Separator (no subir alpha) | medium | icon-contrast |
| Badge error '!' rompe gramática | 4º vocabulario, Neutral sin token | Contorno 1px + tokenizar TrayError | small | icon-style-consistent |
| Tray icon 48px único | Downscale del OS difumina 99+/forma | Render al pixel efectivo (SmallIconSize) | large | vector-only-assets |
| Forma daltónica 4.6px@16 | ▲ vs ◆ = mismo blob | s=18-20 + knockout (dígito ya legible) | medium | icon-contrast |
| Espaciado header fuera de 8pt | Literales 50/22/14/6 | Tokenizar como ya hace SettingsView | medium | spacing-scale |
| Doble banda de chrome | Salud aislada con Cuota expandida | Dot de salud en el título | medium | whitespace-balance |
| Glance solo con Cuota plegada | Header semivacío expandido | Glance en el título de sección | medium | visual-hierarchy |
| % pace sin cultura + proporcional | Bypass de helpers (impacto ~nulo) | `ToString('0', s.Culture)`; deuda | small | number-tabular |
| Sello de privacidad largo | Wrappea siempre a 2 líneas | Condensar 1 línea + Acerca de | small | line-length |
| 'hace 0 s' poco humano | Baja credibilidad en refresco | s.JustNow bajo 5s (×8 idiomas) | small | whitespace-balance |
| Orden de secciones | Opinión en disputa (2 docs) | Promoción condicional de Sesiones | small | content-priority |

---

## Sprint recomendado v0.3.9 (pulido UX, POST-release v0.3.8)

> Quick-wins de alto ROI sobre Theme/DesignSystem + los medium que mueven la aguja. Todo GDI+ nativo, cero recetas web, invariante medir==pintar respetado.

1. **MascotIdle → TextMuted** (1-line; hero de 2.3:1 → ~5:1 en los 3 temas).
2. **Spinner**: arco GDI+ con color de fase + anclar a la fila de la cara (funde los 2 hallazgos del spinner).
3. **Celebración**: Happy → Accent (1-line) + chip alpha 64 + Bounce en `CelebrationActive()`.
4. **QuotaBar**: relleno por % real (o sufijo verbal) — el mayor fix de claridad del producto.
5. **Mini-filas de modelo a relleno neutro** (DataView:377).
6. **Línea de pace**: flecha por segmento + color por ventana + separador ' · ⚠' (funde 3 minors).
7. **Fila de modelo 0%**: % a TextMuted bajo epsilon (1-line).
8. **Badge `pend`**: forma dentro del punto (recupera redundancia no-cromática en Critical).
9. **'99+'**: tokenizar font + fit-to-box.
10. **Scroll de ajustes**: no resetear (1-line).
11. **Back '‹ Volver'** (revive s.Back).
12. **Tokenizar espaciado del header** (replicar SettingsView).
13. **Gridline de umbral en modo %** (patrón QuotaBar.TickX).

Coste: ~7 de 1-line/small + ~3 medium acotados. Riesgo de regresión: bajo (cambios de color/token/string; sin tocar el layout de la banda ni el pipeline del icono).

---

## Diferir a roadmap (grande o decisión de producto)

- **Redistribución §4 completa** (mascota a columna + chrome 1 fila + footer 1 línea): revierte parcialmente un fix deliberado (la banda evitaba cortar la salud); reservar ancho en ambas pasadas. Decisión de Yovan.
- **Ajustes 8→6 cabeceras + umbral de color a 'CUOTA Y UMBRALES'**: strings ×8 locales; decisión de IA del panel.
- **Modo % normalizado** y **eje Y/gridlines en $** de la gráfica: enhancements de data-viz.
- **Gradiente clay de marca (idea #19)**: trade-off afordancia 'verde=seguro'. Decisión de producto.
- **Drag del pulgar de scroll**: mitigado por rueda/trackpad.
- **Multi-res del tray icon** (large): falla solo en estados de borde.
- **Forma daltónica más grande/diferenciable**: redundante sobre el dígito.
- **Sello a Acerca de + condensar** (ligado a footer); **doble banda de chrome / glance en título** (componen con §4).
- **Approval vs Input por forma/bounce**; **énfasis destructivo en la fila** (la confirmación ya protege).
- **'just now' / 'ahora mismo'** (microcopy ×8); **orden de secciones** (en disputa).

> Nota de alcance: NO meter en el sprint cosas de **F4** (notis time-aware, onboarding, logging, CI, tests del pipeline) ni **F5** (modo mini, heatmap, desglose diario, Wrapped PNG).

---

## 🔗 Relacionado

- [[2026-06-10-auditoria-completa-ui-mercado]] — informe base (§4 redistribución, §6 ideas adoptables, idea #19 clay).
- [[2026-06-10-v038-audit-sprint]] — plan del sprint cerrado (T1-T13b).
- [[2026-06-01-rediseno-dashboard-panel-design]] — origen del orden de secciones y de la banda de mascota (fix deliberado).
- [[proyecto_claudebar_win]] — nota maestra del proyecto.
