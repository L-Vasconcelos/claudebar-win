# Rediseño dashboard + panel de ajustes (v0.3) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recomponer el dashboard de ClaudeBar (jerarquía visual, cabecera "de un vistazo", secciones plegables, reordenado por prioridad) y mover toda la configuración a un **panel de ajustes ⚙ dentro del dashboard**, con la mascota agrandada a dos tamaños (6×6/8×8) elegibles — todo integrado con las sesiones en vivo, en `feat/live-sessions`.

**Architecture:** `DashboardForm` (monolito custom-draw de ~891 líneas) gana un **modo de vista** (`Data`|`Settings`) y se **divide** en renderers sin estado bajo `UI/dashboard/`: `DashboardHeader`, `DashboardDataView`, `DashboardSettingsView`, `MascotRenderer`. Cada renderer respeta el contrato del repo: `int Draw(Graphics g, bool draw, int x, int y, int w, ...)` que avanza/devuelve `y` idéntico en `draw=false`/`draw=true` y registra `Rectangle`s clicables. El menú click-derecho se reduce a acciones; el panel reusa `MutateConfig` vía un callback.

**Tech Stack:** C#/.NET 9 WinForms custom-draw (GDI+), xUnit (proyecto `ClaudeBarWin.Tests` ya existe). Sin paquetes nuevos.

**Build:** `$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; & "$env:USERPROFILE\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" -c Release --nologo -v minimal`
**Test:** `… test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.sln" --nologo -v minimal`

**Convenciones del repo (de los mapas):** namespaces por carpeta (`ClaudeBarWin.UI`, `.Services`, `.Config`, `.Models`); `Nullable`+`ImplicitUsings` ON; tests con `global using Xunit` ya configurado. Paleta en `Theme` (`Background/Foreground/Dim/Track/Ok/Warn/Critical/Neutral` + `StatusColor(theme, UsageStatus)`; `Neutral` para idle). Cada `DrawXxx` mantiene simetría medir/pintar. Hit-test central en `OnMouseDown` contra `Rectangle`s cacheados, **antes** del fallback de drag.

**Naturaleza refactor:** varias tareas EXTRAEN métodos existentes de `DashboardForm.cs` a renderers nuevos preservando su cuerpo. En esos pasos, el implementador **debe leer `UI/DashboardForm.cs`** para copiar el cuerpo real del método (las firmas están en el plan; los cuerpos viven en el archivo). No se reescribe la lógica de dibujo, se reubica.

---

## Estructura de archivos

**Nuevos:**
- `UI/dashboard/MascotRenderer.cs` — dibuja el bloque ASCII de la mascota (tamaño + color por fase).
- `UI/dashboard/DashboardHeader.cs` — cabecera "de un vistazo".
- `UI/dashboard/DashboardDataView.cs` — secciones plegables de datos.
- `UI/dashboard/DashboardSettingsView.cs` — panel de ajustes.
- Tests: `ClaudeBarWin.Tests/MascotSizeTests.cs`, `DashboardConfigTests.cs`.

**Modificados:**
- `Config/AppConfig.cs` — `MascotSize` + `Collapsed*`.
- `Services/Mascot/MascotSprite.cs` — `enum MascotSize` + `Frames(phase, size)`.
- `UI/DashboardForm.cs` — modo de vista + delegación a renderers + hit-test + `SettingsChanged`.
- `TrayAppContext.cs` — menú minimal + `SettingsChanged`→`MutateConfig`.
- `Services/Localization.cs` — strings del panel.
- `Program.cs` — `--render-test` extendido a ambas vistas.

---

### Task 1: Config — `MascotSize` + plegado de secciones

**Files:**
- Modify: `Config/AppConfig.cs` (tras las 4 props de live-sessions añadidas antes, ~línea 60)
- Test: `ClaudeBarWin.Tests/DashboardConfigTests.cs`

- [ ] **Step 1: Test que falla**

Create `ClaudeBarWin.Tests/DashboardConfigTests.cs`:
```csharp
using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

public class DashboardConfigTests
{
    [Fact]
    public void Defaults_quota_and_sessions_expanded_spend_and_chart_collapsed()
    {
        var c = new AppConfig();
        Assert.False(c.CollapsedQuota);
        Assert.False(c.CollapsedSessions);
        Assert.True(c.CollapsedSpend);
        Assert.True(c.CollapsedChart);
        Assert.Equal("compact", c.MascotSize);
    }

    [Fact]
    public void Collapsed_and_mascotsize_roundtrip_json()
    {
        var c = new AppConfig { CollapsedQuota = true, CollapsedChart = false, MascotSize = "large" };
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;
        Assert.True(back.CollapsedQuota);
        Assert.False(back.CollapsedChart);
        Assert.Equal("large", back.MascotSize);
    }
}
```

- [ ] **Step 2: Ejecutar — falla de compilación** (`CollapsedQuota`/`MascotSize` no existen).
Run el comando de test.

- [ ] **Step 3: Añadir las propiedades**

En `Config/AppConfig.cs`, tras `public string MascotKind { get; set; } = "cat";` (la última prop de live-sessions), añadir:
```csharp

    // Dashboard layout (v0.3)
    /// <summary>Tamaño de la mascota en la cabecera: "compact" (6×6) o "large" (8×8).</summary>
    public string MascotSize { get; set; } = "compact";
    /// <summary>Sección Cuota plegada en el dashboard.</summary>
    public bool CollapsedQuota { get; set; } = false;
    /// <summary>Sección Sesiones plegada.</summary>
    public bool CollapsedSessions { get; set; } = false;
    /// <summary>Sección Gasto plegada (por defecto sí, para un panel compacto).</summary>
    public bool CollapsedSpend { get; set; } = true;
    /// <summary>Sección Gráfica plegada (por defecto sí).</summary>
    public bool CollapsedChart { get; set; } = true;
```

