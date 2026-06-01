# Diseño — Sesiones en vivo: mascota + avisos de estado (ClaudeBarWin)

Fecha: 2026-06-01
Estado: aprobado (brainstorm) — pendiente de plan de implementación

## Objetivo

Hoy ClaudeBar solo hace **polling de cuota** (`/api/oauth/usage`) y no sabe qué están haciendo las
sesiones de Claude Code en la máquina. Esta feature añade **conciencia de estado en vivo**: una
**mascota ASCII** en el dashboard que reacciona a la fase de la sesión, una **lista de instancias**
activas, y **avisos nativos de Windows** cuando una sesión necesita atención (sobre todo *"espera tu
OK"*).

Enfoque elegido (brainstorm): un **hook de Claude Code** (PowerShell) emite cada evento a un **Named
Pipe** local; ClaudeBar (servidor del pipe) mantiene una máquina de estados por sesión, deriva una
**fase global** (la sesión más prioritaria) para la mascota, lista todas las instancias, y dispara
avisos por **bandeja nativa** con **supresión por foco**. Patrón conceptual tomado de
[Buddi](https://github.com/talkvalue/Buddi) y [Notchi](https://github.com/sk-ruban/notchi) (ambos
macOS, GPL-3.0) — **clean-room obligatorio**: se reimplementa en C#, sin copiar su código ni sus
strings; ClaudeBar es MIT.

## Decisiones del brainstorm

- **Alcance v1:** paquete completo = pipeline de hooks + mascota + avisos. (Acordado con Yovan.)
- **Canal de avisos:** **solo bandeja nativa de Windows** (globo + badge en el icono). **NO Telegram**
  → mantiene ClaudeBar autocontenido y publicable (MIT) sin atarlo a un bot concreto.
- **Supresión por foco:** un aviso solo se muestra si la terminal/ventana de esa sesión **no** está
  en primer plano (anti-spam mientras trabajas delante del PC).
- **Ubicación de la mascota:** en el **dashboard** (panel arrastrable). El **icono de bandeja conserva
  el % de cuota**; cuando hay permiso/input pendiente le sale un **badge ámbar** + globo nativo.
- **Bestiario:** **un bicho propio con 6 estados** (idle · pensando · ejecutando · espera-OK · error ·
  listo). Sin bestiario múltiple ni gacha en v1 (reservado para v2 vía `MascotKind`).
- **Multi-sesión (como Buddi):** se mantienen **todas** las sesiones; la **mascota = la sesión "top"
  por prioridad** (activa > necesita-atención > resto) y el dashboard **lista todas** las instancias
  ordenadas por urgencia.
- **Permiso:** **v1 solo notifica** (hook *fire-and-forget*, no bloquea). **v2** añadirá aprobar/denegar
  desde la bandeja (request/response por el pipe); el transporte se diseña preparado para ello.
- **Transporte:** **Named Pipe** (`\\.\pipe\claudebar`) + hook en **PowerShell** (cero dependencias;
  Buddi usa Python). Descartado vigilar el `.jsonl` del transcript (no da estado en vivo ni el evento
  de permiso) y descartado un hook `.exe` (PS ya está en todo Windows).
- **Instalación de hooks = opt-in.** Tocar `~/.claude/settings.json` requiere consentimiento explícito
  + backup, porque ese archivo es del que depende el Asistente 24/7 del usuario (hooks de crons/ruflo).
  Por defecto la feature está **apagada** hasta que el usuario active los hooks desde el menú.

## Contexto importante del entorno (Yovan)

- El **Asistente 24/7** corre con `--dangerously-skip-permissions` → **no dispara `PermissionRequest`**.
  El aviso *"espera tu OK"* aplica a las **sesiones manuales** de Claude Code; en el 24/7 la mascota
  mostrará pensando/ejecutando/idle/compactando, no espera-OK.
- `~/.claude/settings.json` ya contiene hooks `SessionStart` propios del Asistente. El instalador de
  hooks **debe** preservarlos intactos (merge no destructivo + marca propia).

## Arquitectura

Componentes nuevos. Todos en C#/.NET 9, salvo el hook (PowerShell). Cada uno con una responsabilidad
única y testeable de forma aislada.

### 1. `hooks/claudebar-hook.ps1` (plantilla embebida en la app)
Script instalado en `~/.claude/hooks/claudebar-hook.ps1`. En cada evento de Claude Code:
- Lee el JSON del evento de **stdin**.
- Extrae `session_id`, `cwd`, `transcript_path` (→ `pid`/tty si disponible), `hook_event_name`, y los
  campos específicos (`tool_name`, `tool_input`, `notification`…).
- Construye **una línea JSON** `{session_id, cwd, pid, event, status, tool, ts}` y la escribe al Named
  Pipe `\\.\pipe\claudebar` con **timeout de conexión corto (~200 ms)**.
- Si el pipe no existe (ClaudeBar cerrado) o el timeout vence → **`exit 0` en silencio**. El hook
  **nunca** bloquea ni falla la sesión de Claude (no escribe a stdout/stderr, no devuelve decisión).
- `status` se deriva del `hook_event_name`: `PreToolUse`→`running_tool`, `PostToolUse`→`processing`,
  `PermissionRequest`→`waiting_for_approval`, `Notification(idle)`→`waiting_for_input`,
  `Stop`/`SubagentStop`→`waiting_for_input`, `PreCompact`→`compacting`, `SessionStart`→`starting`,
  `SessionEnd`→`ended`, `UserPromptSubmit`→`processing`.

### 2. `Services/Hooks/HookPipeServer.cs`
`NamedPipeServerStream` **asíncrono, multi-cliente** (varias sesiones conectan a la vez). Lee líneas
JSON, deserializa a `HookEvent`, y dispara `event HookEventReceived`. Reabre nuevas instancias del
pipe según se consumen las conexiones (patrón "accept loop"). Se arranca/para con la app.
- `HookEvent` (record): `SessionId, Cwd, Pid?, Event, Status, Tool?, ToolInput?, ToolUseId?, Message?, Ts`.

### 3. `Services/Session/SessionPhase.cs` + `SessionState.cs` + `SessionStore.cs`
- **`SessionPhase`** (enum): `Idle · Processing · WaitingForApproval · WaitingForInput · Compacting ·
  Ended`. Método `CanTransition(to)` con transiciones válidas (reimplementación del patrón de Buddi).
  Propiedades derivadas `NeedsAttention` (Approval||Input), `IsActive` (Processing||Compacting).
- **`SessionState`**: `SessionId, Cwd, ProjectName (=último segmento de cwd), Pid?, Phase, LastActivity,
  PendingTool?`.
- **`SessionStore`**: `Dictionary<string, SessionState>` con bloqueo. `Apply(HookEvent)` = única vía de
  mutación: mapea `event+status → phase`, valida la transición (ignora las inválidas), actualiza
  `LastActivity`. **TTL**: un `Prune()` (timer ~60 s) marca `Idle` o elimina sesiones sin eventos en
  ~10 min (el `SessionEnd` no siempre llega). Expone `event Changed` con el snapshot de sesiones.

### 4. `Services/Session/SessionAggregator.cs`
Consume el snapshot del store y deriva:
- **Fase global** para la mascota: `primera activa ?? primera que necesita atención ?? primera ?? Idle`.
- **Instancias ordenadas** por prioridad de fase (activa/atención primero) y luego por `LastActivity`.
- **Disparo de avisos**: diffing contra el set de IDs ya vistos esperando (con *seeding* en el primer
  snapshot para **no** disparar al arrancar) → para cada **nueva** sesión en `WaitingForApproval` o
  `WaitingForInput`: comprueba **foco** (`GetForegroundWindow` + match con el `pid`/ventana de la
  sesión); si no está enfocada y pasó el **cooldown**, emite `NotifyRequested(session)`.

### 5. `Services/Mascot/MascotSprite.cs` + `MascotAnimator.cs`
- **Bestiario propio**: 1 criatura, **6 estados**, cada uno 2-3 frames ASCII (texto monoespaciado, sin
  assets binarios). Diseño concreto del bicho a definir en implementación; placeholder acordado:
  idle `( - ˮ - ) zzz` · pensando `( · ˮ · )·✳✳✳` · ejecutando `(๑> ˮ <๑)⚡` · espera-OK `( ʘ ˮ ʘ )¡!`
  · error `( ✕ ˮ ✕ )` · listo `( ^ ˮ ^ )✓`.
- **Lógica pura** `fase → frames` (sin estado mutable). `MascotAnimator` cicla frames con un `Timer`
  según la fase (parado en idle, spinner en pensando, etc.). Color por estado (paleta terminal:
  cian=ejecutando, ámbar=espera, verde=listo, rojo=error, dim=idle; acento naranja Claude `#d97857`).

### 6. UI
- **`UI/DashboardForm.cs`**: sección nueva renderizada dentro del `LayoutContent(g, draw)` auto-regulable
  ya existente (debajo de las barras de cuota): el **bicho + etiqueta de estado**, y una **lista de
  instancias** (`proyecto · mini-estado · hace Xs`). Sin sesiones → la sección se oculta y el panel
  encoge (ya soportado por `Relayout()`). Toggle "Mostrar sesiones/mascota" en **Ajustes → Secciones**
  (`ShowMascot`).
- **`UI/TrayIconRenderer.cs`**: cuando la fase global es `NeedsAttention`, superpone un **badge ámbar**
  (punto/anillo) sobre el % de cuota existente. Vuelve al icono normal al resolverse.
- **Globo nativo**: `NotifyIcon.ShowBalloonTip` (o toast Windows) — *"Claude espera tu OK en
  \<proyecto\>"* / *"Claude terminó en \<proyecto\>"*. Disparado por `NotifyRequested`.

### 7. `Services/Hooks/HookInstaller.cs`
Instalación/desinstalación **opt-in** desde el menú (Ajustes → "Sesiones en vivo…"):
- **Consentimiento**: diálogo que explica que se modificará `~/.claude/settings.json` y se creará el
  script del hook.
- **Backup**: copia `settings.json` → `settings.json.claudebar-bak-<timestamp>` antes de tocar.
- **Merge idempotente**: añade nuestro hook (comando que invoca `claudebar-hook.ps1`) a cada evento
  (`UserPromptSubmit, PreToolUse, PostToolUse, PermissionRequest, Notification, Stop, SubagentStop,
  SessionStart, SessionEnd, PreCompact`) **preservando** cualquier hook existente; detecta el nuestro
  por una marca (`claudebar-hook.ps1`) para no duplicar.
- **Desinstalar**: elimina solo nuestras entradas (por la marca) y el script; deja el resto intacto.
- **Estado**: `IsInstalled()` para reflejar el toggle en el menú.

### 8. Config nueva (`Config/AppConfig.cs`)
- `LiveSessionsEnabled` (bool, default **false**) — refleja si los hooks están instalados/activos.
- `ShowMascot` (bool, default true cuando LiveSessions on).
- `SuppressWhenFocused` (bool, default true).
- `MascotKind` (string, default `"default"`) — reservado para el bestiario de v2.

## Flujo de datos

```
evento Claude Code
  → claudebar-hook.ps1 (lee stdin, fire-and-forget)
  → línea JSON  → \\.\pipe\claudebar
  → HookPipeServer (deserializa → HookEvent)
  → SessionStore.Apply (valida transición, actualiza fase + LastActivity)
  → SessionAggregator (fase global + instancias ordenadas + ¿disparar aviso?)
  → UI: mascota anima · lista de instancias · badge bandeja · globo (si NeedsAttention y sin foco)
```

## Manejo de errores y seguridad

- **ClaudeBar cerrado**: el hook escribe a un pipe inexistente → fallo silencioso (`exit 0`); Claude
  Code no se ve afectado. Es el modo por defecto cuando la app no corre.
- **settings.json**: backup timestamped antes de cualquier cambio; merge no destructivo; marca propia
  para idempotencia y desinstalación limpia. **Nunca** se tocan hooks ajenos.
- **Sesiones fantasma**: TTL/`Prune` limpia sesiones sin actividad (cubre `SessionEnd` perdidos).
- **Anti-spam**: supresión por foco + cooldown por sesión.
- **Privacidad**: todo es **local** (pipe en la máquina). No hay red ni telemetría; el payload del hook
  no incluye contenido de prompts ni resultados de tools (solo nombre de tool y metadatos de fase).
- **Concurrencia**: pipe multi-cliente; acceso al `SessionStore` con bloqueo; mutaciones de UI
  marshalled al hilo de WinForms (`BeginInvoke`), como ya hace el resto de la app.

## Fuera de alcance (YAGNI v1)

- Parsing del `.jsonl` para reconstruir el chat completo (Buddi lo hace; aquí solo hooks).
- Aprobar/denegar permisos desde la app (v2 — transporte ya preparado para request/response).
- Bestiario múltiple / gacha por hash de cuenta.
- Avisos por Telegram u otros canales externos.
- Foco/activación de la ventana de la terminal desde la app (lo que Buddi hace con yabai/tmux).

## Testing

Primer proyecto de **tests** de ClaudeBar (`ClaudeBarWin.Tests`, xUnit):
- `SessionPhase.CanTransition` — transiciones válidas/ inválidas.
- Mapeo `event+status → phase`.
- Agregación: fase global "top por prioridad" y orden de instancias.
- Diffing de avisos: *seeding* no dispara en arranque; nueva sesión esperando sí; misma sesión no
  re-dispara; respeto del cooldown.
- Parse de `HookEvent` desde JSON (incluyendo campos ausentes).

Verificación funcional:
- Modo `--hook-test` que inyecta una secuencia de eventos sintéticos al pipe (o directamente al store)
  → observar mascota, lista, badge y globo sin necesitar sesiones reales.
- Prueba real: instalar hooks (opt-in) → abrir una sesión manual de Claude → forzar un permiso →
  verificar globo "espera tu OK" + badge + mascota en estado espera; comprobar supresión con la
  terminal en foco.
- Verificar que tras instalar/desinstalar hooks, `settings.json` queda **idéntico** salvo nuestras
  entradas, y que el Asistente 24/7 sigue arrancando con sus hooks intactos.

## Riesgos conocidos

- **Nombres de eventos/payload de hooks de Claude Code** pueden variar entre versiones del CLI;
  el mapeo `event→status` se centraliza en el hook + `SessionStore` para ajustarlo en un solo sitio.
- **Detección de foco por pid**: en Windows el `pid` del hook es el del proceso `claude`/shell, no
  siempre el de la ventana de terminal visible; el match foco↔sesión puede necesitar heurística
  (proceso ancestro / título de ventana). Si no se resuelve con fiabilidad, `SuppressWhenFocused`
  degrada con elegancia (peor caso: algún aviso de más, nunca de menos).
- **Tocar settings.json del Asistente 24/7**: mitigado con opt-in + backup + merge marcado, pero es la
  zona de mayor cuidado; el plan debe verificar el archivo antes/después.

## Versión y entrega

Feature destino: **v0.3** (después de cerrar y publicar v0.2.0 de auto-update, que va por separado).
Apagada por defecto; se activa desde el menú. No cambia el comportamiento actual de quien no la active.
