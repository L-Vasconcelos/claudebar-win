# Sesiones en vivo: mascota + avisos — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que ClaudeBar sepa en vivo qué hacen las sesiones de Claude Code (vía un hook → Named Pipe), muestre una mascota ASCII + lista de instancias en el dashboard, y avise por bandeja nativa cuando una sesión necesita atención — todo opt-in y sin tocar la cuota existente.

**Architecture:** Un hook PowerShell escribe cada evento de Claude Code como una línea JSON a `\\.\pipe\claudebar`. `HookPipeServer` (servidor del pipe) emite `HookEvent`; `SessionStore` mantiene una máquina de estados por sesión; `SessionAggregator` deriva la fase global (mascota), la lista ordenada de instancias y los disparos de aviso (diffing + supresión por foco). La UI (DashboardForm + TrayIconRenderer) lo consume por un provider sincrónico cacheado, desacoplado del refresco de cuota. La instalación de hooks es opt-in con backup de `~/.claude/settings.json`.

**Tech Stack:** C#/.NET 9 WinForms (`net9.0-windows`), `System.IO.Pipes` (BCL), `System.Text.Json` (shared framework), xUnit (proyecto de tests nuevo). PowerShell para el hook. Patrón conceptual de Buddi/Notchi reimplementado (clean-room; ClaudeBar es MIT).

**Build local:** la app se compila con
`$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal`.
Los tests con `dotnet test` (Task 0 crea el proyecto).

**Convenciones del repo (de los mapas):** namespaces por carpeta (`ClaudeBarWin.Models`, `ClaudeBarWin.Services`, `ClaudeBarWin.UI`, raíz `ClaudeBarWin`); `Nullable` + `ImplicitUsings` habilitados; sin DI (todo `new` en el ctor de `TrayAppContext`); toda mutación de config por `MutateConfig`; refresco de UI desde hilos no-UI con `try { _dashboard.BeginInvoke(new Action(...)); } catch { }`; cada sección del dashboard es un `DrawXxx(g, draw, x, y, w, ...)` que avanza y devuelve `y` igual en `draw=true` y `draw=false`.

**Línea roja:** ninguna tarea publica nada externo. Todo es local. Los commits son locales (no `git push`). La única acción que toca `~/.claude/settings.json` (Task 12/13) es **código** que solo se ejecuta cuando el usuario activa la feature; el plan no la activa por defecto.

---

## Estructura de archivos

**Nuevos:**
- `ClaudeBarWin.sln` — solución que agrupa app + tests.
- `ClaudeBarWin.Tests/ClaudeBarWin.Tests.csproj` — proyecto xUnit (`net9.0-windows`).
- `ClaudeBarWin.Tests/SessionPhaseTests.cs`, `HookEventTests.cs`, `SessionStoreTests.cs`, `SessionAggregatorTests.cs`, `AppConfigTests.cs`, `MascotSpriteTests.cs`, `HookInstallerTests.cs`, `HookPipeServerTests.cs`.
- `Models/SessionPhase.cs` — enum + extensiones (transiciones, NeedsAttention, IsActive).
- `Models/HookEvent.cs` — record del evento del pipe + parseo JSON + mapeo a fase.
- `Models/SessionState.cs` — estado por sesión.
- `Models/LiveSessionsView.cs` — DTO agregado para la UI (fase global + instancias).
- `Services/Session/SessionStore.cs` — diccionario de sesiones + Apply + Prune.
- `Services/Session/SessionAggregator.cs` — fase global + orden + diffing de avisos.
- `Services/Session/ForegroundDetector.cs` — `GetForegroundWindow` + heurística de foco.
- `Services/Hooks/HookPipeServer.cs` — `NamedPipeServerStream` async multi-cliente.
- `Services/Hooks/HookInstaller.cs` — backup + merge idempotente + uninstall de settings.json.
- `Services/Mascot/MascotSprite.cs` — lógica pura fase → frames ASCII.
- `hooks/claudebar-hook.ps1` — el hook (se embebe como recurso y se copia al instalar).

**Modificados:**
- `Config/AppConfig.cs` — 4 propiedades nuevas.
- `Services/Localization.cs` — strings + traducciones en los 7 idiomas no-inglés.
- `UI/TrayIconRenderer.cs` — badge ámbar opcional.
- `UI/DashboardForm.cs` — sección mascota+lista, provider, hit-test.
- `TrayAppContext.cs` — cablear servicios, menú, badge en `UpdateUi`, dispose.
- `Program.cs` — modo `--hook-test`.
- `ClaudeBarWin.csproj` — `InternalsVisibleTo`.

---

### Task 0: Andamiaje — solución + proyecto de tests

**Files:**
- Create: `ClaudeBarWin.Tests/ClaudeBarWin.Tests.csproj`
- Create: `ClaudeBarWin.sln`
- Modify: `ClaudeBarWin.csproj` (añadir `InternalsVisibleTo`)

- [ ] **Step 1: Crear el csproj de tests**

Create `ClaudeBarWin.Tests/ClaudeBarWin.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ClaudeBarWin.csproj" />
  </ItemGroup>

</Project>
```
(`net9.0-windows` y `UseWindowsForms` para poder referenciar el proyecto de producción que es WinForms.)

- [ ] **Step 2: Exponer internals a los tests**

En `ClaudeBarWin.csproj`, tras la línea 23 (cierre del `</ItemGroup>` de PackageReference), añadir un nuevo ItemGroup:
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ClaudeBarWin.Tests" />
  </ItemGroup>
```

- [ ] **Step 3: Crear la solución y añadir ambos proyectos**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; $d="$env:USERPROFILE\.dotnet\dotnet.exe"
cd "C:\Users\zorro\Proyectos\claudebar-win"
& $d new sln -n ClaudeBarWin
& $d sln add ClaudeBarWin.csproj "ClaudeBarWin.Tests\ClaudeBarWin.Tests.csproj"
```
Expected: "Project ... added to the solution" dos veces.

- [ ] **Step 4: Añadir un test trivial y verificar que el runner va**

Create `ClaudeBarWin.Tests/SmokeTest.cs`:
```csharp
namespace ClaudeBarWin.Tests;

public class SmokeTest
{
    [Fact]
    public void Runner_Works() => Assert.True(true);
}
```

- [ ] **Step 5: Ejecutar los tests**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.Tests\ClaudeBarWin.Tests.csproj" --nologo -v minimal
```
Expected: "Passed! - Failed: 0, Passed: 1". Si la restauración de paquetes falla por red, reintentar; los paquetes de test son estándar de NuGet.org.

- [ ] **Step 6: Commit**

```bash
git add ClaudeBarWin.sln ClaudeBarWin.Tests/ ClaudeBarWin.csproj
git commit -m "build: solucion + proyecto de tests xUnit (ClaudeBarWin.Tests)"
```

---

### Task 1: `SessionPhase` — enum y máquina de estados

**Files:**
- Create: `Models/SessionPhase.cs`
- Test: `ClaudeBarWin.Tests/SessionPhaseTests.cs`

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/SessionPhaseTests.cs`:
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Tests;

public class SessionPhaseTests
{
    [Fact]
    public void Idle_can_go_to_processing()
        => Assert.True(SessionPhase.Idle.CanTransition(SessionPhase.Processing));

    [Fact]
    public void Ended_is_terminal()
        => Assert.False(SessionPhase.Ended.CanTransition(SessionPhase.Processing));

    [Fact]
    public void Any_phase_can_end()
        => Assert.True(SessionPhase.Processing.CanTransition(SessionPhase.Ended));

    [Fact]
    public void Same_phase_is_a_noop_transition()
        => Assert.True(SessionPhase.Processing.CanTransition(SessionPhase.Processing));

    [Theory]
    [InlineData(SessionPhase.WaitingForApproval, true)]
    [InlineData(SessionPhase.WaitingForInput, true)]
    [InlineData(SessionPhase.Processing, false)]
    [InlineData(SessionPhase.Idle, false)]
    public void NeedsAttention_only_for_waiting(SessionPhase p, bool expected)
        => Assert.Equal(expected, p.NeedsAttention());

    [Theory]
    [InlineData(SessionPhase.Processing, true)]
    [InlineData(SessionPhase.Compacting, true)]
    [InlineData(SessionPhase.Idle, false)]
    public void IsActive_for_processing_and_compacting(SessionPhase p, bool expected)
        => Assert.Equal(expected, p.IsActive());
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.Tests\ClaudeBarWin.Tests.csproj" --nologo -v minimal
```
Expected: FALLA de compilación — `SessionPhase` no existe.

- [ ] **Step 3: Implementar el enum y las extensiones**

Create `Models/SessionPhase.cs`:
```csharp
namespace ClaudeBarWin.Models;

/// <summary>Fase del ciclo de vida de una sesión de Claude Code (máquina de estados).</summary>
public enum SessionPhase
{
    Idle,
    Processing,
    WaitingForApproval,
    WaitingForInput,
    Compacting,
    Ended,
}

public static class SessionPhaseExtensions
{
    /// <summary>¿La sesión necesita atención del usuario (espera OK o input)?</summary>
    public static bool NeedsAttention(this SessionPhase p)
        => p is SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput;

    /// <summary>¿La sesión está trabajando (procesando o compactando)?</summary>
    public static bool IsActive(this SessionPhase p)
        => p is SessionPhase.Processing or SessionPhase.Compacting;

    /// <summary>Prioridad para ordenar instancias y elegir la fase global (menor = más prioritario).</summary>
    public static int Priority(this SessionPhase p) => p switch
    {
        SessionPhase.WaitingForApproval => 0,
        SessionPhase.WaitingForInput => 1,
        SessionPhase.Processing => 2,
        SessionPhase.Compacting => 2,
        SessionPhase.Idle => 3,
        SessionPhase.Ended => 4,
        _ => 5,
    };