- [ ] **Step 4: Ejecutar — pasa.** Run el comando de test. Expected: "Passed! - Failed: 0".

- [ ] **Step 5: Commit**
```bash
git add Config/AppConfig.cs ClaudeBarWin.Tests/DashboardConfigTests.cs
git commit -m "feat: config de layout v0.3 (MascotSize + plegado de secciones)"
```

---

### Task 2: `MascotSize` + `MascotSprite.Frames(phase, size)` con arte 6×6 / 8×8

**Files:**
- Modify: `Services/Mascot/MascotSprite.cs`
- Test: `ClaudeBarWin.Tests/MascotSizeTests.cs`

Hoy `MascotSprite.Frames(SessionPhase phase)` devuelve `IReadOnlyList<string>` (1 línea por frame). Se amplía a **bloques multilínea** en dos tamaños. Cada frame pasa a ser `string[]` (varias líneas). Para no romper a los consumidores actuales (DrawLiveSessions usa la versión 1-línea), se mantiene el método viejo delegando al compacto unido por… NO: se migra el consumidor en Task 5/4. Aquí dejamos la API nueva y un shim.

- [ ] **Step 1: Test que falla**

Create `ClaudeBarWin.Tests/MascotSizeTests.cs`:
```csharp
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class MascotSizeTests
{
    [Theory]
    [InlineData(MascotSize.Compact)]
    [InlineData(MascotSize.Large)]
    public void Every_phase_has_nonempty_multiline_frames(MascotSize size)
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            var frames = MascotSprite.Frames(p, size);
            Assert.NotEmpty(frames);
            Assert.All(frames, f => Assert.NotEmpty(f)); // cada frame = array de líneas no vacío
        }
    }

    [Fact]
    public void Large_is_taller_than_compact()
    {
        var c = MascotSprite.Frames(SessionPhase.Idle, MascotSize.Compact)[0];
        var l = MascotSprite.Frames(SessionPhase.Idle, MascotSize.Large)[0];
        Assert.True(l.Length > c.Length); // large tiene más líneas
    }

    [Fact]
    public void Parse_size_falls_back_to_compact()
    {
        Assert.Equal(MascotSize.Compact, MascotSprite.ParseSize("nope"));
        Assert.Equal(MascotSize.Large, MascotSprite.ParseSize("large"));
    }
}
```

- [ ] **Step 2: Ejecutar — falla** (`MascotSize`, `Frames(phase,size)`, `ParseSize` no existen).

- [ ] **Step 3: Reescribir `MascotSprite`**

Reemplazar el contenido de `Services/Mascot/MascotSprite.cs` por (mantiene `Frames(phase)` 1-línea como shim para consumidores antiguos + añade la API por tamaño):
```csharp
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public enum MascotSize { Compact, Large }

/// <summary>
/// Bestiario ASCII propio (clean-room). Frames multilínea por (fase, tamaño).
/// Compact ≈ 4 líneas (6×6), Large ≈ 7 líneas (8×8). El animador cicla frames; idle es estático.
/// </summary>
public static class MascotSprite
{
    public static MascotSize ParseSize(string? s) =>
        string.Equals(s, "large", StringComparison.OrdinalIgnoreCase) ? MascotSize.Large : MascotSize.Compact;

    /// <summary>Frames multilínea (cada frame = varias líneas) por fase y tamaño.</summary>
    public static IReadOnlyList<string[]> Frames(SessionPhase phase, MascotSize size) =>
        size == MascotSize.Large ? Large(phase) : Compact(phase);

    /// <summary>Shim 1-línea para consumidores antiguos (se retirará al migrar el dashboard).</summary>
    public static IReadOnlyList<string> Frames(SessionPhase phase) =>
        Compact(phase).Select(f => f[1].Trim()).ToList();

    public static string LabelKey(SessionPhase phase) => phase.ToString();

    // Gato propio. Compact: 4 líneas. Cara cambia por estado; el cuerpo se mantiene.
    private static IReadOnlyList<string[]> Compact(SessionPhase p)
    {
        string face = p switch
        {
            SessionPhase.Idle => "-.-",
            SessionPhase.Processing => "o.o",
            SessionPhase.WaitingForApproval => "O.O",
            SessionPhase.WaitingForInput => "^.^",
            SessionPhase.Compacting => ">.<",
            SessionPhase.Ended => "x.x",
            _ => "-.-",
        };
        string[] f1 = { " /\\_/\\", $"( {face} )", " > ^ <", " (\")(\")" };
        if (p == SessionPhase.Processing) // parpadeo simple
        {
            string[] f2 = { " /\\_/\\", "( -.o )", " > ^ <", " (\")(\")" };
            return new[] { f1, f2 };
        }
        return new[] { f1 };
    }

    // Large: 7 líneas, gato sentado con cuerpo.
    private static IReadOnlyList<string[]> Large(SessionPhase p)
    {
        string eyes = p switch
        {
            SessionPhase.Idle => "-   -",
            SessionPhase.Processing => "o   o",
            SessionPhase.WaitingForApproval => "O   O",
            SessionPhase.WaitingForInput => "^   ^",
            SessionPhase.Compacting => ">   <",
            SessionPhase.Ended => "x   x",
            _ => "-   -",
        };
        string[] f1 =
        {
            "   /\\_/\\",
            $"  ( {eyes} )",
            "  (  =^=  )",
            "  /|     |\\",
            " ( |     | )",
            "   |     |",
            "   (__|__)",
        };
        if (p == SessionPhase.Processing)
        {
            string[] f2 =
            {
                "   /\\_/\\",
                "  ( o   - )",
                "  (  =^=  )",
                "  /|     |\\",
                " ( |     | )",
                "   |     |",
                "   (__|__)",
            };
            return new[] { f1, f2 };
        }
        return new[] { f1 };
    }
}
```
(Arte propio = placeholder pulible; lo que el test fija es: ≥1 frame por (fase,tamaño), líneas no vacías, large más alto que compact, y `ParseSize`.)

