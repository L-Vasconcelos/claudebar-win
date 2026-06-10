# ClaudeBar-win — Auditoría completa: UI/UX, inspiraciones, mercado y monetización

**Fecha:** 2026-06-10 · **Versión auditada:** v0.3.7 (378 tests)
**Método:** workflow de 28 subagentes — 3 auditorías (código UI / visual sobre renders / arquitectura), minería del vault+roadmap, 8 análisis de apps de inspiración (`.tmp/claudebar-inspiracion/`), 4 investigaciones web (competidores, foros, monetización, distribución). La pasada de verificación adversarial de claims cayó por límite de sesión → los claims de mercado llevan la confianza del researcher (mayoría "alta", con fuentes directas gh api / HN Algolia / webs oficiales).
**Datos crudos:** `C:/Users/zorro/Asistente/.tmp/claudebar-audit/{audits,plans,inspiraciones,mercado,claims}.json`

---

## 1. Veredicto en una línea

El producto es **más completo que casi todos sus rivales** y el código está inusualmente cuidado (tokens, tests de UI, motor de motion 0-CPU); los problemas reales son **2 P0 técnicos**, **~10 defectos visuales concretos** (cortes/bordes/contraste) y, sobre todo, **distribución**: 0 stars y ~9 descargas. El cuello de botella no es el producto.

---

## 2. P0 (arreglar antes de cualquier otra cosa)

1. **DPI: PerMonitorV2 está activo pero TODO el layout es px fijos.** Las fuentes escalan con el DPI y la geometría no: al 125/150% (portátiles) hay solapes verticales, % pisando barras, footer recortado y panel de 340px enano. Falta `sc(px)` centralizado en `Spacing`/`DesignSystem` + `OnDpiChangedAfterParent→Relayout()`. Bonus: `GearIconFont` está en `GraphicsUnit.Pixel` (no escala). → `DashboardForm.cs:312`, `ClaudeBarWin.csproj:14`.
2. **`TryOAuthRefreshAsync` reescribe `~/.claude/.credentials.json` sin escritura atómica ni lock** — crash a mitad = JSON corrupto = logout de Claude Code; carrera real con las sesiones 24/7 de este PC. Fix mínimo: `.tmp` + `File.Replace` + revalidar antes de escribir; ideal: token refrescado solo en memoria. → `UsageApiClient.cs:153-199`.

## 3. Cortes, bordes y defectos visuales (verificados sobre renders)

| # | Defecto | Dónde |
|---|---|---|
| 1 | Primera etiqueta del eje X con 2 textos superpuestos ("lun 1{0/2}h"); "mar 18h"/"mié 04h" pegadas — falta lógica de colisión de labels | `dashboard-full.png`, chart |
| 2 | La barra de "Sonnet 7d" **tacha el porcentaje** (cruza el "12%"/"0%") — acortar el track antes del texto como en Session/Week | QuotaBar fila compacta |
| 3 | **Mezcla de locales en la UI inglesa**: `$420,50`, "resets in 1h 39m · jue 02:12" — usa CurrentCulture del SO en vez de la cultura del idioma elegido. Es lo que ve el público del README | Spend + resets, Language=en |
| 4 | Ticks de umbral de QuotaBar invisibles: `Separator`≈`Track` en LOS TRES temas (CLI: idéntico) | `QuotaBar.cs:71-77` |
| 5 | Tabs de rango 1H/5H/24H/7D/30D sin borde de chip inactivo → invisibles en tema claro + geometría distinta a los chips vecinos (alto 20 vs 18, radio 5 vs 4) | `DashboardDataView.cs:378-393` |
| 6 | Hover con `BgElevated` puro → invisible en claro (1.03:1) y CLI; además sangra ~2px sobre el chrome en ajustes (sin clip del viewport) | `DashboardForm.cs:962` |
| 7 | `ColorMath.Contrast` (luma 140) elige blanco sobre el verde CLI → **1.9:1** en chips, knob y % del tray; Warn claro 2.8:1; Critical oscuro 3.7:1 (el texto más crítico del panel es el de peor contraste) | `DesignSystem.cs:43`, `Theme.cs` |
| 8 | Chips de umbral 25/50/75/95% en ajustes sin estado on/off diferenciado (los 4 idénticos) | `settings.png` |
| 9 | "peak $41,81" colisiona con el marcador de pico cuando el pico cae en el último bucket (caso común) | chart header |
| 10 | Footer ES rompe dejando línea que EMPIEZA por "· sin telemetría" — tratar " · " como punto de ruptura preferente | FooterLayout |
| 11 | Knob del toggle en negro puro = "agujero perforado"; estándar = knob blanco con sombra | TogglePill |
| 12 | Engranaje ⚙ = glifo ClearType borroso con franjas de color (peor en claro); dibujarlo como path GDI+ | `DashboardHeader` |
| 13 | `ToggleRow` (≈12 filas de ajustes) no envuelve ni elide → en DE/FR/NL el texto pasa bajo el pill; ídem `SegmentedRow`/`MultiSegmentRow` | `DashboardSettingsView.cs:649` |
| 14 | Sesiones en vivo: nombre de proyecto largo atraviesa la etiqueta de fase (sin Ellipsize) | `DashboardDataView.cs:335` |
| 15 | Badge "pend" del tray: número+punto+forma solapados en 32px; punto tapa el dígito; color oliva fuera de paleta | `TrayIconRenderer` |
| 16 | Right-align con `MeasureString` sin `GenericTypographic` → columna derecha "dentada" (3-6px); mezcla de `(int)` y `Ceiling` | QuotaBar/DataView |
| 17 | Mascota: el indicador `∴` parece píxeles muertos; el glifo necesita más intención | `mascot.png` |
| 18 | `PlaceWindow` siempre usa `Screen.PrimaryScreen` (panel en pantalla equivocada en multi-monitor); ajustes usa `FromControl` y clamp usa `FromPoint` — unificar | `DashboardForm.cs:666` |