    /// <summary>¿Es válida la transición a <paramref name="next"/>?</summary>
    public static bool CanTransition(this SessionPhase from, SessionPhase next)
    {
        if (from == next) return true;          // no-op
        if (from == SessionPhase.Ended) return false; // terminal
        if (next == SessionPhase.Ended) return true;  // cualquiera puede terminar
        return (from, next) switch
        {
            (SessionPhase.Idle, SessionPhase.Processing) => true,
            (SessionPhase.Idle, SessionPhase.WaitingForApproval) => true,
            (SessionPhase.Idle, SessionPhase.Compacting) => true,
            (SessionPhase.Processing, SessionPhase.WaitingForInput) => true,
            (SessionPhase.Processing, SessionPhase.WaitingForApproval) => true,
            (SessionPhase.Processing, SessionPhase.Compacting) => true,
            (SessionPhase.Processing, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Processing) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForInput, SessionPhase.Compacting) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.Processing) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.Idle) => true,
            (SessionPhase.WaitingForApproval, SessionPhase.WaitingForInput) => true,
            (SessionPhase.Compacting, SessionPhase.Processing) => true,
            (SessionPhase.Compacting, SessionPhase.Idle) => true,
            (SessionPhase.Compacting, SessionPhase.WaitingForInput) => true,
            _ => false,
        };
    }
}
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de Step 2.
Expected: "Passed! - Failed: 0" (todos los de SessionPhase + smoke).

- [ ] **Step 5: Commit**

```bash
git add Models/SessionPhase.cs ClaudeBarWin.Tests/SessionPhaseTests.cs
git commit -m "feat: SessionPhase enum + maquina de estados (transiciones, prioridad)"
```

---

### Task 2: `HookEvent` — parseo JSON y mapeo a fase

**Files:**
- Create: `Models/HookEvent.cs`
- Test: `ClaudeBarWin.Tests/HookEventTests.cs`

El hook escribe líneas JSON con estas claves (snake_case): `session_id`, `cwd`, `pid`, `event`, `status`, `tool`, `tool_use_id`, `message`, `ts`.

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/HookEventTests.cs`:
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Tests;

public class HookEventTests
{
    [Fact]
    public void Parses_minimal_json()
    {
        var e = HookEvent.Parse("""{"session_id":"abc","cwd":"C:\\proj\\x","event":"PreToolUse","status":"running_tool","tool":"Bash"}""");
        Assert.NotNull(e);
        Assert.Equal("abc", e!.SessionId);
        Assert.Equal("C:\\proj\\x", e.Cwd);
        Assert.Equal("Bash", e.Tool);
    }

    [Fact]
    public void Returns_null_on_garbage()
        => Assert.Null(HookEvent.Parse("not json"));

    [Fact]
    public void Returns_null_when_session_id_missing()
        => Assert.Null(HookEvent.Parse("""{"event":"Stop"}"""));

    [Theory]
    [InlineData("waiting_for_approval", SessionPhase.WaitingForApproval)]
    [InlineData("waiting_for_input", SessionPhase.WaitingForInput)]
    [InlineData("running_tool", SessionPhase.Processing)]
    [InlineData("processing", SessionPhase.Processing)]
    [InlineData("starting", SessionPhase.Processing)]
    [InlineData("compacting", SessionPhase.Compacting)]
    [InlineData("ended", SessionPhase.Ended)]
    [InlineData("whatever", SessionPhase.Idle)]
    public void Maps_status_to_phase(string status, SessionPhase expected)
    {
        var e = HookEvent.Parse($$"""{"session_id":"s","cwd":"c","event":"X","status":"{{status}}"}""");
        Assert.Equal(expected, e!.ToPhase());
    }

    [Fact]
    public void PreCompact_event_forces_compacting_regardless_of_status()
    {
        var e = HookEvent.Parse("""{"session_id":"s","cwd":"c","event":"PreCompact","status":"running_tool"}""");
        Assert.Equal(SessionPhase.Compacting, e!.ToPhase());
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test de Task 1 Step 2. Expected: FALLA de compilación — `HookEvent` no existe.

- [ ] **Step 3: Implementar el record**

Create `Models/HookEvent.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeBarWin.Models;

/// <summary>Un evento emitido por el hook de Claude Code a través del Named Pipe.</summary>
public sealed class HookEvent
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("tool")] public string? Tool { get; set; }
    [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parsea una línea JSON. Devuelve null si no es JSON válido o falta session_id.</summary>
    public static HookEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var e = JsonSerializer.Deserialize<HookEvent>(line, Opts);
            if (e is null || string.IsNullOrEmpty(e.SessionId)) return null;
            return e;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Mapea el evento a una fase de sesión.</summary>
    public SessionPhase ToPhase()
    {
        if (Event == "PreCompact") return SessionPhase.Compacting;
        return Status switch
        {
            "waiting_for_approval" => SessionPhase.WaitingForApproval,
            "waiting_for_input" => SessionPhase.WaitingForInput,
            "running_tool" or "processing" or "starting" => SessionPhase.Processing,
            "compacting" => SessionPhase.Compacting,
            "ended" => SessionPhase.Ended,
            _ => SessionPhase.Idle,
        };
    }
}
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**

```bash
git add Models/HookEvent.cs ClaudeBarWin.Tests/HookEventTests.cs
git commit -m "feat: HookEvent (parseo JSON del pipe + mapeo a SessionPhase)"
```

---

### Task 3: `SessionState` + `SessionStore`

