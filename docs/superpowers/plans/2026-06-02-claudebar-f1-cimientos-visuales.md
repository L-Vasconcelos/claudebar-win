# Fase 1 · Cimientos visuales — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar a ClaudeBar-win una base visual coherente (tokens semánticos por tema, rejilla 8pt + tipografía explícita, números monoespaciados, color de cuota interpolado) sin cambiar la funcionalidad.

**Architecture:** Un módulo nuevo `DesignSystem` (sin estado, puro: Spacing/Typography/ColorMath) + el `Theme` existente ampliado con tokens semánticos. Los 6 renderers GDI+ se migran para consumir tokens/typography/RiskColor en vez de colores y offsets ad-hoc. Refactor visual: misma función, look coherente.

**Tech Stack:** C#/.NET 9, WinForms, System.Drawing (GDI+), xUnit. Build/test con `C:/Users/zorro/.dotnet/dotnet.exe` (env `DOTNET_ROOT=C:/Users/zorro/.dotnet`). Verificación visual con `--render-test`.

---

## Notas de entorno (leer antes de empezar)

- **SDK:** `dotnet` NO está en PATH. Usar siempre:
  ```
  $env:DOTNET_ROOT="C:\Users\zorro\.dotnet"; & "C:\Users\zorro\.dotnet\dotnet.exe" <cmd>
  ```