- [ ] **Step 4: Ejecutar — pasa.** Run el comando de test.

- [ ] **Step 5: Compilar la app** (el shim mantiene a `DrawLiveSessions` compilando). Run el build. Expected: 0 errores.

- [ ] **Step 6: Commit**
```bash
git add Services/Mascot/MascotSprite.cs ClaudeBarWin.Tests/MascotSizeTests.cs
git commit -m "feat: MascotSprite con tamaños 6x6/8x8 (Frames(phase,size) + ParseSize)"
```

---

### Task 3: `MascotRenderer` (dibujo del bloque ASCII)

**Files:**
- Create: `UI/dashboard/MascotRenderer.cs`

Renderer sin estado: dibuja un frame de la mascota a una posición, con fuente monoespaciada y color por fase. Devuelve el `Size` ocupado (para que la cabecera lo coloque).

- [ ] **Step 1: Implementar** — Create `UI/dashboard/MascotRenderer.cs`:
```csharp
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>Dibuja la mascota ASCII (tamaño + color por fase). Sin estado.</summary>
public static class MascotRenderer
{
    /// <summary>Dibuja el frame indicado en (x,y) y devuelve el tamaño ocupado.</summary>
    public static Size Draw(Graphics g, bool draw, int x, int y, SessionPhase phase, MascotSize size,
                            int frameIndex, Theme theme, Font mono)
    {
        var frames = MascotSprite.Frames(phase, size);
        var frame = frames[frameIndex % frames.Count];
        float lineH = mono.GetHeight(g);
        float maxW = 0;
        var color = PhaseColor(theme, phase);
        for (int i = 0; i < frame.Length; i++)
        {
            if (draw)
            {
                using var b = new SolidBrush(color);
                g.DrawString(frame[i], mono, b, x, y + i * lineH);
            }
            var w = g.MeasureString(frame[i], mono).Width;
            if (w > maxW) maxW = w;
        }
        return new Size((int)Math.Ceiling(maxW), (int)Math.Ceiling(frame.Length * lineH));
    }

    public static Color PhaseColor(Theme theme, SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval => theme.Warn,
        SessionPhase.WaitingForInput => theme.Warn,
        SessionPhase.Processing => theme.Ok,
        SessionPhase.Compacting => theme.Ok,
        SessionPhase.Ended => theme.Critical,
        _ => theme.Neutral,
    };
}
```

- [ ] **Step 2: Compilar.** Run el build. Expected: 0 errores.

- [ ] **Step 3: Commit**
```bash
git add UI/dashboard/MascotRenderer.cs
git commit -m "feat: MascotRenderer (dibujo del bloque ASCII con color por fase)"
```

---

> **Tareas 4–7 son REFACTOR del monolito `UI/DashboardForm.cs`.** Antes de cada una, **lee `UI/DashboardForm.cs` completo** para copiar los cuerpos reales de los métodos que se mueven (`DrawBar`, `DrawPace`, `DrawModelLine`, `DrawLiveSessions`, `DrawSpendBody`, `DrawChart`, `DrawPercentBody`, `DrawSegments`, `FillRounded`, `Pick`). El plan da firmas y el destino; el cuerpo se preserva tal cual, ajustando solo a recibir `Theme/Strings/rects` por parámetro en lugar de leer campos `_theme/_s/_xxxRects`. Tras cada tarea: `dotnet build` 0 errores y, si hay tests, verdes.

### Task 4: `DashboardHeader` — cabecera "de un vistazo"

**Files:**
- Create: `UI/dashboard/DashboardHeader.cs`

Compone, en la zona superior: **mascota** (via `MascotRenderer`) a la izquierda; a la derecha **estado del servicio** (●), la **cuota crítica** (la peor de 5h/7d con su barra + countdown) y **pace+ETA**. Devuelve alto y registra el rect del botón ⚙.