**Files:**
- Create: `Models/SessionState.cs`
- Create: `Services/Session/SessionStore.cs`
- Test: `ClaudeBarWin.Tests/SessionStoreTests.cs`

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/SessionStoreTests.cs`:
```csharp
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionStoreTests
{
    private static HookEvent Ev(string id, string cwd, string ev, string status, string? tool = null)
        => new() { SessionId = id, Cwd = cwd, Event = ev, Status = status, Tool = tool };

    [Fact]
    public void Apply_creates_session_with_project_name_from_cwd()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "C:\\Users\\z\\Proyectos\\phoenix", "PreToolUse", "running_tool"), DateTime.UtcNow);
        var sess = Assert.Single(s.Snapshot());
        Assert.Equal("s1", sess.SessionId);
        Assert.Equal("phoenix", sess.ProjectName);
        Assert.Equal(SessionPhase.Processing, sess.Phase);
    }

    [Fact]
    public void Apply_ignores_invalid_transition_but_keeps_session()
    {
        var s = new SessionStore();
        var t0 = DateTime.UtcNow;
        s.Apply(Ev("s1", "c", "SessionEnd", "ended"), t0);
        // Ended es terminal: un nuevo evento processing no debe revivirla a Processing
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), t0.AddSeconds(1));
        Assert.Equal(SessionPhase.Ended, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Apply_records_pending_tool_on_approval()
    {
        var s = new SessionStore();
        s.Apply(Ev("s1", "c", "PermissionRequest", "waiting_for_approval", tool: "Bash"), DateTime.UtcNow);
        Assert.Equal("Bash", s.Snapshot()[0].PendingTool);
    }

    [Fact]
    public void Prune_removes_stale_sessions()
    {
        var s = new SessionStore();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Apply(Ev("old", "c", "PreToolUse", "running_tool"), t0);
        s.Apply(Ev("fresh", "c", "PreToolUse", "running_tool"), t0.AddMinutes(9));
        var removed = s.Prune(t0.AddMinutes(10), TimeSpan.FromMinutes(10));
        Assert.Equal(1, removed);
        Assert.Equal("fresh", Assert.Single(s.Snapshot()).SessionId);
    }

    [Fact]
    public void Apply_raises_Changed()
    {
        var s = new SessionStore();
        var fired = 0;
        s.Changed += () => fired++;
        s.Apply(Ev("s1", "c", "PreToolUse", "running_tool"), DateTime.UtcNow);
        Assert.Equal(1, fired);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `SessionState`/`SessionStore` no existen.

- [ ] **Step 3: Implementar `SessionState`**

Create `Models/SessionState.cs`:
```csharp
namespace ClaudeBarWin.Models;

/// <summary>Estado vivo de una sesión de Claude Code. Mutado solo por SessionStore.</summary>
public sealed class SessionState
{
    public required string SessionId { get; init; }
    public required string Cwd { get; set; }
    public string ProjectName { get; set; } = "";
    public int? Pid { get; set; }
    public SessionPhase Phase { get; set; } = SessionPhase.Idle;
    public string? PendingTool { get; set; }
    public DateTime LastActivityUtc { get; set; }

    /// <summary>Copia superficial para exponer snapshots inmutables a la UI.</summary>
    public SessionState Clone() => new()
    {
        SessionId = SessionId,
        Cwd = Cwd,
        ProjectName = ProjectName,
        Pid = Pid,
        Phase = Phase,
        PendingTool = PendingTool,
        LastActivityUtc = LastActivityUtc,
    };

    public static string ProjectNameFromCwd(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "(sin proyecto)";
        var trimmed = cwd.TrimEnd('\\', '/');
        var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
    }
}
```

- [ ] **Step 4: Implementar `SessionStore`**

Create `Services/Session/SessionStore.cs`:
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Fuente única de verdad de las sesiones vivas. Thread-safe vía lock.</summary>
public sealed class SessionStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionState> _sessions = new();

    /// <summary>Se dispara tras cualquier mutación (en el hilo que llamó Apply/Prune).</summary>
    public event Action? Changed;

    /// <summary>Aplica un evento del hook. nowUtc se inyecta para testabilidad.</summary>
    public void Apply(HookEvent e, DateTime nowUtc)
    {
        bool changed;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(e.SessionId, out var s))
            {
                s = new SessionState { SessionId = e.SessionId, Cwd = e.Cwd };
                _sessions[e.SessionId] = s;
            }

            if (!string.IsNullOrEmpty(e.Cwd)) s.Cwd = e.Cwd;
            s.ProjectName = SessionState.ProjectNameFromCwd(s.Cwd);
            if (e.Pid is { } pid) s.Pid = pid;

            var next = e.ToPhase();
            if (s.Phase.CanTransition(next)) s.Phase = next;

            s.PendingTool = s.Phase == SessionPhase.WaitingForApproval ? (e.Tool ?? s.PendingTool) : null;
            s.LastActivityUtc = nowUtc;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Elimina sesiones Ended o sin actividad desde hace más de ttl. Devuelve cuántas quitó.</summary>
    public int Prune(DateTime nowUtc, TimeSpan ttl)
    {
        int removed;
        lock (_lock)
        {
            var dead = _sessions.Values
                .Where(s => s.Phase == SessionPhase.Ended || nowUtc - s.LastActivityUtc > ttl)
                .Select(s => s.SessionId)
                .ToList();
            foreach (var id in dead) _sessions.Remove(id);
            removed = dead.Count;
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Snapshot inmutable de las sesiones actuales.</summary>
    public IReadOnlyList<SessionState> Snapshot()
    {
        lock (_lock) return _sessions.Values.Select(s => s.Clone()).ToList();
    }
}
```

- [ ] **Step 5: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 6: Commit**

```bash
git add Models/SessionState.cs Services/Session/SessionStore.cs ClaudeBarWin.Tests/SessionStoreTests.cs
git commit -m "feat: SessionState + SessionStore (apply/prune/snapshot, TTL)"
```

---

### Task 4: `SessionAggregator` + `LiveSessionsView`

**Files:**
- Create: `Models/LiveSessionsView.cs`
- Create: `Services/Session/SessionAggregator.cs`
- Test: `ClaudeBarWin.Tests/SessionAggregatorTests.cs`

`SessionAggregator` es lógica pura: recibe el snapshot y produce (a) la `LiveSessionsView` (fase global + instancias ordenadas) y (b) la lista de sesiones que deben disparar aviso *en esta actualización* (diffing contra lo ya visto, con seeding en la primera llamada y cooldown por sesión). La supresión por foco NO va aquí (se inyecta el resultado de foco), para mantenerlo testeable.

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/SessionAggregatorTests.cs`:
```csharp
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionAggregatorTests
{
    private static SessionState S(string id, SessionPhase phase, DateTime when)
        => new() { SessionId = id, Cwd = "c\\" + id, ProjectName = id, Phase = phase, LastActivityUtc = when };

    private readonly DateTime _t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Global_phase_is_most_urgent()
    {
        var agg = new SessionAggregator();
        var view = agg.BuildView(new[]
        {
            S("a", SessionPhase.Processing, _t0),
            S("b", SessionPhase.WaitingForApproval, _t0),
            S("c", SessionPhase.Idle, _t0),
        });
        Assert.Equal(SessionPhase.WaitingForApproval, view.GlobalPhase);
        Assert.Equal("b", view.Instances[0].SessionId); // el más prioritario primero
    }

    [Fact]
    public void Empty_snapshot_is_idle()
        => Assert.Equal(SessionPhase.Idle, new SessionAggregator().BuildView(Array.Empty<SessionState>()).GlobalPhase);

    [Fact]
    public void Seeding_does_not_notify_on_first_call()
    {
        var agg = new SessionAggregator();
        var n = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0);
        Assert.Empty(n); // primera llamada solo siembra
    }

    [Fact]
    public void New_waiting_session_notifies_after_seed()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0); // seed vacío
        var n = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        Assert.Single(n);
        Assert.Equal("a", n[0].SessionId);
    }

    [Fact]
    public void Same_waiting_session_does_not_renotify()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0);
        agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        var again = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(2));
        Assert.Empty(again);
    }

    [Fact]
    public void Resolving_then_waiting_again_renotifies()
    {
        var agg = new SessionAggregator();
        agg.DiffNotifications(Array.Empty<SessionState>(), _t0);
        agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(1));
        agg.DiffNotifications(new[] { S("a", SessionPhase.Processing, _t0) }, _t0.AddSeconds(2)); // resuelto
        var renote = agg.DiffNotifications(new[] { S("a", SessionPhase.WaitingForApproval, _t0) }, _t0.AddSeconds(3));
        Assert.Single(renote);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `SessionAggregator`/`LiveSessionsView` no existen.

- [ ] **Step 3: Implementar `LiveSessionsView`**

Create `Models/LiveSessionsView.cs`:
```csharp
namespace ClaudeBarWin.Models;

/// <summary>Vista agregada de las sesiones para la UI (mascota + lista).</summary>
public sealed class LiveSessionsView
{
    public SessionPhase GlobalPhase { get; init; } = SessionPhase.Idle;
    public IReadOnlyList<SessionState> Instances { get; init; } = Array.Empty<SessionState>();
    public int ActiveCount { get; init; }
}
```

- [ ] **Step 4: Implementar `SessionAggregator`**

Create `Services/Session/SessionAggregator.cs`:
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Lógica pura: snapshot de sesiones → vista agregada + diffing de avisos.</summary>
public sealed class SessionAggregator
{
    // IDs que en el snapshot anterior estaban esperando atención (para no re-avisar).
    private readonly HashSet<string> _knownWaiting = new();
    private bool _seeded;

    /// <summary>Ordena por prioridad de fase y luego por actividad reciente; deriva la fase global.</summary>
    public LiveSessionsView BuildView(IReadOnlyList<SessionState> snapshot)
    {
        var ordered = snapshot
            .OrderBy(s => s.Phase.Priority())
            .ThenByDescending(s => s.LastActivityUtc)
            .ToList();

        var global = ordered.Count == 0 ? SessionPhase.Idle : ordered[0].Phase;
        return new LiveSessionsView
        {
            GlobalPhase = global,
            Instances = ordered,
            ActiveCount = ordered.Count(s => s.Phase.IsActive() || s.Phase.NeedsAttention()),
        };
    }

    /// <summary>
    /// Devuelve las sesiones que pasaron a "necesita atención" desde la última llamada.
    /// La primera llamada solo siembra (no avisa) para no disparar al arrancar.
    /// </summary>
    public IReadOnlyList<SessionState> DiffNotifications(IReadOnlyList<SessionState> snapshot, DateTime nowUtc)
    {
        var waitingNow = snapshot.Where(s => s.Phase.NeedsAttention()).ToList();
        var idsNow = waitingNow.Select(s => s.SessionId).ToHashSet();

        if (!_seeded)
        {
            _seeded = true;
            SyncKnown(idsNow);
            return Array.Empty<SessionState>();
        }

        var fresh = waitingNow.Where(s => !_knownWaiting.Contains(s.SessionId)).ToList();
        SyncKnown(idsNow);
        return fresh;
    }

    private void SyncKnown(HashSet<string> idsNow)
    {
        _knownWaiting.RemoveWhere(id => !idsNow.Contains(id)); // los que ya no esperan se olvidan
        foreach (var id in idsNow) _knownWaiting.Add(id);
    }
}
```

- [ ] **Step 5: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 6: Commit**

```bash
git add Models/LiveSessionsView.cs Services/Session/SessionAggregator.cs ClaudeBarWin.Tests/SessionAggregatorTests.cs
git commit -m "feat: SessionAggregator (fase global, orden por prioridad, diffing de avisos con seeding)"
```

---

### Task 5: `MascotSprite` — fase → frames ASCII

**Files:**
- Create: `Services/Mascot/MascotSprite.cs`
- Test: `ClaudeBarWin.Tests/MascotSpriteTests.cs`

Lógica pura: dada una fase, devuelve los frames ASCII y una etiqueta de estado (key de localización). El arte concreto es placeholder acordado; un solo bestiario (`"cat"`).

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/MascotSpriteTests.cs`:
```csharp
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class MascotSpriteTests
{
    [Fact]
    public void Every_phase_has_at_least_one_frame()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            var frames = MascotSprite.Frames(p);
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.False(string.IsNullOrEmpty(f)));
        }
    }

    [Fact]
    public void Idle_is_a_single_static_frame()
        => Assert.Single(MascotSprite.Frames(SessionPhase.Idle));

    [Fact]
    public void Processing_animates_with_multiple_frames()
        => Assert.True(MascotSprite.Frames(SessionPhase.Processing).Count > 1);

    [Fact]
    public void Label_key_is_defined_for_every_phase()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            Assert.False(string.IsNullOrEmpty(MascotSprite.LabelKey(p)));
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `MascotSprite` no existe.

- [ ] **Step 3: Implementar `MascotSprite`**

Create `Services/Mascot/MascotSprite.cs`:
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Bestiario ASCII propio (clean-room). Cada fase devuelve N frames de texto monoespaciado.
/// El animador cicla los frames; idle es estático. LabelKey devuelve el nombre de la propiedad
/// de Localization.Strings con la etiqueta traducida.
/// </summary>
public static class MascotSprite
{
    // Un bicho propio de ClaudeBar (gato terminal). Frames de 1 línea por simplicidad de layout.
    public static IReadOnlyList<string> Frames(SessionPhase phase) => phase switch
    {
        SessionPhase.Idle => new[] { "( -.- ) zzz" },
        SessionPhase.Processing => new[] { "( o.o )", "( o.- )", "( -.o )" },
        SessionPhase.Compacting => new[] { "( >.< )~", "( >.< )≈" },
        SessionPhase.WaitingForApproval => new[] { "( O.O )!", "( o.o )!" },
        SessionPhase.WaitingForInput => new[] { "( ^.^ )?", "( ^.~ )?" },
        SessionPhase.Ended => new[] { "( x.x )" },
        _ => new[] { "( -.- )" },
    };

    /// <summary>Nombre de la propiedad de Strings con la etiqueta del estado (ver Task 7).</summary>
    public static string LabelKey(SessionPhase phase) => phase switch
    {
        SessionPhase.Idle => nameof(Localization),       // resuelto por el caller; ver nota
        _ => phase.ToString(),
    };
}
```

> NOTA para el implementador: `LabelKey` devuelve un identificador estable de fase; el `DashboardForm` (Task 9/10) lo mapea a la propiedad concreta de `Strings` (`SessionPhaseIdle`, `SessionPhaseWorking`, etc.) con un `switch` local. Mantener `LabelKey` como string no-vacío por fase es lo único que el test exige; ajustar el cuerpo para que devuelva p. ej. `"Idle"`, `"Processing"`… (sin referenciar `Localization`). Reemplaza el cuerpo por:
```csharp
    public static string LabelKey(SessionPhase phase) => phase.ToString();
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**

```bash
git add Services/Mascot/MascotSprite.cs ClaudeBarWin.Tests/MascotSpriteTests.cs
git commit -m "feat: MascotSprite (bestiario ASCII propio, frames por fase)"
```

---

### Task 6: `AppConfig` — 4 propiedades nuevas

**Files:**
- Modify: `Config/AppConfig.cs` (insertar tras línea 56, antes de `ConfigPath` en línea 58)
- Test: `ClaudeBarWin.Tests/AppConfigTests.cs`

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/AppConfigTests.cs`:
```csharp
using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_are_opt_in_for_live_sessions()
    {
        var c = new AppConfig();
        Assert.False(c.LiveSessionsEnabled);
        Assert.True(c.ShowMascot);
        Assert.True(c.SuppressWhenFocused);
        Assert.Equal("cat", c.MascotKind);
    }

    [Fact]
    public void Roundtrips_through_json()
    {
        var c = new AppConfig { LiveSessionsEnabled = true, ShowMascot = false, SuppressWhenFocused = false, MascotKind = "cat" };
        var json = JsonSerializer.Serialize(c);
        var back = JsonSerializer.Deserialize<AppConfig>(json)!;
        Assert.True(back.LiveSessionsEnabled);
        Assert.False(back.ShowMascot);
        Assert.False(back.SuppressWhenFocused);
    }

    [Fact]
    public void Missing_keys_fall_back_to_defaults()
    {
        var back = JsonSerializer.Deserialize<AppConfig>("{}")!;
        Assert.False(back.LiveSessionsEnabled);
        Assert.True(back.ShowMascot);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — las propiedades no existen.

- [ ] **Step 3: Añadir las propiedades**

En `Config/AppConfig.cs`, justo después de `public double DashboardOpacity { get; set; } = 1.0;` (línea 56) y ANTES de `[JsonIgnore] public static string ConfigPath` (línea 58), insertar:
```csharp

    // Live sessions (hook de Claude Code -> Named Pipe)
    /// <summary>Interruptor maestro de la feature de sesiones en vivo (listener del pipe + mascota + lista).</summary>
    public bool LiveSessionsEnabled { get; set; } = false;
    /// <summary>Mostrar la mascota ASCII que reacciona a la fase global de las sesiones.</summary>
    public bool ShowMascot { get; set; } = true;
    /// <summary>No avisar mientras una ventana de Claude Code/terminal sea la del primer plano.</summary>
    public bool SuppressWhenFocused { get; set; } = true;
    /// <summary>Bestiario de la mascota a renderizar (de momento solo "cat").</summary>
    public string MascotKind { get; set; } = "cat";
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**

```bash
git add Config/AppConfig.cs ClaudeBarWin.Tests/AppConfigTests.cs
git commit -m "feat: config de sesiones en vivo (LiveSessionsEnabled/ShowMascot/SuppressWhenFocused/MascotKind)"
```

---

### Task 7: `HookInstaller` — backup + merge idempotente de settings.json

**Files:**
- Create: `Services/Hooks/HookInstaller.cs`
- Test: `ClaudeBarWin.Tests/HookInstallerTests.cs`

La lógica de merge/unmerge del JSON se aísla en métodos `static` puros (testeables con strings); los métodos `Install()/Uninstall()` solo hacen el IO (backup + escribir). El hook se identifica por la marca `claudebar-hook.ps1` en el comando.

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/HookInstallerTests.cs`:
```csharp
using System.Text.Json;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class HookInstallerTests
{
    private const string Cmd = "powershell -NoProfile -File \"C:\\x\\claudebar-hook.ps1\"";

    [Fact]
    public void Merge_into_empty_settings_adds_all_events()
    {
        var merged = HookInstaller.MergeSettings("{}", Cmd);
        using var doc = JsonDocument.Parse(merged);
        var hooks = doc.RootElement.GetProperty("hooks");
        Assert.True(hooks.TryGetProperty("PreToolUse", out _));
        Assert.True(hooks.TryGetProperty("PermissionRequest", out _));
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    [Fact]
    public void Merge_is_idempotent()
    {
        var once = HookInstaller.MergeSettings("{}", Cmd);
        var twice = HookInstaller.MergeSettings(once, Cmd);
        // No debe duplicar nuestra entrada: contar ocurrencias de la marca por evento sigue siendo 1 cada uno.
        var count = twice.Split("claudebar-hook.ps1").Length - 1;
        var onceCount = once.Split("claudebar-hook.ps1").Length - 1;
        Assert.Equal(onceCount, count);
    }

    [Fact]
    public void Merge_preserves_foreign_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        Assert.Contains("echo cron-setup", merged); // el hook del Asistente sobrevive
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    [Fact]
    public void Remove_strips_only_our_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("echo cron-setup", removed);
        Assert.DoesNotContain("claudebar-hook.ps1", removed);
    }

    [Fact]
    public void Remove_preserves_non_hook_settings()
    {
        var existing = """{"model":"opus","hooks":{}}""";
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("\"model\"", removed);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `HookInstaller` no existe.

- [ ] **Step 3: Implementar `HookInstaller`**

Create `Services/Hooks/HookInstaller.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeBarWin.Services;

/// <summary>
/// Instala/desinstala el hook de ClaudeBar en ~/.claude/settings.json de forma idempotente,
/// preservando hooks ajenos y haciendo backup. La lógica de merge es pura y testeable.
/// </summary>
public static class HookInstaller
{
    public const string Marker = "claudebar-hook.ps1";

    private static readonly string[] Events =
    {
        "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
        "Notification", "Stop", "SubagentStop", "SessionStart", "SessionEnd", "PreCompact",
    };

    public static string ClaudeDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static string SettingsPath => Path.Combine(ClaudeDir, "settings.json");
    public static string HookScriptPath => Path.Combine(ClaudeDir, "hooks", "claudebar-hook.ps1");

    /// <summary>Comando que invoca el hook (PowerShell, sin perfil, leyendo el evento de stdin).</summary>
    public static string HookCommand() =>
        $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{HookScriptPath}\"";

    /// <summary>Devuelve true si nuestro hook ya está en settings.json.</summary>
    public static bool IsInstalled()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains(Marker); }
        catch { return false; }
    }

    /// <summary>Merge puro: añade nuestro hook a cada evento sin duplicar ni tocar hooks ajenos.</summary>
    public static string MergeSettings(string json, string command)
    {
        var root = (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject) ?? new JsonObject();
        var hooks = root["hooks"] as JsonObject;
        if (hooks is null) { hooks = new JsonObject(); root["hooks"] = hooks; }

        foreach (var ev in Events)
        {
            var arr = hooks[ev] as JsonArray;
            if (arr is null) { arr = new JsonArray(); hooks[ev] = arr; }

            if (ContainsMarker(arr)) continue; // idempotente

            arr.Add(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            });
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Quita solo nuestras entradas (por la marca), dejando el resto intacto.</summary>
    public static string RemoveHooks(string json)
    {
        var root = (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject) ?? new JsonObject();
        if (root["hooks"] is not JsonObject hooks)
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        foreach (var ev in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[ev] is not JsonArray arr) continue;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (EntryHasMarker(arr[i])) arr.RemoveAt(i);
            }
            if (arr.Count == 0) hooks.Remove(ev);
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool ContainsMarker(JsonArray arr) => arr.Any(EntryHasMarker);

    private static bool EntryHasMarker(JsonNode? entry)
    {
        if (entry is not JsonObject obj || obj["hooks"] is not JsonArray inner) return false;
        return inner.Any(h => h is JsonObject ho && (ho["command"]?.GetValue<string>() ?? "").Contains(Marker));
    }

    /// <summary>Escribe el script del hook, hace backup de settings.json y mergea. Devuelve la ruta del backup.</summary>
    public static string Install(string hookScriptContents, string backupStamp)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HookScriptPath)!);
        File.WriteAllText(HookScriptPath, hookScriptContents);

        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
        var backup = SettingsPath + ".claudebar-bak-" + backupStamp;
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(backup, current);

        File.WriteAllText(SettingsPath, MergeSettings(current, HookCommand()));
        return backup;
    }

    /// <summary>Quita el hook de settings.json (con backup) y borra el script.</summary>
    public static string Uninstall(string backupStamp)
    {
        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
        var backup = SettingsPath + ".claudebar-bak-" + backupStamp;
        File.WriteAllText(backup, current);
        File.WriteAllText(SettingsPath, RemoveHooks(current));
        try { if (File.Exists(HookScriptPath)) File.Delete(HookScriptPath); } catch { }
        return backup;
    }
}
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**