- **Build:** `& "C:\Users\zorro\.dotnet\dotnet.exe" build "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.csproj" --nologo`
- **Test:** `& "C:\Users\zorro\.dotnet\dotnet.exe" test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.Tests\ClaudeBarWin.Tests.csproj" --nologo`
- **Render:** `& "C:\Users\zorro\Proyectos\claudebar-win\bin\Debug\net9.0-windows\ClaudeBarWin.exe" --render-test` → PNGs en `%TEMP%\claudebar-render\`.
- Hay **64 tests** hoy; deben seguir verdes en cada paso.
- `System.Drawing` (`Color`, `Font`, `FontStyle`, `Graphics`) está disponible vía global usings del proyecto WinForms (ver `Theme.cs`, que usa `Color` sin `using`).
- Rama actual: `feat/live-sessions`. Hay 6 archivos de código MODIFICADOS sin commitear (pulidos de v0.3: `Program.cs`, `MascotSprite.cs`, `TrayAppContext.cs`, `TrayIconRenderer.cs`, `DashboardHeader.cs`, `DashboardSettingsView.cs`). **No los toques ni los descartes**; convive con ellos. Los commits de este plan añaden encima.

## File Structure

- **Create** `Services/DesignSystem.cs` — `Spacing` (consts), `Typography` (fuentes cacheadas), `ColorMath` (`Lerp`, `RiskColor`).
- **Modify** `Services/Theme.cs` — tokens nuevos (`Accent`, `BgElevated`, `TextMuted`, `Separator`) + alias semánticos (`TextPrimary`/`TextSecondary`/`BgBase`); setear en `Dark`/`Light`/`Cli` + `FromImported`.
- **Create** `ClaudeBarWin.Tests/DesignSystemTests.cs`, `ClaudeBarWin.Tests/RiskColorTests.cs`, `ClaudeBarWin.Tests/ThemeTokenTests.cs`.
- **Modify** `UI/dashboard/DashboardHeader.cs`, `DashboardDataView.cs`, `DashboardSettingsView.cs`, `MascotRenderer.cs`, `UI/TrayIconRenderer.cs`, `UI/DashboardForm.cs` — consumir tokens + `Typography` + `Spacing` + `ColorMath.RiskColor`; números con `Typography.Mono`.

Orden: primero las unidades nuevas testeables (Tasks 1-3), luego los refactors de render verificados por `--render-test` (Tasks 4-6), luego verificación final (Task 7).

---

### Task 1: `DesignSystem` — Spacing + ColorMath (Lerp, RiskColor)

**Files:**
- Create: `Services/DesignSystem.cs`
- Test: `ClaudeBarWin.Tests/RiskColorTests.cs`, `ClaudeBarWin.Tests/DesignSystemTests.cs`

- [ ] **Step 1: Write the failing tests**

`ClaudeBarWin.Tests/RiskColorTests.cs`:
```csharp
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class RiskColorTests
{
    [Fact]
    public void At_zero_is_ok()
        => Assert.Equal(Theme.Dark.Ok, ColorMath.RiskColor(0, Theme.Dark, 70, 90));

    [Fact]
    public void At_hundred_is_critical()
        => Assert.Equal(Theme.Dark.Critical, ColorMath.RiskColor(100, Theme.Dark, 70, 90));

    [Fact]
    public void Red_channel_is_monotonic_nondecreasing()
    {
        int prev = -1;
        for (int p = 0; p <= 100; p += 5)
        {
            int r = ColorMath.RiskColor(p, Theme.Dark, 70, 90).R;
            Assert.True(r >= prev, $"R bajó en {p}%");
            prev = r;
        }
    }

    [Fact]
    public void Clamps_out_of_range()
    {
        Assert.Equal(Theme.Dark.Ok, ColorMath.RiskColor(-50, Theme.Dark, 70, 90));
        Assert.Equal(Theme.Dark.Critical, ColorMath.RiskColor(250, Theme.Dark, 70, 90));
    }
}
```

`ClaudeBarWin.Tests/DesignSystemTests.cs`:
```csharp
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class DesignSystemTests
{
    [Fact]
    public void Spacing_values_are_multiples_of_four()
    {
        foreach (var v in new[] { Spacing.Xs, Spacing.Sm, Spacing.Md, Spacing.Lg, Spacing.Xl, Spacing.Xxl })
            Assert.Equal(0, v % 4);
    }

    [Fact]
    public void Lerp_returns_endpoints_at_0_and_1()
    {
        var a = Color.FromArgb(10, 20, 30);
        var b = Color.FromArgb(200, 200, 200);
        Assert.Equal(a, ColorMath.Lerp(a, b, 0));
        Assert.Equal(b, ColorMath.Lerp(a, b, 1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "C:\Users\zorro\.dotnet\dotnet.exe" test "C:\Users\zorro\Proyectos\claudebar-win\ClaudeBarWin.Tests\ClaudeBarWin.Tests.csproj" --nologo`
Expected: FAIL (no existe `ColorMath`/`Spacing`).

- [ ] **Step 3: Create `Services/DesignSystem.cs` (Spacing + ColorMath)**

```csharp
namespace ClaudeBarWin.Services;

/// <summary>Espaciado en rejilla de 8pt (múltiplos de 4). Sustituye los offsets ad-hoc.</summary>
public static class Spacing
{
    public const int Xs = 4;
    public const int Sm = 8;
    public const int Md = 12;
    public const int Lg = 16;
    public const int Xl = 24;
    public const int Xxl = 32;
}

/// <summary>Helpers de color: interpolación lineal y color de cuota por riesgo.</summary>
public static class ColorMath
{
    /// <summary>Interpola por canal ARGB. t se recorta a [0,1].</summary>
    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>
    /// Color de cuota interpolado de forma continua: Ok→Warn hasta el umbral 'warn',
    /// Warn→Critical hasta 'crit', y Critical a partir de ahí. pct se recorta a [0,100].
    /// </summary>
    public static Color RiskColor(double pct, Theme t, double warn, double crit)
    {
        pct = Math.Clamp(pct, 0.0, 100.0);
        if (warn <= 0) warn = 70;
        if (crit <= warn) crit = Math.Max(warn + 1, 90);
        if (pct >= crit) return t.Critical;
        if (pct <= warn) return Lerp(t.Ok, t.Warn, pct / warn);
        return Lerp(t.Warn, t.Critical, (pct - warn) / (crit - warn));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `& "C:\Users\zorro\.dotnet\dotnet.exe" test "...\ClaudeBarWin.Tests.csproj" --filter "FullyQualifiedName~RiskColor|FullyQualifiedName~DesignSystem" --nologo`
Expected: PASS (6 nuevos).

- [ ] **Step 5: Commit**

```
git add Services/DesignSystem.cs ClaudeBarWin.Tests/RiskColorTests.cs ClaudeBarWin.Tests/DesignSystemTests.cs
git commit -m "feat(design): Spacing + ColorMath (Lerp, RiskColor) con tests"
```

---

### Task 2: `Typography` (fuentes del sistema, cacheadas)

**Files:**
- Modify: `Services/DesignSystem.cs` (añadir `Typography`)

> Sin test unitario dedicado (crear `Font` en test headless es frágil); se valida vía build + `--render-test` en tareas de refactor. Es código nuevo aislado.

- [ ] **Step 1: Añadir `Typography` al final de `Services/DesignSystem.cs`**

```csharp
/// <summary>
/// Fuentes del sistema de diseño: una familia (Segoe UI Variable) en 4 pasos + mono para números.
/// Cacheadas estáticamente (viven toda la app); con fallback si la familia no está instalada.
/// </summary>
public static class Typography
{
    public static readonly Font Hero    = Ui("Segoe UI Variable Display", 28f, FontStyle.Bold);
    public static readonly Font Title   = Ui("Segoe UI Variable Text", 15f, FontStyle.Bold);
    public static readonly Font Body    = Ui("Segoe UI Variable Text", 12f, FontStyle.Regular);
    public static readonly Font Caption = Ui("Segoe UI Variable Text", 11f, FontStyle.Regular);
    public static readonly Font Mono    = MonoFont(12f);

    // Crea la fuente pedida; si el sistema sustituye por otra familia (no instalada), cae a "Segoe UI".
    private static Font Ui(string family, float size, FontStyle style)
    {
        try
        {
            var f = new Font(family, size, style);
            if (f.Name.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        catch { }
        return new Font("Segoe UI", size, style);
    }

    private static Font MonoFont(float size)
    {
        foreach (var family in new[] { "Cascadia Mono", "Consolas" })
        {
            try
            {
                var f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
                if (f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
        }
        return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
    }
}
```

- [ ] **Step 2: Build**

Run: `& "C:\Users\zorro\.dotnet\dotnet.exe" build "...\ClaudeBarWin.csproj" --nologo`
Expected: 0 errores.

- [ ] **Step 3: Commit**

```
git add Services/DesignSystem.cs
git commit -m "feat(design): Typography (Segoe UI Variable + mono) cacheada"
```

---

### Task 3: `Theme` — tokens semánticos

**Files:**
- Modify: `Services/Theme.cs`
- Test: `ClaudeBarWin.Tests/ThemeTokenTests.cs`

- [ ] **Step 1: Write the failing test** — `ClaudeBarWin.Tests/ThemeTokenTests.cs`:

```csharp
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ThemeTokenTests
{
    public static IEnumerable<object[]> Themes =>
        new[] { new object[] { Theme.Dark }, new object[] { Theme.Light }, new object[] { Theme.Cli } };

    [Theory]
    [MemberData(nameof(Themes))]
    public void All_tokens_are_opaque(Theme t)
    {
        var tokens = new[] { t.TextPrimary, t.TextSecondary, t.TextMuted, t.Separator, t.Track,
                             t.BgBase, t.BgElevated, t.Accent, t.Ok, t.Warn, t.Critical, t.Neutral };
        Assert.All(tokens, c => Assert.True(c.A > 0, "token transparente/sin setear"));
    }

    [Fact]
    public void Dark_accent_is_claude_orange()
        => Assert.Equal(Color.FromArgb(0xCC, 0x78, 0x5C), Theme.Dark.Accent);

    [Fact]
    public void Dark_has_two_background_levels()
        => Assert.NotEqual(Theme.Dark.BgBase, Theme.Dark.BgElevated);

    [Fact]
    public void Semantic_aliases_map_to_existing_fields()
    {
        Assert.Equal(Theme.Dark.Foreground, Theme.Dark.TextPrimary);
        Assert.Equal(Theme.Dark.Dim, Theme.Dark.TextSecondary);
        Assert.Equal(Theme.Dark.Background, Theme.Dark.BgBase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `& "...dotnet.exe" test "...Tests.csproj" --filter "FullyQualifiedName~ThemeToken" --nologo`
Expected: FAIL (no existen `Accent`/`BgElevated`/`TextMuted`/`Separator`/alias).

- [ ] **Step 3: Añadir tokens + alias a `Theme` (en `Services/Theme.cs`, dentro de la clase, tras `Neutral`)**

```csharp
    // --- Tokens semánticos (Fase 1) ---
    public Color Accent { get; init; }
    public Color BgElevated { get; init; }
    public Color TextMuted { get; init; }
    public Color Separator { get; init; }

    // Alias semánticos sobre los campos existentes (sin romper consumidores).
    public Color TextPrimary => Foreground;
    public Color TextSecondary => Dim;
    public Color BgBase => Background;
```

- [ ] **Step 4: Setear los tokens nuevos en cada tema**

En `Theme.Dark` (cambiar `Track` y añadir los 4 tokens):
```csharp
        Track = Color.FromArgb(58, 58, 60),          // #3A3A3C (antes 63,63,70)
        Ok = Color.FromArgb(22, 163, 74),
        Warn = Color.FromArgb(217, 119, 6),
        Critical = Color.FromArgb(220, 38, 38),
        Neutral = Color.FromArgb(82, 82, 91),
        Accent = Color.FromArgb(0xCC, 0x78, 0x5C),    // naranja Claude
        BgElevated = Color.FromArgb(44, 44, 46),      // #2C2C2E
        TextMuted = Color.FromArgb(142, 142, 147),    // #8E8E93
        Separator = Color.FromArgb(56, 56, 58)        // #38383A
```
En `Theme.Light` (añadir tras `Neutral`):
```csharp
        Accent = Color.FromArgb(0xCC, 0x78, 0x5C),
        BgElevated = Color.FromArgb(255, 255, 255),
        TextMuted = Color.FromArgb(142, 142, 147),
        Separator = Color.FromArgb(209, 209, 214)
```
En `Theme.Cli` (el acento sigue el carácter del tema = verde; añadir tras `Neutral`):
```csharp
        Accent = Color.FromArgb(0, 217, 89),
        BgElevated = Color.FromArgb(10, 16, 10),
        TextMuted = Color.FromArgb(0, 110, 44),
        Separator = Color.FromArgb(0, 50, 20)
```

- [ ] **Step 5: Actualizar `FromImported` (en `ThemeResolver`) con fallbacks**

En el objeto que crea `FromImported`, tras `Neutral = Theme.Dark.Neutral`:
```csharp
        Neutral = Theme.Dark.Neutral,
        Accent = Theme.Dark.Accent,
        BgElevated = ColorMath.Lerp(Hex(c.Bg, Theme.Dark.Background), Color.White, 0.06),
        TextMuted = Hex(c.Dim, Theme.Dark.TextMuted),
        Separator = Hex(c.Track, Theme.Dark.Separator)
```
(`ColorMath` está en el mismo namespace `ClaudeBarWin.Services`, no requiere `using`.)

- [ ] **Step 6: Run tests (nuevos + 64 existentes)**

Run: `& "...dotnet.exe" test "...Tests.csproj" --nologo`
Expected: PASS — total **64 + 6 (Task1) + 4 (ThemeToken) = 74**.

- [ ] **Step 7: Commit**

```
git add Services/Theme.cs ClaudeBarWin.Tests/ThemeTokenTests.cs
git commit -m "feat(theme): tokens semánticos (Accent/BgElevated/TextMuted/Separator) + alias"
```

---

### Task 4: Refactor `DashboardHeader` + `DashboardDataView`

**Files:**
- Modify: `UI/dashboard/DashboardHeader.cs`, `UI/dashboard/DashboardDataView.cs`

Objetivo: que estos renderers consuman tokens/typography/RiskColor. **Sin cambiar el layout** (mismas posiciones); solo de dónde sacan color/fuente. Leer cada archivo entero antes de editar.

- [ ] **Step 1: Migrar colores a tokens semánticos**

Sustituciones (en ambos archivos, donde apliquen):
- `theme.Foreground` (texto principal) → `theme.TextPrimary`
- `theme.Dim` (etiquetas) → `theme.TextSecondary`; captions/“resets en…” → `theme.TextMuted`
- Fondo de tarjeta/sección (donde se rellene un panel, p.ej. el botón ⚙ que hoy usa `theme.Track`) → `theme.BgElevated`
- dots/segmento/tab activo → `theme.Accent` (NO usar Accent para alertas)

- [ ] **Step 2: Color de cuota por `RiskColor`**

En `DashboardHeader.DrawCriticalBar` el color `c` se calcula por salto de umbral. Sustituir el `else` de umbral por interpolación, conservando la rama de pace:
```csharp
Color c = pace is { } ps
    ? (ps == PaceStatus.Critical ? theme.Critical : ps == PaceStatus.Over ? theme.Warn : theme.Ok)
    : ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
```
Aplicar el mismo `RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct)` donde `DashboardDataView` coloree barras/valores de cuota por umbral.

- [ ] **Step 3: Números con `Typography.Mono`**

Donde se dibuje un valor numérico (`$"{util:0.#}%"`, importes `$`, tokens, countdown/ETA) con `labelFont`/`smallFont`, usar `Typography.Mono` (mismo tamaño visual) y mantener la alineación a la derecha ya existente (`x + w - sz.Width`).

- [ ] **Step 4: Build + render-test**

Run build; luego `--render-test`; abrir `%TEMP%\claudebar-render\data.png`.
Expected: 0 errores; el header se ve igual de posicionado pero con números monoespaciados, acento naranja en dots y barra con color gradual.

- [ ] **Step 5: Run tests + Commit**

```
& "...dotnet.exe" test "...Tests.csproj" --nologo   # 74 verdes
git add UI/dashboard/DashboardHeader.cs UI/dashboard/DashboardDataView.cs
git commit -m "refactor(ui): Header+DataView usan tokens, mono y RiskColor"
```

---

### Task 5: Refactor `DashboardSettingsView` + `MascotRenderer` + fuentes de `DashboardForm`

**Files:**
- Modify: `UI/dashboard/DashboardSettingsView.cs`, `UI/dashboard/MascotRenderer.cs`, `UI/DashboardForm.cs`

- [ ] **Step 1: `DashboardSettingsView` a tokens**

- Texto de filas/labels → `theme.TextPrimary`; group headers y captions → `theme.TextSecondary`/`theme.TextMuted`.
- Fondo de control/segmento activo y el `ButtonRow` (fondo) → `theme.BgElevated`; segmento/tab activo tinte `theme.Accent`.
- El `RoundedRectPath`/`FillRounded` se mantienen.

- [ ] **Step 2: `MascotRenderer` a tokens**

`MascotRenderer.PhaseColor` ya mapea a `theme.Warn/Ok/Critical/Neutral`: dejarlo (son estados). Solo cambiar cualquier `theme.Foreground`/`Dim` literal por `TextPrimary`/`TextSecondary` si los hubiera.

- [ ] **Step 3: Fuentes de `DashboardForm.LayoutContent` → `Typography`**

En `UI/DashboardForm.cs` (~líneas 411-416) sustituir las fuentes locales por las cacheadas:
```csharp
// Antes: using var titleFont = new Font("Segoe UI", 13f, FontStyle.Bold); ... etc.
var titleFont = Typography.Title;
var planFont  = Typography.Caption;
var labelFont = Typography.Body;
var smallFont = Typography.Caption;
var tabFont   = Typography.Caption;   // si se necesita bold, mantener un Font bold local
var mono      = Typography.Mono;
```
Quitar los `using var` de esas fuentes (ya NO se deben `Dispose`: son estáticas compartidas). Revisar que ninguna de esas variables se haga `Dispose` luego. Snap de offsets evidentes (`y += 50` → `y += Spacing.Xxl + Spacing.Lg` = 56 si conserva el look; si 50 encaja mejor, dejar y anotar) — **prioridad: no romper el layout**; ante la duda, conservar el valor numérico actual.

- [ ] **Step 4: Build + render-test (data.png y settings.png)**

Expected: 0 errores; ajustes y mascota coherentes; sin solapes.

- [ ] **Step 5: Run tests + Commit**

```
& "...dotnet.exe" test "...Tests.csproj" --nologo   # 74 verdes
git add UI/dashboard/DashboardSettingsView.cs UI/dashboard/MascotRenderer.cs UI/DashboardForm.cs
git commit -m "refactor(ui): SettingsView+Mascot+fuentes usan el sistema de diseño"
```

---

### Task 6: Refactor `TrayIconRenderer` (badge por RiskColor)

**Files:**
- Modify: `UI/TrayIconRenderer.cs`

> El badge hoy recibe `bg` ya calculado por `Theme.StatusColor` (salto de umbral) desde los call-sites (`Program.cs`, `TrayAppContext.cs`). Para color gradual sin tocar todos los call-sites, exponer un overload que acepte el % y el tema.

- [ ] **Step 1: Añadir overload que colorea por riesgo**

En `TrayIconRenderer`:
```csharp
public static Icon Render(int percent, Theme theme, double warn, double crit, bool pending = false)
    => Render(percent, ColorMath.RiskColor(percent, theme, warn, crit), pending);
```
(El `Render(int, Color, bool)` existente se mantiene.)

- [ ] **Step 2: Usar el overload en el call-site de runtime**

En `TrayAppContext.cs` donde hoy hace `TrayIconRenderer.Render(icoVal, icoColor, pending: ...)`, pasar a:
```csharp
newIcon = TrayIconRenderer.Render(icoVal, _theme, _config.WarnThresholdPct, _config.CriticalThresholdPct, pending: LiveAttentionPending());
```
(Dejar `RenderError(...)` como está.)

- [ ] **Step 3: Build + render-test (tray-badges.png)**

Expected: 0 errores; los badges 12/68/95 muestran transición de color (no saltos).

- [ ] **Step 4: Run tests + Commit**

```
& "...dotnet.exe" test "...Tests.csproj" --nologo   # 74 verdes
git add UI/TrayIconRenderer.cs TrayAppContext.cs
git commit -m "refactor(tray): badge con color por riesgo (RiskColor)"
```

---

### Task 7: Verificación final + antes/después

**Files:** ninguno (verificación).

- [ ] **Step 1: Build + suite completa**

Run: `& "...dotnet.exe" test "...Tests.csproj" --nologo`
Expected: **74 verdes, 0 fallos**.

- [ ] **Step 2: Render de los 3 temas**

Generar `--render-test` (dark por defecto). Para light/cli, no hay flag — basta confirmar build/tests; el cambio de tema es runtime. Guardar `data.png`, `settings.png`, `mascot-large.png`, `tray-badges.png`.

- [ ] **Step 3: Checklist visual (criterios de aceptación del spec §7)**

Verificar en los PNG: tipografía consistente · números monoespaciados alineados que no bailan · acento naranja en dots/tab activo · 2 niveles de fondo en dark · barra/badge/número con color por riesgo gradual · sin regresión de layout.

- [ ] **Step 4: Enviar antes/después a Yovan** (PNG) y esperar validación visual antes de cerrar Fase 1.

---

## Self-Review

**Spec coverage:** §4.1 tokens → Task 3. §4.2 spacing/typography → Task 1 (Spacing) + Task 2 (Typography) + Task 5 (DashboardForm). §4.3 RiskColor → Task 1 + aplicado en 4/6. §4.4 refactor 6 archivos → Tasks 4-6 (+ DashboardForm en 5). §6 tests → Tasks 1/3 + existentes. §7 criterios → Task 7. **Sin huecos.**

**Placeholders:** ninguno; los refactors dan sustituciones concretas (token→token, font→Typography, umbral→RiskColor) sobre archivos que el ejecutor lee. Los valores numéricos arbitrarios se conservan ante la duda para no romper el layout (decisión explícita, no TODO).

**Type consistency:** `ColorMath.Lerp`/`RiskColor`, `Spacing.*`, `Typography.{Hero,Title,Body,Caption,Mono}`, tokens `Theme.{Accent,BgElevated,TextMuted,Separator,TextPrimary,TextSecondary,BgBase}`, overload `TrayIconRenderer.Render(int, Theme, double, double, bool)` — nombres usados de forma consistente entre tareas. `cfg.WarnThresholdPct`/`cfg.CriticalThresholdPct` existen (usados hoy en `DashboardHeader`).

## 🔗 Relacionado
- Spec: `docs/superpowers/specs/2026-06-02-claudebar-f1-cimientos-visuales-design.md`
- Roadmap: `docs/superpowers/specs/2026-06-02-claudebar-apple-roadmap.md`