- [ ] **Step 1: Implementar** — Create `UI/dashboard/DashboardHeader.cs`. Lee de `DashboardForm.cs` el cuerpo de `DrawBar` y `DrawPace` para reusar el dibujo de barra/pace (cópialos como métodos privados del header, o exponlos `internal static` y llámalos). Estructura:
```csharp
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>Cabecera de un vistazo: mascota + estado servicio + cuota crítica + pace. Sin estado.</summary>
public static class DashboardHeader
{
    /// <summary>Dibuja la cabecera y devuelve el nuevo y. Registra el rect del ⚙ en gearRect.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
                           AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
                           int mascotFrame, Font labelFont, Font smallFont, Font mono,
                           ref Rectangle gearRect)
    {
        // 1) botón ⚙ arriba a la derecha (siempre)
        var gear = new Rectangle(x + w - 18, y, 16, 16);
        gearRect = gear;
        if (draw)
        {
            using var b = new SolidBrush(theme.Dim);
            g.DrawString("⚙", smallFont, b, gear.X, gear.Y); // ⚙
        }

        // 2) mascota a la izquierda (si ShowMascot y LiveSessionsEnabled)
        int top = y + 18;
        int textX = x;
        if (cfg.LiveSessionsEnabled && cfg.ShowMascot)
        {
            var sz = MascotRenderer.Draw(g, draw, x, top, live.GlobalPhase,
                MascotSprite.ParseSize(cfg.MascotSize), mascotFrame, theme, mono);
            textX = x + sz.Width + 12;
        }

        // 3) a la derecha: estado servicio + cuota crítica + pace
        int ty = top;
        if (draw && snap?.Health is { } h)
        {
            using var b = new SolidBrush(theme.Dim);
            g.DrawString($"● {h.Label}", smallFont, b, textX, ty); // ● estado
        }
        ty += 16;
        // cuota crítica = la de mayor utilización entre 5h/7d
        var w5 = snap?.Usage?.FiveHour; var w7 = snap?.Usage?.SevenDay;
        var crit = PickCritical(w5, w7);
        if (crit is not null)
        {
            // Reusa el dibujo de barra del DashboardForm (DrawBar): cópialo aquí o llama al helper.
            ty = DrawCriticalBar(g, draw, crit, textX, ty, w - (textX - x), cfg, theme, labelFont, smallFont);
        }
        // pace: línea "↗ 5h X% · 7d Y% ⚠ETA" reusando lo de DrawPace
        ty = DrawPaceLine(g, draw, snap, textX, ty, w - (textX - x), theme, smallFont);

        int bottom = Math.Max(ty, top + (cfg.LiveSessionsEnabled && cfg.ShowMascot ? MascotRenderer.Draw(g, false, 0, 0, live.GlobalPhase, MascotSprite.ParseSize(cfg.MascotSize), 0, theme, mono).Height : 0));
        // separador
        if (draw) { using var p = new Pen(theme.Track); g.DrawLine(p, x, bottom + 4, x + w, bottom + 4); }
        return bottom + 10;
    }

    private static UsageWindow? PickCritical(UsageWindow? a, UsageWindow? b)
    {
        if (a is null) return b; if (b is null) return a;
        return a.UtilizationPct >= b.UtilizationPct ? a : b;
    }

    // DrawCriticalBar y DrawPaceLine: copia los cuerpos de DrawBar / DrawPace de DashboardForm.cs
    // ajustando para recibir theme/fonts por parámetro. (Ver nota de refactor arriba.)
    private static int DrawCriticalBar(Graphics g, bool draw, UsageWindow win, int x, int y, int w,
                                       AppConfig cfg, Theme theme, Font labelFont, Font smallFont) { /* cuerpo de DrawBar adaptado */ return y + 40; }
    private static int DrawPaceLine(Graphics g, bool draw, AppSnapshot? snap, int x, int y, int w,
                                    Theme theme, Font smallFont) { /* cuerpo de DrawPace adaptado */ return y + 18; }
}
```
> El implementador rellena `DrawCriticalBar`/`DrawPaceLine` copiando los cuerpos reales de `DrawBar`/`DrawPace` (los lee de `DashboardForm.cs`); deben avanzar `y` idéntico en draw=false/true. El `return y + 40/+18` es el alto que esos métodos ya devuelven hoy — confírmalo contra el archivo.

- [ ] **Step 2: Compilar.** Run el build. Expected: 0 errores.
- [ ] **Step 3: Commit**
```bash
git add UI/dashboard/DashboardHeader.cs
git commit -m "feat: DashboardHeader (cabecera de un vistazo: mascota + estado + cuota critica + pace)"
```

---

### Task 5: `DashboardDataView` — secciones plegables reordenadas

**Files:**
- Create: `UI/dashboard/DashboardDataView.cs`
- Modify: `UI/DashboardForm.cs` (mover los `DrawXxx` de sección aquí)

Orden nuevo (prioridad): **Cuota → Sesiones → Gasto → Gráfica**. Cada sección tiene un header clicable `▸ Título`(plegado) / `▾ Título`(expandido); si expandida, dibuja su cuerpo (reusa los métodos existentes). El plegado se lee de `cfg.Collapsed*`. Registra rects de plegado en `sectionRects` (clave = "quota"/"sessions"/"spend"/"chart").