```bash
git add Services/Hooks/HookInstaller.cs ClaudeBarWin.Tests/HookInstallerTests.cs
git commit -m "feat: HookInstaller (merge/unmerge idempotente de settings.json + backup)"
```

---

### Task 8: `HookPipeServer` — servidor del Named Pipe

**Files:**
- Create: `Services/Hooks/HookPipeServer.cs`
- Test: `ClaudeBarWin.Tests/HookPipeServerTests.cs`

Servidor async multi-cliente sobre `\\.\pipe\claudebar`. Cada conexión envía una línea JSON y se cierra; el server la parsea a `HookEvent` y dispara `EventReceived`. El nombre del pipe es público para el test (y para el hook).

- [ ] **Step 1: Escribir el test que falla (round-trip por el pipe real)**

Create `ClaudeBarWin.Tests/HookPipeServerTests.cs`:
```csharp
using System.IO.Pipes;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class HookPipeServerTests
{
    [Fact]
    public async Task Server_receives_and_parses_a_client_line()
    {
        // Pipe con nombre único para no chocar con una instancia real corriendo.
        var pipeName = "claudebar-test-" + Guid.NewGuid().ToString("N");
        using var server = new HookPipeServer(pipeName);
        HookEvent? got = null;
        var tcs = new TaskCompletionSource();
        server.EventReceived += e => { got = e; tcs.TrySetResult(); };
        server.Start();

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await client.ConnectAsync(2000);
            using var w = new StreamWriter(client) { AutoFlush = true };
            await w.WriteLineAsync("""{"session_id":"s1","cwd":"c","event":"PreToolUse","status":"running_tool","tool":"Bash"}""");
        }

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.NotNull(got);
        Assert.Equal("s1", got!.SessionId);
        Assert.Equal("Bash", got.Tool);
    }

    [Fact]
    public void DefaultPipeName_is_stable()
        => Assert.Equal("claudebar", HookPipeServer.DefaultPipeName);
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `HookPipeServer` no existe.

- [ ] **Step 3: Implementar `HookPipeServer`**

Create `Services/Hooks/HookPipeServer.cs`:
```csharp
using System.IO.Pipes;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Servidor del Named Pipe que recibe eventos del hook de Claude Code.
/// Multi-cliente: acepta una conexión, lee una línea JSON, la parsea y vuelve a escuchar.
/// </summary>
public sealed class HookPipeServer : IDisposable
{
    public const string DefaultPipeName = "claudebar";

    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Se dispara (en un hilo del thread-pool) por cada evento recibido.</summary>
    public event Action<HookEvent>? EventReceived;