**Duplicado/jerarquía:** "Week (7d) · 84%" sale dos veces (header colapsable + sección Quota); y el headline elige la ventana en verde (84%) cuando la sesión está al 62% con pace crítico — **el headline debe elegir la ventana con peor estado**, no la de mayor %.

## 4. Redistribución propuesta

**Dashboard** (hoy gasta ~110px antes del primer dato accionable):
1. Fila única de chrome: punto de salud + "ClaudeBar · Max 20x" izquierda, ⚙ ✕ derecha (ahorra una banda).
2. Mascota a la DERECHA del bloque de estado (columna ~70px), no en banda a ancho completo.
3. Glance fusionado con el título de sección ("▾ Cuota · ◆ 87%") u oculto cuando Cuota está expandida.
4. Orden: **Cuota → Gráfica → Sesiones → Gasto**; Sesiones sube arriba SOLO con `NeedsAttention()`.
5. Sello de privacidad → "Acerca de" o icono 🔒 con tooltip; footer en 1 línea; línea stale fundida con "Actualizado".

**Ajustes** (8 cabeceras → 6):
1. APARIENCIA (tema/posición/opacidad/fijado/on-top/reduce-motion + los 3 toggles de "Mostrar" como "Secciones")
2. CUOTA Y UMBRALES (umbral warn/crit — hoy engañosamente bajo "Icono" — + modo del icono)
3. NOTIFICACIONES · 4. SESIONES EN VIVO · 5. SISTEMA (arranque, idioma, **frecuencia**) · 6. ACERCA DE.
Bonus: pulgar del scroll arrastrable (hoy solo rueda) + persistir scroll en la sesión del panel.

## 5. Arquitectura (lo que frena crecer)

- **P1** `TranscriptParser` relee TODOS los .jsonl de 7 días cada 60s desde byte 0 → mayor consumidor de CPU/I/O en reposo. Cache incremental por archivo (mtime+offset, append-only) o computar solo con panel visible.
- **P1** `claude -p .` como fallback de refresh: gasta cuota, dispara los hooks del usuario (¡incluido claudebar-hook → eventos fantasma!), hereda cwd arbitrario. Eliminar o aislar (cwd temp + settings sin hooks).
- **P1** Si Anthropic cambia el shape de `/api/oauth/usage`, la app muestra 0% verde (miente). Añadir `SchemaMismatch` + conservar last-good + test de contrato con JSON grabado.
- **P1** 0 tests del pipeline de datos (UsageApiClient.Parse, TranscriptParser, UsageHistoryStore, Pricing…) — los 378 tests son de motion/layout. Y **no hay CI** (.github ausente).
- **P1** El dominio hardcodea 5h/7d/Opus/Sonnet de punta a punta (15 archivos por proveedor nuevo). Refactor `IUsageProvider` + `QuotaWindow(id,label,pct,resetsAt)` + migración SQLite a `(provider,window_id,ts,pct)` ANTES de multi-proveedor. El extra_usage cae natural ahí.
- **P1** 40+ `catch {}` silenciosos y cero logging → indiagnosticable en campo. Logger mínimo con rotación (~30 líneas).
- **P2** Balloon de sesiones desde hilo del pipe (no-UI) — envolver en `BeginInvoke`. Pipe server atiende 1 conexión → eventos concurrentes se pierden. PowerShell por CADA evento de hook (~150-400ms en cada tool-call del usuario) → exe AOT o recortar eventos.
- **P2** Routing stringly-typed (`'toggle:ShowSpend'`) en 3 archivos; Program.cs = 700 líneas de harness de demos dentro del exe de producción.