- [ ] **Step 1: Implementar** — Create `UI/dashboard/DashboardDataView.cs`. Mueve desde `DashboardForm.cs` los cuerpos de `DrawBar`, `DrawModelLine`, `DrawLiveSessions`, `DrawSpendBody`, `DrawChart`, `DrawPercentBody`, `DrawSegments`, `FillRounded`, `Pick` (como `internal static` recibiendo theme/strings/rects por parámetro). Patrón de sección plegable:
```csharp
// firma orientativa
public static int Draw(Graphics g, bool draw, int x, int y, int w,
    AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme, /* fonts */ ...,
    Dictionary<string,Rectangle> sectionRects, Dictionary<ChartRange,Rectangle> tabRects,
    Dictionary<string,Rectangle> modeRects, Dictionary<string,Rectangle> pctWinRects,
    Dictionary<string,Rectangle> liveRowRects, List<HistoryBucket> chartData, List<PctPoint> pctData, ...)
{
    sectionRects.Clear();
    y = Section(g, draw, "quota", s.SectionQuota, !cfg.CollapsedQuota, x, y, w, theme, smallFont, sectionRects,
        (yy) => DrawQuotaBody(g, draw, snap, x, yy, w, cfg, theme, ...));
    y = Section(g, draw, "sessions", s.SectionSessions, !cfg.CollapsedSessions, x, y, w, theme, smallFont, sectionRects,
        (yy) => DrawLiveSessionsBody(g, draw, live, x, yy, w, s, theme, ..., liveRowRects));
    y = Section(g, draw, "spend", s.SectionSpend, !cfg.CollapsedSpend, x, y, w, theme, smallFont, sectionRects,
        (yy) => DrawSpendBody(g, draw, snap, x, yy, w, ...));
    y = Section(g, draw, "chart", s.SectionChart, !cfg.CollapsedChart, x, y, w, theme, smallFont, sectionRects,
        (yy) => DrawChartBody(g, draw, x, yy, w, chartData, pctData, ..., tabRects, modeRects, pctWinRects));
    return y;
}

// header plegable: dibuja "▸/▾ Título", registra rect, y si expanded llama al cuerpo.
private static int Section(Graphics g, bool draw, string key, string title, bool expanded,
    int x, int y, int w, Theme theme, Font f, Dictionary<string,Rectangle> rects, Func<int,int> body)
{
    var r = new Rectangle(x, y, w, 16);
    rects[key] = r;
    if (draw) { using var b = new SolidBrush(theme.Foreground); g.DrawString((expanded?"▾ ":"▸ ")+title, f, b, x, y); }
    y += 18;
    if (expanded) y = body(y);
    return y + 4;
}
```
> `DrawQuotaBody` = el bloque de barras 5h/7d + modelos que hoy está suelto en `LayoutContent`; `DrawLiveSessionsBody` = el cuerpo actual de `DrawLiveSessions` (sin su propia cabecera, que ahora la pone `Section`). Copia los cuerpos reales. Mantén la simetría medir/pintar y `Clear()` de los rects cuando una sección está plegada (no dejar rects fantasma).

- [ ] **Step 2: Compilar.** 0 errores.
- [ ] **Step 3: Commit**
```bash
git add UI/dashboard/DashboardDataView.cs UI/DashboardForm.cs
git commit -m "feat: DashboardDataView (secciones plegables reordenadas por prioridad)"
```

---

### Task 6: `DashboardSettingsView` — panel de ajustes

**Files:**
- Create: `UI/dashboard/DashboardSettingsView.cs`

Dibuja las filas de configuración agrupadas (Apariencia, Secciones, Sesiones, Notificaciones, Icono, Frecuencia, Idioma, Sistema). Cada control clicable registra su rect con una **clave de acción** (p.ej. `"theme:dark"`, `"toggle:ShowSpend"`, `"freq:60"`, `"mascotsize:large"`). El click se traduce a un `Action<AppConfig>` que `DashboardForm` emite por `SettingsChanged`.

- [ ] **Step 1: Implementar** — Create `UI/dashboard/DashboardSettingsView.cs`:
```csharp
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

public static class DashboardSettingsView
{
    /// <summary>Dibuja el panel y registra rects clicables con clave de acción. Devuelve nuevo y.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
                           Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        rects.Clear();
        y = GroupHeader(g, draw, s.MenuSections, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowSpend", s.ShowSpend, cfg.ShowSpendEstimate, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowHealth", s.ShowServiceStatus, cfg.ShowHealth, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowChart", s.UsageChart, cfg.ShowChart, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.MenuLiveSessions, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowMascot", s.MenuShowMascot, cfg.ShowMascot, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "mascotsize", s.MascotSizeLabel, new[]{("compact",s.MascotSizeCompact),("large",s.MascotSizeLarge)}, cfg.MascotSize, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.Notifications, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Notifications", s.Enabled, cfg.NotificationsEnabled, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:PaceAlerts", s.PaceAlerts, cfg.PaceAlerts, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.UpdateFrequency, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "freq", "", new[]{("30",s.Sec30),("60",s.Min1),("300",s.Min5),("900",s.Min15)}, cfg.RefreshSeconds.ToString(), x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.MenuAppearance, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "theme", s.Theme, new[]{("system",s.ThemeSystem),("dark",s.ThemeDark),("light",s.ThemeLight),("cli","CLI")}, cfg.Theme, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Sticky", s.Sticky, cfg.DashboardSticky, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:OnTop", s.AlwaysOnTop, cfg.DashboardAlwaysOnTop, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, "Sistema", x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Startup", s.StartWithWindows, StartupManager.IsEnabled(), x, y, w, theme, smallFont, rects);
        return y;
    }

    /// <summary>Traduce la clave de acción de un rect clicado a la mutación de config.</summary>
    public static Action<AppConfig>? ActionFor(string key) => key switch
    {
        "toggle:ShowSpend" => c => c.ShowSpendEstimate = !c.ShowSpendEstimate,
        "toggle:ShowHealth" => c => c.ShowHealth = !c.ShowHealth,
        "toggle:ShowChart" => c => c.ShowChart = !c.ShowChart,
        "toggle:ShowMascot" => c => c.ShowMascot = !c.ShowMascot,
        "toggle:Notifications" => c => c.NotificationsEnabled = !c.NotificationsEnabled,
        "toggle:PaceAlerts" => c => c.PaceAlerts = !c.PaceAlerts,
        "toggle:Sticky" => c => c.DashboardSticky = !c.DashboardSticky,
        "toggle:OnTop" => c => c.DashboardAlwaysOnTop = !c.DashboardAlwaysOnTop,
        "mascotsize:compact" => c => c.MascotSize = "compact",
        "mascotsize:large" => c => c.MascotSize = "large",
        "theme:system" => c => c.Theme = "system",
        "theme:dark" => c => c.Theme = "dark",
        "theme:light" => c => c.Theme = "light",
        "theme:cli" => c => c.Theme = "cli",
        "freq:30" => c => c.RefreshSeconds = 30,
        "freq:60" => c => c.RefreshSeconds = 60,
        "freq:300" => c => c.RefreshSeconds = 300,
        "freq:900" => c => c.RefreshSeconds = 900,
        "toggle:Startup" => _ => StartupManager.Toggle(),
        _ => null,
    };

    // GroupHeader, ToggleRow, SegmentedRow: helpers de dibujo (texto + ☑/☐ + segmentos),
    // cada uno avanza y devuelve y idéntico en draw=false/true y registra rects con clave (segmentos: "key:value").
    private static int GroupHeader(Graphics g, bool draw, string title, int x, int y, Theme theme, Font f) { if(draw){using var b=new SolidBrush(theme.Dim); g.DrawString(title,f,b,x,y);} return y+20; }
    private static int ToggleRow(Graphics g, bool draw, string key, string label, bool on, int x, int y, int w, Theme theme, Font f, Dictionary<string,Rectangle> rects) { var r=new Rectangle(x,y,w,18); rects[key]=r; if(draw){using var b=new SolidBrush(theme.Foreground); g.DrawString((on?"☑ ":"☐ ")+label,f,b,x,y);} return y+20; }
    private static int SegmentedRow(Graphics g, bool draw, string key, string label, (string val,string txt)[] segs, string active, int x, int y, int w, Theme theme, Font f, Dictionary<string,Rectangle> rects) { /* dibuja label + segmentos; cada segmento registra rects[$"{key}:{val}"]; activo en theme.Ok */ return y+20; }
}
```
> Implementar `SegmentedRow` dibujando los segmentos en fila (reusa el patrón de `DrawSegments` de DashboardForm para look&hit-test), registrando `rects[$"{key}:{val}"]`. `StartupManager.IsEnabled()/Toggle()` — confirma los nombres reales en `Services/StartupManager.cs` (ajusta si difieren).