    public HookPipeServer(string pipeName = DefaultPipeName) => _pipeName = pipeName;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _loop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    var e = HookEvent.Parse(line);
                    if (e is not null) EventReceived?.Invoke(e);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* conexión rota: seguir aceptando */ }
        }
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0". (El test usa un nombre de pipe único, no choca con instancias reales.)

- [ ] **Step 5: Commit**

```bash
git add Services/Hooks/HookPipeServer.cs ClaudeBarWin.Tests/HookPipeServerTests.cs
git commit -m "feat: HookPipeServer (NamedPipeServerStream async, round-trip de eventos)"
```

---

### Task 9: `ForegroundDetector` — supresión por foco

**Files:**
- Create: `Services/Session/ForegroundDetector.cs`
- Test: `ClaudeBarWin.Tests/ForegroundDetectorTests.cs`

No es unit-testeable de forma determinista (depende de la ventana en foco real); el test solo verifica que no lanza y que `pid==null` devuelve `false`.

- [ ] **Step 1: Escribir el test que falla**

Create `ClaudeBarWin.Tests/ForegroundDetectorTests.cs`:
```csharp
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ForegroundDetectorTests
{
    [Fact]
    public void Null_pid_is_not_foreground()
        => Assert.False(new ForegroundDetector().IsSessionForeground(null));

    [Fact]
    public void Does_not_throw_for_arbitrary_pid()
    {
        var d = new ForegroundDetector();
        var _ = d.IsSessionForeground(999999); // pid inexistente: false sin excepción
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run el comando de test. Expected: FALLA de compilación — `ForegroundDetector` no existe.

- [ ] **Step 3: Implementar `ForegroundDetector`**

Create `Services/Session/ForegroundDetector.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services;

/// <summary>
/// Decide si la sesión cuyo pid se pasa está "en primer plano" (su proceso o un ancestro
/// es el dueño de la ventana del primer plano). Heurística: comparamos el pid de la ventana
/// foreground y subimos por el árbol de procesos buscando el pid de la sesión.
/// </summary>
public sealed class ForegroundDetector
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public bool IsSessionForeground(int? sessionPid)
    {
        if (sessionPid is not { } target) return false;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint fgPid);
            if (fgPid == 0) return false;
            if ((int)fgPid == target) return true;

            // Subir por ancestros del proceso foreground (terminal -> shell -> claude).
            var seen = 0;
            int? cur = (int)fgPid;
            while (cur is { } pid && seen++ < 6)
            {
                if (pid == target) return true;
                cur = ParentPid(pid);
            }
            return false;
        }
        catch { return false; }
    }

    private static int? ParentPid(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // No hay API directa simple; usamos WMI vía ManagementObjectSearcher sería pesado.
            // Heurística ligera: si no podemos resolver el padre, devolvemos null (degrada a "no foreground").
            return null;
        }
        catch { return null; }
    }
}
```

> NOTA: la resolución del PID padre por BCL puro es limitada; este detector degrada con elegancia (peor caso: algún aviso de más, nunca de menos), como recoge el spec en "Riesgos conocidos". Si en implementación se quiere precisión, añadir una consulta a `NtQueryInformationProcess`/WMI en `ParentPid`; queda fuera del alcance mínimo de esta tarea.

- [ ] **Step 4: Ejecutar los tests**

Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**

```bash
git add Services/Session/ForegroundDetector.cs ClaudeBarWin.Tests/ForegroundDetectorTests.cs
git commit -m "feat: ForegroundDetector (supresion por foco, degrada con elegancia)"
```

---

### Task 10: El hook PowerShell + recurso embebido

**Files:**
- Create: `hooks/claudebar-hook.ps1`
- Modify: `ClaudeBarWin.csproj` (embeber el .ps1 como recurso)
- Create: `Services/Hooks/HookScript.cs` (lee el recurso embebido)

- [ ] **Step 1: Escribir el script del hook**

Create `hooks/claudebar-hook.ps1`:
```powershell
# ClaudeBar live-sessions hook. Lee el evento de Claude Code de stdin (JSON) y lo
# reenvia como una linea JSON al Named Pipe \\.\pipe\claudebar. Fire-and-forget:
# si ClaudeBar no esta corriendo (pipe inexistente) o tarda, sale en silencio (exit 0)
# y NUNCA bloquea ni rompe la sesion de Claude. No escribe a stdout/stderr ni devuelve decision.
try {
  $raw = [Console]::In.ReadToEnd()
  if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
  $in = $raw | ConvertFrom-Json

  # Derivar status a partir del nombre del evento del hook.
  $ev = "$($in.hook_event_name)"
  switch ($ev) {
    'PreToolUse'        { $status = 'running_tool' }
    'PostToolUse'       { $status = 'processing' }
    'PermissionRequest' { $status = 'waiting_for_approval' }
    'Notification'      { $status = 'waiting_for_input' }
    'Stop'              { $status = 'waiting_for_input' }
    'SubagentStop'      { $status = 'processing' }
    'UserPromptSubmit'  { $status = 'processing' }
    'PreCompact'        { $status = 'compacting' }
    'SessionStart'      { $status = 'starting' }
    'SessionEnd'        { $status = 'ended' }
    default             { $status = 'processing' }
  }

  $payload = [ordered]@{
    session_id  = "$($in.session_id)"
    cwd         = "$($in.cwd)"
    pid         = $PID
    event       = $ev
    status      = $status
    tool        = "$($in.tool_name)"
    tool_use_id = "$($in.tool_use_id)"
    ts          = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
  }
  $json = ($payload | ConvertTo-Json -Compress)

  $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'claudebar', [System.IO.Pipes.PipeDirection]::Out)
  $pipe.Connect(200)   # timeout corto; si no hay servidor, lanza y caemos al catch
  $sw = New-Object System.IO.StreamWriter($pipe)
  $sw.AutoFlush = $true
  $sw.WriteLine($json)
  $sw.Dispose()
  $pipe.Dispose()
} catch {
  # ClaudeBar cerrado / pipe ocupado / timeout: ignorar por completo.
}
exit 0
```

- [ ] **Step 2: Embeber el script como recurso**

En `ClaudeBarWin.csproj`, dentro de un `<ItemGroup>` nuevo (tras el ItemGroup de `InternalsVisibleTo` de Task 0), añadir:
```xml
  <ItemGroup>
    <EmbeddedResource Include="hooks\claudebar-hook.ps1" />
  </ItemGroup>
```

- [ ] **Step 3: Lector del recurso embebido**

Create `Services/Hooks/HookScript.cs`:
```csharp
using System.Reflection;

namespace ClaudeBarWin.Services;