**Fortalezas confirmadas:** invariante medir==pintar con tests, motor de motion 0-CPU en reposo, modelo de fallos del endpoint (states+Retry-After+last-good), hooks idempotentes con backup, update Ed25519 Strict, harness de render determinista.

## 6. Lluvia de ideas — conceptos adoptables (de las 8 inspiraciones)

**Tier 1 — diferenciadores:**
1. **Approve/Deny de permisos desde el tray** (Notch-Pilot **MIT** = referencia copiable legalmente; hook PermissionRequest bloqueante → Named Pipe → UI; render por tipo de tool: diff para Edit, code block para Bash; "Always allow" escribe en permissions.allow; cola FIFO "1 of N"; dismissStalePermissions). Extensión natural para Yovan: reenviar a Telegram. La v2 "aprueba" ya estaba decidida en el brainstorm de v0.3.
2. **Multi-proveedor**: `ProviderDescriptor` como fuente única (CodexBar: UI sin branching por proveedor), pipeline de fetch con fallback ordenado y outcome-con-intentos; Codex YA resuelto en CodeZeno MIT (`$CODEX_HOME/auth.json`, `chatgpt.com/backend-api/wham/usage`, refresh `codex exec .`). "CodexBar para Windows" = EL hueco de mercado.
3. **Widget embebido en la taskbar** (CodeZeno MIT, `window.rs` documenta todos los trucos Win32: reparent en Shell_TrayWnd, alpha=1 para ClearType clicable, WinEvent hook de reposicionamiento) = el "modo mini" de F5 con esteroides.
4. **Daily usage cards con delta vs ayer + ahorro de caché** (ClaudeBar-mac: "Vs Mar 10 −$27.47 (4.9%)", "Saved $X by cache") — el desglose diario del backlog casi gratis sobre el SQLite existente.
5. **Extra usage en $**: shape exacto del JSON confirmado por 3 apps (`extra_usage{is_enabled,monthly_limit,used_credits,utilization}`) + badge rojo "quemando dinero" + ventanas por modelo (`seven_day_sonnet/opus`).

**Tier 2 — confianza/robustez (F4):**
6. Notis focus-aware: no sonar si el terminal tiene foco (lista de procesos: WindowsTerminal/wt/Code/cursor…), seeding inicial anti-spam, ventana de "listo" de 30s autoextinguible.
7. **Detección de `claude -p` no-interactivo** (Notchi) — crítico aquí: los crons del Asistente no deben disparar notis ni contaminar sesiones.
8. Watch de credenciales en fallo de auth (CodeZeno): pausa + firma del archivo + recuperación instantánea al re-login. Fast-poll 5s tras el reset de ventana + timer al borde del bucket del countdown.
9. Cache+lock+stale del endpoint (ccstatusline: 180s cache, lock inter-proceso, backoff 300s, stale antes que error) y fallback a headers `anthropic-ratelimit-unified-*` del Messages API (CodeZeno) si oauth/usage muere.
10. **Dedup de entradas JSONL en streaming** (ccstatusline lo arregló: Claude Code escribe duplicados al streamear) — posible bug de sobreconteo HOY en el spend en vivo.
11. Settings versionados con migraciones (evita resets de config al actualizar).