- [ ] **Step 2: Compilar.** 0 errores.
- [ ] **Step 3: Commit**
```bash
git add UI/dashboard/DashboardSettingsView.cs
git commit -m "feat: DashboardSettingsView (panel de ajustes con filas + ActionFor)"
```

---

### Task 7: `DashboardForm` — modo de vista + integración + hit-test

**Files:**
- Modify: `UI/DashboardForm.cs`

- [ ] **Step 1: Campo de modo + evento** — junto a los campos de estado (líneas 61-66) añadir:
```csharp
    private string _viewMode = "data"; // "data" | "settings"
    private Rectangle _gearRect, _backRect;
    private readonly Dictionary<string, Rectangle> _sectionRects = new();
    private readonly Dictionary<string, Rectangle> _settingsRects = new();
```
Junto a los eventos públicos (72-75):
```csharp
    public event Action<Action<AppConfig>>? SettingsChanged;
    public void ShowSettings() { _viewMode = "settings"; Relayout(); Invalidate(); }
```

- [ ] **Step 2: Dispatch en `LayoutContent`** — al **inicio** de `LayoutContent` (línea 369), ramificar por modo:
```csharp
        if (_viewMode == "settings")
        {
            int yy = top;
            if (draw) { using var b = new SolidBrush(_theme.Dim); g.DrawString("‹ " + _s.Settings, labelFont, b, x, yy); }
            _backRect = new Rectangle(x, yy, 60, 18);
            yy += 22;
            yy = DashboardSettingsView.Draw(g, draw, x, yy, contentW, _cfg, _s, _theme, labelFont, smallFont, _settingsRects);
            return yy + 18;
        }
        // modo data: cabecera + secciones
        { int yy = top;
          yy = DashboardHeader.Draw(g, draw, x, yy, contentW, _snap, _liveView, _cfg, _s, _theme, _mascotFrame, labelFont, smallFont, mono, ref _gearRect);
          yy = DashboardDataView.Draw(g, draw, x, yy, contentW, _snap, _liveView, _cfg, _s, _theme, /*fonts*/..., _sectionRects, _tabRects, _modeRects, _pctWinRects, _liveRowRects, _chartData, _pctData /*, ...*/);
          return yy + 18; }
```
> Lee `LayoutContent` real para los nombres de `top`/`contentW`/fuentes (`labelFont`/`smallFont`/`mono`) y crea `mono` (`new Font("Consolas", ...)`) si no existe. Las secciones sueltas que hoy dibuja `LayoutContent` quedan ahora dentro de los renderers (Task 4/5); retira el código duplicado.