/// <summary>Acceso al contenido del hook PowerShell embebido como recurso.</summary>
public static class HookScript
{
    /// <summary>Devuelve el contenido de hooks/claudebar-hook.ps1 embebido en el assembly.</summary>
    public static string Contents()
    {
        var asm = Assembly.GetExecutingAssembly();
        // El nombre del recurso es "<RootNamespace>.hooks.claudebar-hook.ps1" => "ClaudeBarWin.hooks.claudebar-hook.ps1"
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("claudebar-hook.ps1", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Recurso claudebar-hook.ps1 no embebido.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 4: Compilar la app (verifica que el recurso se embebe)**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal
```
Expected: "Compilación correcta. 0 Errores".

- [ ] **Step 5: Commit**

```bash
git add hooks/claudebar-hook.ps1 Services/Hooks/HookScript.cs ClaudeBarWin.csproj
git commit -m "feat: hook PowerShell (fire-and-forget al pipe) embebido como recurso"
```

---

### Task 11: Strings de localización

**Files:**
- Modify: `Services/Localization.cs`

- [ ] **Step 1: Añadir las propiedades a la clase `Strings`**

En `Services/Localization.cs`, dentro de la clase `Strings`, justo antes de la última propiedad `public string Changelog { get; init; } = "What's new";` (línea 114), añadir:
```csharp
    // Live sessions / Mascot
    public string MenuLiveSessions { get; init; } = "Live sessions";
    public string MenuShowMascot { get; init; } = "Show mascot";
    public string MenuSuppressWhenFocused { get; init; } = "Mute when terminal focused";
    public string MenuInstallHooks { get; init; } = "Enable (install hooks)…";
    public string MenuUninstallHooks { get; init; } = "Disable (remove hooks)";
    public string NoActiveSessions { get; init; } = "No active sessions";
    public string SessionPhaseIdle { get; init; } = "idle";
    public string SessionPhaseProcessing { get; init; } = "working";
    public string SessionPhaseWaitingApproval { get; init; } = "waiting for OK";
    public string SessionPhaseWaitingInput { get; init; } = "your turn";
    public string SessionPhaseCompacting { get; init; } = "compacting";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingApprovalFmt { get; init; } = "Claude is waiting for your OK in {0}";
    /// <summary>{0} = nombre del proyecto.</summary>
    public string NotifWaitingInputFmt { get; init; } = "Claude finished in {0}";
    public string LiveSessionsTitle { get; init; } = "Claude sessions";
    /// <summary>{0} = ruta del backup de settings.json.</summary>
    public string HooksInstalledFmt { get; init; } = "Live sessions on. Backup: {0}";
    public string HooksRemoved { get; init; } = "Live sessions off. Hooks removed.";
```

- [ ] **Step 2: Añadir las traducciones en español**

En el inicializador `Spanish` (líneas 170-256), antes de `Changelog = "..."` (línea 255), añadir:
```csharp
        MenuLiveSessions = "Sesiones en vivo",
        MenuShowMascot = "Mostrar mascota",
        MenuSuppressWhenFocused = "Silenciar si la terminal tiene foco",
        MenuInstallHooks = "Activar (instalar hooks)…",
        MenuUninstallHooks = "Desactivar (quitar hooks)",
        NoActiveSessions = "Sin sesiones activas",
        SessionPhaseIdle = "en reposo",
        SessionPhaseProcessing = "trabajando",
        SessionPhaseWaitingApproval = "espera tu OK",
        SessionPhaseWaitingInput = "tu turno",
        SessionPhaseCompacting = "compactando",
        NotifWaitingApprovalFmt = "Claude espera tu OK en {0}",
        NotifWaitingInputFmt = "Claude terminó en {0}",
        LiveSessionsTitle = "Sesiones de Claude",
        HooksInstalledFmt = "Sesiones en vivo activadas. Backup: {0}",
        HooksRemoved = "Sesiones en vivo desactivadas. Hooks quitados.",
```

- [ ] **Step 3: Añadir las traducciones en los otros 6 idiomas**

En cada inicializador (`Dutch` 258-344, `French` 346-432, `German` 434-520, `Japanese` 522-608, `Korean` 610-696, `TradChinese` 698-784), antes de su `Changelog = "..."` final, añadir el mismo bloque de claves con la traducción correspondiente. Para idiomas donde no se quiera traducir aún, **omitir esas claves** es válido: caerán al default inglés automáticamente (fallback estructural). Mínimo obligatorio: traducir `NotifWaitingApprovalFmt`, `MenuLiveSessions`, `NoActiveSessions` y los `SessionPhase*` en los 6; el resto puede quedar en inglés. Mantener `{0}` idéntico.

Ejemplo (francés) a insertar antes de `Changelog`:
```csharp
        MenuLiveSessions = "Sessions en direct",
        MenuShowMascot = "Afficher la mascotte",
        MenuSuppressWhenFocused = "Muet si le terminal a le focus",
        NoActiveSessions = "Aucune session active",
        SessionPhaseIdle = "au repos",
        SessionPhaseProcessing = "en cours",
        SessionPhaseWaitingApproval = "attend votre OK",
        SessionPhaseWaitingInput = "à vous",
        SessionPhaseCompacting = "compactage",
        NotifWaitingApprovalFmt = "Claude attend votre OK dans {0}",
        NotifWaitingInputFmt = "Claude a terminé dans {0}",
```
(Para de/nl/ja/ko/zh-Hant, traducir análogamente; consultar context7/diccionario para los CJK. No bloquea: lo no traducido cae a inglés.)

- [ ] **Step 4: Compilar**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores".

- [ ] **Step 5: Commit**

```bash
git add Services/Localization.cs
git commit -m "i18n: strings de sesiones en vivo / mascota / avisos (es completo, resto fallback EN)"
```

---

### Task 12: Badge ámbar en `TrayIconRenderer`

**Files:**
- Modify: `UI/TrayIconRenderer.cs`

- [ ] **Step 1: Añadir el parámetro `pending` y el dibujo del badge**

En `UI/TrayIconRenderer.cs`:

(a) Cambiar la firma de `Render` (línea 22) y `RenderError` (línea 29) para aceptar un flag opcional, y propagarlo a `RenderBadge`:
```csharp
    public static Icon Render(int percent, Color bg, bool pending = false)
    {
        int clamped = Math.Clamp(percent, 0, 999);
        string text = clamped >= 100 ? "99+" : clamped.ToString();
        return RenderBadge(text, bg, pending);
    }
```
```csharp
    public static Icon RenderError(Color bg, bool pending = false) => RenderBadge("!", bg, pending);
```

(b) Cambiar la firma de `RenderBadge` (línea 31) a `private static Icon RenderBadge(string text, Color bg, bool pending)` y, dentro del bloque `using (var g = Graphics.FromImage(bmp))`, DESPUÉS del `g.DrawString(...)` (línea 51) y antes de cerrar el `using`, añadir:
```csharp
            if (pending)
            {
                var amber = Color.FromArgb(0xF5, 0xA6, 0x23);
                int d = 12;
                var badge = new Rectangle(size - d - 1, 0, d, d);
                using var fill = new SolidBrush(amber);
                using var ring = new Pen(Color.FromArgb(0x1A, 0x1A, 0x1A), 1.5f);
                g.FillEllipse(fill, badge);
                g.DrawEllipse(ring, badge);
            }
```
(`size` es la const local = 32, definida en línea 33. El badge va en la esquina superior derecha; no choca con el texto centrado ni con el "99+".)

- [ ] **Step 2: Compilar**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores". (Los callers existentes siguen compilando porque `pending` es opcional con default `false`.)

- [ ] **Step 3: Verificación visual rápida (opcional, manual)**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" run --project "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -- --render-test
```
Expected: genera PNGs del icono en `%TEMP%\claudebar-render`. (El badge se verá en el render una vez Task 14 pase `pending:true`; aquí solo se confirma que `--render-test` sigue funcionando.)

- [ ] **Step 4: Commit**

```bash
git add UI/TrayIconRenderer.cs
git commit -m "feat: badge ambar opcional en el icono de bandeja (pending)"
```

---

### Task 13: Sección mascota + lista de instancias en `DashboardForm`

**Files:**
- Modify: `UI/DashboardForm.cs`

El form recibe la vista por un provider sincrónico cacheado (patrón `SetHistoryProvider`). El `_tick` de 1s (líneas 83-84) ya repinta; para resize en vivo se llama `OnLiveSessionsChanged()`.

- [ ] **Step 1: Campo provider + cache + setter + Rects + evento**

En `UI/DashboardForm.cs`:

(a) Junto a `_historyProvider`/`_pctProvider` (líneas 53-54), añadir:
```csharp
    private Func<LiveSessionsView>? _liveProvider;
    private LiveSessionsView _liveView = new();
    private int _mascotFrame;
```
(b) Junto a `_tabRects`/`_modeRects`/`_pctWinRects` (líneas 61-63), añadir:
```csharp
    private readonly Dictionary<string, Rectangle> _liveRowRects = new();
```
(c) Junto a los eventos públicos (líneas 65-67), añadir:
```csharp
    public event Action<string>? SessionClicked;
```
(d) Junto a `SetHistoryProvider`/`SetPercentProvider` (líneas 87-88), añadir:
```csharp
    public void SetLiveSessionsProvider(Func<LiveSessionsView> provider) => _liveProvider = provider;
```
(e) Añadir un método público para refrescar+resize al recibir eventos del pipe (cerca de `UpdateData`):
```csharp
    /// <summary>Llamar cuando cambien las sesiones en vivo (desde el hilo de UI vía BeginInvoke).</summary>
    public void OnLiveSessionsChanged()
    {
        if (_liveProvider is not null) _liveView = _liveProvider();
        Relayout();
        Invalidate();
    }
```
(f) Asegurar que el import del modelo está disponible: en la cabecera de usings del archivo, añadir `using ClaudeBarWin.Models;` si no está (con ImplicitUsings puede faltar el de Models).

- [ ] **Step 2: Avanzar el frame de animación en el tick**

El `_tick` ya hace `Invalidate()` cada 1s. Para animar la mascota, en el handler del tick (líneas 83-84) cambiar a:
```csharp
        _tick.Tick += (_, _) => { if (Visible) { _mascotFrame++; Invalidate(); } };
```

- [ ] **Step 3: Dibujar la sección dentro de `LayoutContent`**

En `LayoutContent` (líneas 345-451), tras el bloque de gasto (que termina ~línea 424) y antes del `if (_cfg.ShowChart)` (~línea 426), insertar:
```csharp
            if (_cfg.LiveSessionsEnabled)
            {
                y += 8;
                y = DrawLiveSessions(g, draw, x, y, contentW, smallFont, fg, dim);
            }
            else { _liveRowRects.Clear(); }
```
(Usar el mismo nombre de ancho que usan las otras secciones; en el mapa es el `w`/`contentW` que reciben los `DrawXxx`. Ajustar al identificador real de la línea 408-424.)

- [ ] **Step 4: Implementar `DrawLiveSessions`**

Tras `DrawSpendBody` (termina línea 681) o junto a los demás `Draw*`, añadir:
```csharp
    private int DrawLiveSessions(Graphics g, bool draw, int x, int y, int w, Font smallFont, Brush fg, Brush dim)
    {
        _liveRowRects.Clear();
        var view = _liveView;

        // Cabecera de sección
        if (draw)
            g.DrawString(_s.LiveSessionsTitle, smallFont, dim, x, y);
        y += 18;

        // Mascota (si está activada)
        if (_cfg.ShowMascot)
        {
            var frames = MascotSprite.Frames(view.GlobalPhase);
            var frame = frames[_mascotFrame % frames.Count];
            var label = PhaseLabel(view.GlobalPhase);
            using var mono = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Point);
            if (draw)
            {
                using var accent = new SolidBrush(PhaseColor(view.GlobalPhase));
                g.DrawString(frame, mono, accent, x, y);
                g.DrawString(label, smallFont, dim, x + 110, y + 2);
            }
            y += 22;
        }

        // Lista de instancias
        if (view.Instances.Count == 0)
        {
            if (draw) g.DrawString(_s.NoActiveSessions, smallFont, dim, x, y);
            y += 18;
        }
        else
        {
            foreach (var s in view.Instances)
            {
                var rect = new Rectangle(x, y, w, 16);
                if (draw)
                {
                    g.DrawString(s.ProjectName, smallFont, fg, x, y);
                    var st = PhaseLabel(s.Phase);
                    var size = g.MeasureString(st, smallFont);
                    g.DrawString(st, smallFont, dim, x + w - size.Width, y);
                }
                _liveRowRects[s.SessionId] = rect;
                y += 18;
            }
        }
        return y;
    }

    private string PhaseLabel(SessionPhase p) => p switch
    {
        SessionPhase.Idle => _s.SessionPhaseIdle,
        SessionPhase.Processing => _s.SessionPhaseProcessing,
        SessionPhase.WaitingForApproval => _s.SessionPhaseWaitingApproval,
        SessionPhase.WaitingForInput => _s.SessionPhaseWaitingInput,
        SessionPhase.Compacting => _s.SessionPhaseCompacting,
        _ => _s.SessionPhaseIdle,
    };

    private Color PhaseColor(SessionPhase p) => p switch
    {
        SessionPhase.WaitingForApproval => _theme.Warn,
        SessionPhase.Processing => _theme.Ok,
        SessionPhase.Compacting => _theme.Ok,
        SessionPhase.WaitingForInput => _theme.Warn,
        _ => _theme.Dim,
    };
```
(Si `_theme` no expone `Dim`/`Ok`/`Warn` con esos nombres exactos, usar los reales del mapa de DashboardForm: paleta `_theme.Background/Foreground/Dim/Track/Ok/Warn/Critical`.)

- [ ] **Step 5: Hit-testing de las filas en `OnMouseDown`**

En `OnMouseDown` (líneas 253-302), ANTES del fallback `_dragging = true;` (~línea 299), añadir:
```csharp
            foreach (var (id, rect) in _liveRowRects)
            {
                if (rect.Contains(e.Location)) { SessionClicked?.Invoke(id); return; }
            }
```
Y en `OnMouseMove` (líneas 304-318), añadir al OR de `overClickable`:
```csharp
                || _liveRowRects.Values.Any(r => r.Contains(e.Location))
```

- [ ] **Step 6: Poblar `_liveView` en `UpdateData` (para el primer pintado)**

En `UpdateData` (líneas 98-119), tras asignar `_cfg`, añadir:
```csharp
        if (_cfg.LiveSessionsEnabled && _liveProvider is not null) _liveView = _liveProvider();
```

- [ ] **Step 7: Compilar**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores".

- [ ] **Step 8: Commit**

```bash
git add UI/DashboardForm.cs
git commit -m "feat: seccion mascota + lista de instancias en el dashboard (provider + hit-test)"
```

---

### Task 14: Cablear todo en `TrayAppContext`

**Files:**
- Modify: `TrayAppContext.cs`

- [ ] **Step 1: Campos de servicios**

En el bloque de campos de servicios (líneas 17-25), añadir:
```csharp
    private readonly SessionStore _sessions;
    private readonly SessionAggregator _sessionAgg;
    private readonly ForegroundDetector _foreground;
    private HookPipeServer? _pipe;
```
Y en el bloque de toggles `_miXxx` (líneas 35-45), añadir:
```csharp
    private ToolStripMenuItem _miLiveSessions = null!;
    private ToolStripMenuItem _miShowMascot = null!;
    private ToolStripMenuItem _miSuppressFocused = null!;
    private ToolStripMenuItem _miInstallHooks = null!;
```

- [ ] **Step 2: Instanciar en el ctor y cablear el aggregator → UI**

En el ctor (tras crear `_dashboard` y forzar `_ = _dashboard.Handle`, ~línea 83), añadir:
```csharp
        _sessions = new SessionStore();
        _sessionAgg = new SessionAggregator();
        _foreground = new ForegroundDetector();
        _dashboard.SetLiveSessionsProvider(() => _sessionAgg.BuildView(_sessions.Snapshot()));

        _sessions.Changed += OnSessionsChanged;

        if (_config.LiveSessionsEnabled) StartPipe();
```
Donde `StartPipe` y `OnSessionsChanged` se definen como:
```csharp
    private void StartPipe()
    {
        if (_pipe is not null) return;
        _pipe = new HookPipeServer();
        _pipe.EventReceived += e => _sessions.Apply(e, DateTime.UtcNow);
        _pipe.Start();
    }

    private void StopPipe()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    private void OnSessionsChanged()
    {
        // Prune perezoso (cada cambio comprobamos TTL de 10 min).
        _sessions.Prune(DateTime.UtcNow, TimeSpan.FromMinutes(10));

        // Avisos: diff + supresión por foco.
        var snap = _sessions.Snapshot();
        foreach (var s in _sessionAgg.DiffNotifications(snap, DateTime.UtcNow))
        {
            if (_config.SuppressWhenFocused && _foreground.IsSessionForeground(s.Pid)) continue;
            NotifySession(s);
        }

        // Refrescar mascota/lista + icono en el hilo de UI.
        try { _dashboard.BeginInvoke(new Action(() =>
        {
            _dashboard.OnLiveSessionsChanged();
            RefreshTrayIcon();
        })); } catch { }
    }

    private void NotifySession(Models.SessionState s)
    {
        if (!_config.NotificationsEnabled) return;
        var fmt = s.Phase == Models.SessionPhase.WaitingForApproval
            ? _s.NotifWaitingApprovalFmt : _s.NotifWaitingInputFmt;
        var icon = s.Phase == Models.SessionPhase.WaitingForApproval
            ? ToolTipIcon.Warning : ToolTipIcon.Info;
        try { _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(fmt, s.ProjectName), icon); } catch { }
    }
```

- [ ] **Step 3: Badge en el icono (push + en `UpdateUi`)**

Añadir un helper que re-renderiza el icono con el badge según la fase global, reutilizando el último snapshot de cuota:
```csharp
    private bool LiveAttentionPending()
        => _config.LiveSessionsEnabled
           && _sessionAgg.BuildView(_sessions.Snapshot()).GlobalPhase.NeedsAttention();

    private void RefreshTrayIcon()
    {
        if (_lastSnapshot is null || _lastUsage is null) return;
        UpdateUi(_lastSnapshot); // recalcula icono; UpdateUi ya pinta con _lastSnapshot
    }
```
Y en `UpdateUi` (líneas 539-569), en la rama que construye el icono normal (línea 545, `TrayIconRenderer.Render(icoVal, icoColor)`), pasar el flag:
```csharp
            var newIcon = TrayIconRenderer.Render(icoVal, icoColor, pending: LiveAttentionPending());
```
y en la rama de error (línea 559, `TrayIconRenderer.RenderError(_theme.Neutral)`):
```csharp
            var newIcon = TrayIconRenderer.RenderError(_theme.Neutral, pending: LiveAttentionPending());
```
(Mantener el resto de `UpdateUi` igual; `LiveAttentionPending()` devuelve `false` si la feature está off, así que el comportamiento actual no cambia.)

- [ ] **Step 4: Submenú "Sesiones en vivo" en `BuildMenu`**

En `BuildMenu` (líneas 194-374), tras el bloque "Sections" (~línea 273) y antes del separador de ~línea 349, añadir:
```csharp
        var live = Sub(_s.MenuLiveSessions);
        _miInstallHooks = new ToolStripMenuItem(_s.MenuInstallHooks);
        _miInstallHooks.Click += (_, _) => ToggleHooks();
        _miLiveSessions = new ToolStripMenuItem(_s.MenuLiveSessions);
        _miLiveSessions.Click += (_, _) => MutateConfig(c => c.LiveSessionsEnabled = !c.LiveSessionsEnabled);
        _miShowMascot = new ToolStripMenuItem(_s.MenuShowMascot);
        _miShowMascot.Click += (_, _) => MutateConfig(c => c.ShowMascot = !c.ShowMascot);
        _miSuppressFocused = new ToolStripMenuItem(_s.MenuSuppressWhenFocused);
        _miSuppressFocused.Click += (_, _) => MutateConfig(c => c.SuppressWhenFocused = !c.SuppressWhenFocused);
        live.DropDownItems.Add(_miInstallHooks);
        live.DropDownItems.Add(new ToolStripSeparator());
        live.DropDownItems.Add(_miLiveSessions);
        live.DropDownItems.Add(_miShowMascot);
        live.DropDownItems.Add(_miSuppressFocused);
        menu.Items.Add(live);
```

- [ ] **Step 5: Estado checked en `UpdateMenuChecks`**

En `UpdateMenuChecks` (líneas 376-417), tras `var c = AppConfig.Load();` y junto a los demás, añadir:
```csharp
        _miLiveSessions.Checked = c.LiveSessionsEnabled;
        _miShowMascot.Checked = c.ShowMascot;
        _miSuppressFocused.Checked = c.SuppressWhenFocused;
        _miShowMascot.Enabled = c.LiveSessionsEnabled;
        _miSuppressFocused.Enabled = c.LiveSessionsEnabled;
        _miInstallHooks.Text = HookInstaller.IsInstalled() ? _s.MenuUninstallHooks : _s.MenuInstallHooks;
```

- [ ] **Step 6: `ToggleHooks` (instala/desinstala con OK + arranca/para el pipe)**

Añadir el método (junto a `ImportItermColors`, ~línea 419):
```csharp
    private void ToggleHooks()
    {
        if (HookInstaller.IsInstalled())
        {
            HookInstaller.Uninstall(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            MutateConfig(c => c.LiveSessionsEnabled = false);
            StopPipe();
            _tray.ShowBalloonTip(4000, _s.LiveSessionsTitle, _s.HooksRemoved, ToolTipIcon.Info);
            return;
        }

        var ok = MessageBox.Show(
            "ClaudeBar va a modificar ~/.claude/settings.json para recibir eventos de tus sesiones de Claude Code. Se hará una copia de seguridad antes. ¿Continuar?",
            _s.MenuLiveSessions, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (ok != DialogResult.OK) return;

        var backup = HookInstaller.Install(HookScript.Contents(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        MutateConfig(c => c.LiveSessionsEnabled = true);
        StartPipe();
        _tray.ShowBalloonTip(5000, _s.LiveSessionsTitle, string.Format(_s.HooksInstalledFmt, backup), ToolTipIcon.Info);
    }
```

- [ ] **Step 7: Arrancar/parar el pipe al togglear `LiveSessionsEnabled` por menú**

En `MutateConfig` (líneas 450-466), tras la reasignación de `_config` (~línea 464) y antes de `_ = RefreshAsync()`, añadir:
```csharp
        if (_config.LiveSessionsEnabled && _pipe is null) StartPipe();
        else if (!_config.LiveSessionsEnabled && _pipe is not null) StopPipe();
```

- [ ] **Step 8: Liberar en `ExitApp` y `Dispose`**

En `ExitApp` (líneas 755-764) y en `Dispose(bool)` (líneas 766-777), junto al dispose de `_showSignal`, añadir:
```csharp
        _pipe?.Dispose();
```

- [ ] **Step 9: Compilar**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores".

- [ ] **Step 10: Commit**

```bash
git add TrayAppContext.cs
git commit -m "feat: cablear sesiones en vivo en el tray (pipe, avisos, badge, menu, hooks opt-in)"
```

---

### Task 15: Modo CLI `--hook-test`

**Files:**
- Modify: `Program.cs`

Inyecta eventos sintéticos al pipe para validar la cadena completa sin abrir Claude Code real.

- [ ] **Step 1: Añadir el guard en `Main`**

En `Program.cs`, tras el guard `--notify-demo` (~línea 50) y antes de `ApplicationConfiguration.Initialize();` (línea 52), añadir:
```csharp
        if (args.Contains("--hook-test")) { RunHookTest(); return; }
```

- [ ] **Step 2: Implementar `RunHookTest`**

Tras `RunDbTest` (~línea 88), añadir:
```csharp
    private static void RunHookTest()
    {
        // Requiere una instancia de ClaudeBar corriendo con sesiones en vivo activadas.
        var seq = new (string ev, string status, string tool)[]
        {
            ("SessionStart", "starting", ""),
            ("PreToolUse", "running_tool", "Bash"),
            ("PermissionRequest", "waiting_for_approval", "Write"),
            ("PostToolUse", "processing", "Write"),
            ("Stop", "waiting_for_input", ""),
        };
        foreach (var (ev, status, tool) in seq)
        {
            var json = $$"""{"session_id":"hook-test","cwd":"C:\\Users\\zorro\\Proyectos\\demo","pid":{{Environment.ProcessId}},"event":"{{ev}}","status":"{{status}}","tool":"{{tool}}","ts":0}""";
            try
            {
                using var c = new System.IO.Pipes.NamedPipeClientStream(".", "claudebar", System.IO.Pipes.PipeDirection.Out);
                c.Connect(500);
                using var w = new StreamWriter(c) { AutoFlush = true };
                w.WriteLine(json);
                Console.WriteLine($"sent {ev}/{status}");
            }
            catch (Exception ex) { Console.WriteLine($"FAIL {ev}: {ex.Message} (¿está ClaudeBar corriendo con sesiones en vivo ON?)"); }
            System.Threading.Thread.Sleep(1500);
        }
    }
```

- [ ] **Step 3: Compilar**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores".

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "feat: modo CLI --hook-test (inyecta eventos sinteticos al pipe)"
```

---

### Task 16: Verificación end-to-end + suite completa

**Files:** ninguno (validación).

- [ ] **Step 1: Suite de tests completa**

Run:
```powershell
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.sln" --nologo -v minimal
```
Expected: "Passed! - Failed: 0" con todos los tests de las Tasks 1-8.

- [ ] **Step 2: Build Release de la app**

Run el comando de build de Task 10 Step 4. Expected: "0 Errores".

- [ ] **Step 3: Prueba viva del pipe (dos terminales)**

1. Lanzar la app: `dotnet run --project ClaudeBarWin.csproj` (o el exe). Activar **menú → Sesiones en vivo → Activar (instalar hooks)** y aceptar el diálogo. Verificar que aparece el globo "Sesiones en vivo activadas. Backup: …" y que existe `~/.claude/settings.json.claudebar-bak-<stamp>`.
2. En otra terminal: `dotnet run --project ClaudeBarWin.csproj -- --hook-test`.
3. Abrir el dashboard (clic en el icono): la mascota debe pasar por trabajando → espera tu OK → tu turno, la lista debe mostrar "demo" con su estado, y al llegar `waiting_for_approval` debe salir el globo "Claude espera tu OK en demo" + badge ámbar en el icono.

- [ ] **Step 4: Verificar preservación de settings.json**

Run:
```powershell
Get-Content "$env:USERPROFILE\.claude\settings.json" | Select-String "claudebar-hook.ps1","SessionStart"
```
Expected: aparece nuestra entrada Y los hooks previos (SessionStart del Asistente) siguen presentes.

- [ ] **Step 5: Desactivar y verificar limpieza**

Menú → Sesiones en vivo → "Desactivar (quitar hooks)". Verificar que `settings.json` ya NO contiene `claudebar-hook.ps1` pero conserva el resto, y que el Asistente 24/7 sigue arrancando con sus hooks (revisar que `SessionStart` propio sigue ahí).

- [ ] **Step 6: Commit final (si quedó algún ajuste)**

```bash
git add -A
git commit -m "test: verificacion end-to-end de sesiones en vivo (pipe, avisos, badge, settings.json intacto)"
```

---

## Self-Review

**Cobertura del spec:**
- Hook PowerShell fire-and-forget → Task 10 ✓
- Named Pipe server → Task 8 ✓
- SessionStore + máquina de estados + TTL → Tasks 1, 3 ✓
- SessionAggregator (fase global + orden + diffing/seeding) → Task 4 ✓
- Supresión por foco → Task 9 + cableado Task 14 ✓
- Mascota ASCII 1 bicho 6 estados → Task 5 (frames) + Task 13 (dibujo) ✓
- Lista de instancias en dashboard → Task 13 ✓
- Badge ámbar en bandeja → Task 12 + Task 14 ✓
- Globo nativo "espera tu OK" → Task 14 (NotifySession) ✓
- Config (4 props, opt-in) → Task 6 ✓
- Localización → Task 11 ✓
- Instalación opt-in con backup + idempotente + uninstall → Task 7 + Task 14 (ToggleHooks) ✓
- `--hook-test` → Task 15 ✓
- Proyecto de tests (primer test suite) → Task 0 ✓
- NO Telegram, NO approve/reject, NO JSONL chat, NO gacha → respetado (no aparece ninguno) ✓

**Placeholder scan:** los pasos sobre archivos no leídos al 100% (DashboardForm `contentW`, TrayAppContext `_theme.Neutral`, líneas exactas) llevan nota explícita de "ajustar al identificador real del mapa"; no son placeholders de lógica sino puntos de anclaje que el implementador confirma contra el archivo (los mapas dan las líneas). El arte de la mascota es placeholder acordado con el usuario. `MascotSprite.LabelKey` tiene una corrección inline (devolver `phase.ToString()`).

**Type consistency:** `SessionPhase` (Task 1) se usa idéntico en Tasks 2-5, 13, 14. `HookEvent.Parse`/`ToPhase` (Task 2) usados en Tasks 3, 8, 15. `SessionStore.Apply(HookEvent, DateTime)`/`Snapshot()`/`Prune(DateTime, TimeSpan)`/`Changed` (Task 3) usados en Task 14. `SessionAggregator.BuildView`/`DiffNotifications` (Task 4) usados en Tasks 13, 14. `LiveSessionsView.GlobalPhase`/`Instances` (Task 4) usados en Task 13. `HookInstaller.Install/Uninstall/IsInstalled/MergeSettings/RemoveHooks/HookCommand` (Task 7) usados en Task 14. `HookPipeServer(string)`/`Start`/`EventReceived`/`Dispose` (Task 8) usados en Task 14. `HookScript.Contents()` (Task 10) usado en Task 14. `TrayIconRenderer.Render(int,Color,bool)` (Task 12) usado en Task 14. `MascotSprite.Frames` (Task 5) usado en Task 13. `DashboardForm.SetLiveSessionsProvider`/`OnLiveSessionsChanged`/`SessionClicked` (Task 13) usados en Task 14. Consistente.

**Riesgo abierto documentado:** `ForegroundDetector.ParentPid` degrada (sin WMI) — recogido en el spec; el peor caso es algún aviso de más. El nombre real de algunos miembros de `_theme`/anchos de `LayoutContent` se confirma contra el archivo en el momento (los mapas dan las líneas exactas).