**Tier 3 — personalidad/polish:**
12. Identidad gacha determinista de la mascota (Buddi, clean-room: hash del accountUuid → especie/rareza/ojos/sombrero; el roll original nunca se muta) — apego + compartible.
13. Mascota reactiva a ToolAction (Notch-Pilot): Focused/Active/Curious/**Shocked en rojo ante `rm -rf`/DROP TABLE** — señal de seguridad glanceable única.
14. Jump-to-terminal al clicar sesión (cadena de procesos padre → SetForegroundWindow).
15. Pace verbal de CodexBar: "Runs out in 3h" / "Lasts until reset" (+ regla del 3% para no juzgar sin datos).
16. Heatmap 24h con navegación de días + breakdown por proyecto en hover con fila de altura fija (Notch-Pilot = spec probada del heatmap de F5).
17. Temas con nombre (Catppuccin/Nord/Dracula/Gruvbox) como presets sobre los tokens de F1.
18. Confeti/celebración en el reset semanal (CodexBar) — encaja con el gato.
19. Gradiente lerp del badge del tray con paradas en la paleta Claude clay (#D97757→#B82020).
20. CLI companion `claudebar.exe usage --json` — terceros construyen encima (módulos statusline, scripts, el propio Asistente).
21. Formateador de nombres MCP (`mcp__deepwiki__ask_question` → "Deepwiki · Ask Question").
22. Account card (quién está logueado + tier, de `~/.claude.json`) + badge de permission-mode por sesión (Bypass en rojo = seguridad).
23. MorphingText (blur-out→swap→blur-in) para labels que cambian; detección de canal portable-vs-winget en el updater.

## 7. Lo que ya estaba planeado (vault/roadmap) — 28 ítems

- **F4 entera** (6): notis time-aware+cooldown+"cuota renovada", empty-state/first-run, boot animation, microcopy vivo, DPI+sc(px), backoff+watch creds.
- **F5 entera** (11): modo mini, heatmap, desglose por proyecto+cache rate, tok/s, gasto humanizado, Wrapped PNG, hotkey, multi-máquina, anti-emoji+iconos, salud discreta, barra batería (diferida de F2).
- **Caídas sin reasignar** (4): glass card, headroom invertido, pestaña-pill (verificar si ya cubierta), snap del panel a bordes.
- **Backlog pre-roadmap** (3): desglose diario, multi-proveedor, extra_usage $.
- **Operativa**: winget como installer (ver §9 — el PR portable #380749 SÍ se mergeó el 2-jun), sesiones v2 aprobar/denegar, Yovan debe guardar la pass del backup 7z de la clave Ed25519.
- Descartes documentados (no reabrir sin motivo): Telegram en la app, gacha-con-assets GPL, mascota grande, pestañas/acordeón en ajustes, claim "100% local" engañoso, MVP por coste estimado, reduce-motion default ON.

## 8. Mercado (jun-2026)

- **Nicho grande en atención, comoditizado en precio**: top-5 suma ~50k★ (ccusage 15.9k, CodexBar 14.5k, ccstatusline 10.5k, Maciek 8.2k, CCometixLine 3.1k); 26+ companion apps; >95% gratis/MIT.
- **SÍ hay apps de pago — 2, ambas macOS**: SessionWatcher ($2.99 solo-Claude / $7.99 bundle / Pro $2/mes) y CUStats ($9.99 Mac App Store). Banda validada: **$3-10 one-time**. **Cero apps de pago en Windows.**
- **Windows fragmentado sin ganador**: CodeZeno 189★, jens-duttke 107★, sr-kai 19★ + long tail. Ningún multi-proveedor real. El listón para ser nº1 Windows: ~110-190★.
- **Dolores de foros**: (1) opacidad/ansiedad de límites (hilo seminal HN 609 pts/705 comentarios); (2) crisis de drain mar-2026 (Max $200 bloqueados en <20 min; "we're being gaslighted") → el monitor como "trust but verify"; (3) estimación local ≠ realidad (issues de ccusage #298/#569) → **el % oficial del endpoint es lo que la gente quiere** (ClaudeBar ya lo hace, ponerlo en el titular).
- **Features más pedidas**: % oficial 5h Y semanal + reset timers, notis por umbral, burn rate/proyección, color-coding del icono, multi-cuenta, multi-proveedor, cero consumo de tokens, privacidad local. ClaudeBar cubre ~7 de 9.
- **Sherlocking**: Claude Code ya trae /usage, statusline, OTel, analytics Team/Enterprise. El valor de terceros queda en: histórico, atribución por proyecto, predicción, alertas, vista siempre-visible, multi-proveedor.
- **Timing**: el +50% de límites semanales expira el **13-jul-2026** → ola de quejas previsible = ventana de lanzamiento.

## 9. Estado real del repo (corrige el vault)

0 stars, 0 forks, ~9 descargas de instaladores (48 del exe portable v0.1.0), **sin topics de GitHub, sin homepage**, sin GIF hero en el README… y el **PR winget #380749 SE MERGEÓ el 2026-06-02** (`winget install Yovancas.ClaudeBarWin` funciona) — el vault lo daba por OPEN. Sigue pendiente: actualizar el manifest a tipo installer + automatizar bumps con wingetcreate.

## 10. Monetización — ¿se puede? ¿conviene?

**Se puede, pero todavía no.** Con ~9 descargas, monetizar es optimizar el 0. Riesgos específicos documentados:
- **ToS**: enforcement real de Anthropic 2026 (bloqueo OAuth ene, TOS feb: "tokens OAuth en cualquier otro producto… not permitted", revocación abr, C&D a OpenCode) — todo contra harnesses de INFERENCIA; cero acciones contra monitores read-only (tolerados de facto: ccusage, Notchi, CodeZeno). Lo más expuesto de ClaudeBar: el **refresh de token con el client_id oficial** (impersonación), más que el GET de cuota.
- **Marca**: CLAUDE = marca USPTO de Anthropic (ene-2025). App DE PAGO llamada "ClaudeBar" en una store = blanco fácil de takedown. Si se cobra → renombrar.
- **Regla de oro**: lo cobrado NUNCA debe depender del endpoint OAuth no público. El dinero se apoya en lo local (.jsonl, históricos, reports, multi-proveedor); el endpoint queda como extra gratuito con degradación elegante.
- **Antes de cobrar**: firma del instalador (SmartScreen), custodia de la clave Ed25519 (¡relevante con Phoenix/formateo!), logging para soporte, CI.

**Modelos viables (orden):**
- **A. No monetizar aún + lanzar** (recomendada).
- **B. GitHub Sponsors/BuyMeACoffee ya** — coste cero, ingresos ~cero, no bloquea nada.
- **C. Modelo Maccy** (mejor encaje a medio plazo): GitHub sigue MIT y gratis; build de conveniencia firmado **6,99-9,99$ en Microsoft Store** (registro individual GRATIS desde sep-2025; MSIX = Microsoft re-firma → resuelve SmartScreen sin pagar certificado; 100% revenue con pasarela propia o 15% con la de MS). Requiere renombrar.
- **D. Open-core "Pro"** más adelante: core MIT; Pro 10-19$ one-time vía Lemon Squeezy/Paddle (MoR = gestionan IVA español) con multi-proveedor/equipos/históricos avanzados. Único copyright holder (0 contributors) → puede relicenciar futuro sin CLA.
- **E. Sponsorware**: descartable sin audiencia.

## 11. Playbook de lanzamiento (de la investigación de distribución)

**Fase 0 (1 día):** topics de GitHub (hoy CERO: claude, claude-code, claude-usage, windows, system-tray…), GIF hero 10-15s arriba del README, homepage en el repo, descripción = keyword exacta ("Claude usage monitor for Windows (system tray)"), titular diferencial ("tu cuota REAL 5h/7d del mismo endpoint que usa Claude Code + predicción"), sembrar 20-50 stars de red propia.
**Fase 1 (48h, martes-jueves):** r/ClaudeAI (889k miembros) con GIF + cross-post r/ClaudeCode; **Show HN 12-17 UTC** (media +121★/24h si engancha; comparable macOS: 161 pts → 328★) con primer comentario del autor; X + DEV.to + listas awesome (hesreallyhim vía su sistema de issues, NO PR).
**Fase 2:** automatizar bump winget en CI; valorar Microsoft Store MSIX (línea roja: cuenta Partner Center = decisión de Yovan).
**Fase 3:** loop "Share your screenshot" + export de tarjeta PNG (F5) + responder TODO comentario en horas.
**Expectativa honesta:** base 50-150★ (nº1 de Windows), bueno 300-500★. Lanzar ANTES del 13-jul.

## 12. Plan recomendado (síntesis)

1. **Sprint "escaparate + verdad"** (≈1 semana): los 2 P0 + cortes/bordes §3 (1-2 días de fixes quirúrgicos, muchos son de 1 línea de token) + i18n de formatos + headline por peor estado + quitar duplicado.
2. **Lanzamiento** (Fase 0+1 del playbook) — antes del 13-jul.
3. **v0.4 = F4** (DPI sc(px), notis focus-aware, onboarding, watch creds, logging, CI, tests del pipeline de datos).
4. **v0.5 = diferenciación**: refactor IUsageProvider → Codex como 2º proveedor + permisos Approve/Deny (sesiones v2). Relanzar ("the CodexBar for Windows").
5. **Monetización**: solo si 2-4 traen usuarios; modelo Maccy (Store) o open-core Pro, con renombrado y regla de oro.

## 🔗 Relacionado
- `2026-06-02-claudebar-apple-roadmap.md` (33 mejoras, F1-F5)
- `vault/proyectos/ClaudeBar for Windows.md`