- [ ] **Step 3: Hit-test en `OnMouseDown`** — añadir, **antes** del fallback `_dragging=true`:
```csharp
        if (_viewMode == "data" && _gearRect.Contains(e.Location)) { _viewMode = "settings"; Relayout(); Invalidate(); return; }
        if (_viewMode == "settings")
        {
            if (_backRect.Contains(e.Location)) { _viewMode = "data"; Relayout(); Invalidate(); return; }
            foreach (var (key, r) in _settingsRects)
                if (r.Contains(e.Location)) { if (DashboardSettingsView.ActionFor(key) is { } a) SettingsChanged?.Invoke(a); Invalidate(); return; }
            return; // en settings, fuera de rects: no drag
        }
        foreach (var (key, r) in _sectionRects)
            if (r.Contains(e.Location)) { SettingsChanged?.Invoke(Toggle(key)); Relayout(); Invalidate(); return; }
```
con helper local:
```csharp
    private static Action<AppConfig> Toggle(string sectionKey) => sectionKey switch
    {
        "quota" => c => c.CollapsedQuota = !c.CollapsedQuota,
        "sessions" => c => c.CollapsedSessions = !c.CollapsedSessions,
        "spend" => c => c.CollapsedSpend = !c.CollapsedSpend,
        "chart" => c => c.CollapsedChart = !c.CollapsedChart,
        _ => _ => { },
    };
```
> Mantén el hit-test existente (tabs/modos/pctWin/liveRow/close) dentro de la rama `data`. Al volver a `data` desde settings, el panel ya refrescó config vía `UpdateData` (el `SettingsChanged`→`MutateConfig`→`RefreshAsync`→`UpdateData` recarga `_cfg`).

- [ ] **Step 4: `OnDeactivate`** — el panel de ajustes NO debe autocerrarse al clicar dentro; el auto-hide actual (líneas 249-256) ya respeta `_sticky`. Para que ajustar no cierre el popup, en `OnDeactivate` añadir: `if (_viewMode == "settings") return;`.

- [ ] **Step 5: Compilar.** 0 errores.
- [ ] **Step 6: Commit**
```bash
git add UI/DashboardForm.cs
git commit -m "feat: DashboardForm modo Data|Settings + ⚙/‹ + hit-test + SettingsChanged"
```

---

### Task 8: `TrayAppContext` — menú minimal + cableado del panel

**Files:**
- Modify: `TrayAppContext.cs`

- [ ] **Step 1: Cablear `SettingsChanged`** — en el ctor, donde se configura `_dashboard` (tras `SetLiveSessionsProvider`, ~línea 112), añadir:
```csharp
        _dashboard.SettingsChanged += a => MutateConfig(a);
```

- [ ] **Step 2: `BuildMenu` minimal** — reemplazar el cuerpo de `BuildMenu` (todos los submenús de config) por solo acciones:
```csharp
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        var miDash = new ToolStripMenuItem(_s.Dashboard); miDash.Click += (_, _) => ToggleDashboard();
        var miSettings = new ToolStripMenuItem(_s.Settings); miSettings.Click += (_, _) => { ShowDashboard(); _dashboard.ShowSettings(); };
        _miUpdate = new ToolStripMenuItem(_s.CheckUpdates); _miUpdate.Click += async (_, _) => await _updates.CheckInteractive();
        var miExit = new ToolStripMenuItem(_s.Exit); miExit.Click += (_, _) => ExitApp();
        menu.Items.Add(miDash);
        menu.Items.Add(miSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExit);
        menu.Opening += (_, _) => UpdateMenuChecks();
        return menu;
    }
```
> Esto retira los campos de menú de config (`_miSpend`, `_miHealth`, `_miChart`, `_miNotifications`, `_miPaceAlerts`, `_miStartup`, `_miSticky`, `_miOnTop`, `_miImportTheme`, `_miLiveSessions`, `_miShowMascot`, `_miSuppressFocused`, `_miInstallHooks`, y las listas `_freqItems`/`_milestoneItems`/`_thresholdItems`/`_posItems`/`_opacityItems`/`_langItems`/`_themeItems`/`_iconItems`/`_mascotItems`). **Pero** la activación de hooks de live-sessions (`ToggleHooks`/instalar) debe seguir accesible → muévela a una fila del panel de ajustes (grupo Sesiones) o mantén un único item "Sesiones en vivo: activar/desactivar" en el menú. Decisión del implementador: lo más coherente con el spec es una fila en `DashboardSettingsView` grupo Sesiones (`"hooks:toggle"` → llama a la lógica de `ToggleHooks`). Cablea esa acción especial fuera de `ActionFor` (necesita el instalador), via un evento `HookToggleRequested` del dashboard que TrayAppContext conecta a `ToggleHooks`.

- [ ] **Step 2b: Simplificar `UpdateMenuChecks`** — quitar todas las líneas que referencian los items retirados; dejar solo `_miUpdate.Text` (disponibilidad de update). Quitar también los helpers de submenú huérfanos (`Sub`, `FreqLabel`, `PosLabel`, `ThemeLabel`, `ImportItermColors` si ya no se usa desde menú — `ImportItermColors` puede quedar accesible desde el panel o retirarse; decisión: mantener el método pero invocarlo desde una fila del panel Apariencia `"theme:import"`).

- [ ] **Step 3: `DescribeMenu`** — actualizar al menú minimal (Dashboard · Ajustes · Buscar actualizaciones · Salir).

- [ ] **Step 4: Compilar.** 0 errores. (Resolver todas las referencias a campos retirados.)
- [ ] **Step 5: Commit**
```bash
git add TrayAppContext.cs
git commit -m "feat: menu click-derecho minimal + cableado SettingsChanged->MutateConfig"
```

---

### Task 9: Localization — strings del panel

**Files:**
- Modify: `Services/Localization.cs`

- [ ] **Step 1: Añadir a la clase `Strings`** (antes de `Changelog`):
```csharp
    public string Settings { get; init; } = "Settings";
    public string Back { get; init; } = "Back";
    public string SectionQuota { get; init; } = "Quota";
    public string SectionSessions { get; init; } = "Sessions";
    public string SectionSpend { get; init; } = "Spend";
    public string SectionChart { get; init; } = "Chart";
    public string MascotSizeLabel { get; init; } = "Mascot size";
    public string MascotSizeCompact { get; init; } = "compact";
    public string MascotSizeLarge { get; init; } = "large";
```
- [ ] **Step 2: Traducción ES** (en el bloque `Spanish`, antes de `Changelog`):
```csharp
        Settings = "Ajustes",
        Back = "Volver",
        SectionQuota = "Cuota",
        SectionSessions = "Sesiones",
        SectionSpend = "Gasto",
        SectionChart = "Gráfica",
        MascotSizeLabel = "Tamaño mascota",
        MascotSizeCompact = "compacta",
        MascotSizeLarge = "grande",
```
(Resto de idiomas: fallback EN, opcional traducir luego. `ThemeLight` ya existe; reusar.)
- [ ] **Step 3: Compilar.** 0 errores.
- [ ] **Step 4: Commit**
```bash
git add Services/Localization.cs
git commit -m "i18n: strings del panel de ajustes y secciones (ES + fallback EN)"
```

---

### Task 10: `--render-test` ambas vistas + verificación E2E

**Files:**
- Modify: `Program.cs` (`RunRenderTest`, líneas 371-414)

- [ ] **Step 1: Extender `RunRenderTest`** — lee el cuerpo real (371-414). Tras renderizar la vista Data a PNG, añadir: render de la **vista Settings** (`form.ShowSettings()` o seteando el modo + `PrepareForRender`) a otro PNG, y un render con `cfg.MascotSize="large"` + `LiveSessionsEnabled=true` para ver la mascota grande. Volcar a `%TEMP%\claudebar-render\data.png`, `settings.png`, `mascot-large.png`. Mantener el patrón existente (DrawToBitmap/CreateGraphics del form).
```csharp
// tras el render Data existente:
form.ShowSettings();
using (var bmpS = new Bitmap(form.Width, form.Height))
{ form.DrawToBitmap(bmpS, new Rectangle(0,0,form.Width,form.Height)); bmpS.Save(Path.Combine(dir,"settings.png")); }
Console.WriteLine("rendered data.png + settings.png");
```
> Ajusta a cómo `RunRenderTest` instancia/renderiza el form (lee 371-414); reusa su `dir` y su forma de capturar.

- [ ] **Step 2: Suite completa**
Run: `… test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.sln" --nologo -v minimal`
Expected: "Passed! - Failed: 0" (todos: live-sessions previos + MascotSize + DashboardConfig).

- [ ] **Step 3: Build Release** — 0 errores.

- [ ] **Step 4: Render visual**
Run: `… run --project ClaudeBarWin.csproj -- --render-test`
Abrir `%TEMP%\claudebar-render\data.png` y `settings.png`: verificar cabecera (mascota+estado+cuota+pace), secciones plegables, y el panel de ajustes con sus filas. Ajustar espaciados si hace falta.

- [ ] **Step 5: Prueba viva (manual, con la app)** — lanzar la app, clic en el icono → dashboard nuevo; ⚙ → panel de ajustes; cambiar un toggle (p.ej. tema) y ver que aplica; ‹ → vuelve a datos; plegar/expandir secciones; cambiar tamaño de mascota. Click-derecho → menú minimal.

- [ ] **Step 6: Commit**
```bash
git add Program.cs
git commit -m "feat: --render-test vuelca vista Data + Settings; verificacion E2E v0.3"
```

---

## Self-Review

**Cobertura del spec:**
- Panel ⚙ dentro del dashboard (modo Data|Settings) → T6, T7 ✓
- Menú click-derecho minimal → T8 ✓
- Cabecera "de un vistazo" (mascota+estado+cuota+pace) → T4 ✓
- Secciones plegables reordenadas por prioridad → T5 + config T1 ✓
- Jerarquía visual (headers/separadores) → T4 (separador) + T5 (headers de sección) ✓
- Mascota 6×6/8×8 elegible → T1 (config) + T2 (arte) + T3 (render) + T6 (fila tamaño) ✓
- División de DashboardForm en renderers → T3-T7 ✓
- Config nueva → T1 ✓; Localization → T9 ✓; render-test ambas vistas → T10 ✓
- Reusa MutateConfig vía SettingsChanged → T7, T8 ✓

**Placeholder scan:** los cuerpos de `DrawCriticalBar`/`DrawPaceLine` (T4), los `DrawXxxBody` (T5) y `SegmentedRow` (T6) se marcan explícitamente como **extracción del código real de `DashboardForm.cs`** (refactor), con la nota dura de leer el archivo y preservar el cuerpo — NO son "implementar luego" sino "mover código existente". El arte de la mascota (T2) es propio y concreto (pulible). No quedan TBDs.

**Type consistency:** `MascotSize` (T2) usado en T3/T6/config. `MascotSprite.Frames(phase,size)`/`ParseSize` (T2) usados en T3. `MascotRenderer.Draw` (T3) usado en T4. `DashboardHeader.Draw`/`DashboardDataView.Draw`/`DashboardSettingsView.Draw`+`ActionFor` (T4/5/6) usados en T7. `SettingsChanged`/`ShowSettings` (T7) usados en T8. Config props (`MascotSize`,`Collapsed*`, T1) usadas en T5/T6/T7. Strings (T9) usadas en T4/5/6/7/8. Consistente.

**Riesgo conocido:** la activación de hooks de live-sessions (antes en el menú) debe reubicarse al panel (T8 step 2 lo señala con un evento `HookToggleRequested`); el implementador no debe perder esa función al adelgazar el menú. Los anchos/altos exactos de los `Draw*` extraídos se confirman contra `DashboardForm.cs` real (refactor de extracción).

