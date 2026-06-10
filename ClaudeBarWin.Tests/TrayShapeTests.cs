using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class TrayShapeTests
{
    [Fact]
    public void ShapeFor_ok_is_circle()
        => Assert.Equal(TrayShape.Circle, Tray.ShapeFor(UsageStatus.Ok));

    [Fact]
    public void ShapeFor_warn_is_triangle()
        => Assert.Equal(TrayShape.Triangle, Tray.ShapeFor(UsageStatus.Warn));

    [Fact]
    public void ShapeFor_critical_is_rhombus()
        => Assert.Equal(TrayShape.Rhombus, Tray.ShapeFor(UsageStatus.Critical));

    [Fact]
    public void Each_status_maps_to_a_distinct_shape()
    {
        var shapes = new[]
        {
            Tray.ShapeFor(UsageStatus.Ok),
            Tray.ShapeFor(UsageStatus.Warn),
            Tray.ShapeFor(UsageStatus.Critical)
        };
        Assert.Equal(3, shapes.Distinct().Count());
    }

    [Fact]
    public void Glyph_returns_a_single_char_for_each_status()
    {
        // El glifo de forma del dashboard es de 1 carácter junto al %.
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Circle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Triangle).Length);
        Assert.Equal(1, Tray.ShapeGlyph(TrayShape.Rhombus).Length);
    }

    [Fact]
    public void TaskbarIsLight_does_not_throw()
    {
        // Lee el registro con fallback; nunca debe lanzar.
        var ex = Record.Exception(() => ThemeResolver.TaskbarIsLight());
        Assert.Null(ex);
    }

    [Fact]
    public void Render_with_status_and_stale_does_not_throw()
    {
        // La nueva firma de Render (status + stale) debe producir un icono sin lanzar.
        var ex = Record.Exception(() =>
        {
            using var ico = TrayIconRenderer.Render(
                68, Theme.Dark, 70, 90, UsageStatus.Warn, stale: true);
        });
        Assert.Null(ex);
    }

    // --- T9d (§3 #15): badge "pend" — forma suprimida, ámbar de paleta, sin tapar el dígito ---

    private const double Warn = 70, Crit = 90;

    private static (Bitmap b0, Bitmap b1) RenderPair(int pct, UsageStatus st)
    {
        using var icoBase = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: false);
        using var icoPend = TrayIconRenderer.Render(pct, Theme.Dark, Warn, Crit, st, stale: false, pending: true);
        return (icoBase.ToBitmap(), icoPend.ToBitmap());
    }

    private static double Dist(Color a, Color b)
        => Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    [Fact]
    public void Pending_suppresses_the_a11y_shape_overlay()
    {
        // Con el punto de notificación la forma a11y (rombo/triángulo) se SUPRIME: un solo elemento
        // decorativo por esquina — dígito + forma + punto a 16-32px se solapaban en ruido (§3 #15).
        var (b0, b1) = RenderPair(95, UsageStatus.Critical);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(95, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            // Centro del rombo (s=14 anclado a la esquina inferior derecha del lienzo de 48).
            var shapePx = b0.GetPixel(40, 40);
            Assert.True(Dist(shapePx, ink) < 60, $"sin pending el rombo debe pintarse; fue {shapePx}");
            var pendPx = b1.GetPixel(40, 40);
            Assert.True(Dist(pendPx, ink) >= 60, $"con pending el rombo debe SUPRIMIRSE; fue {pendPx}");
        }
    }

    [Fact]
    public void Pending_dot_uses_palette_warn_amber_with_knockout_ring()
    {
        // El punto era #F5A623 (fuera de paleta; el .ico antiguo lo aplanaba a oliva) con aro
        // #1A1A1A. Ahora: ámbar Warn del tema + aro KNOCKOUT (borrado a transparente) que lo separa
        // del relleno del badge sobre cualquier barra de tareas. Geometría espejo: d=10 en
        // (48-10-1, 1) → centro (42, 6), aro borrado a +2px.
        var (b0, b1) = RenderPair(10, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var dotPx = b1.GetPixel(42, 6);
            var warn = Theme.Dark.Warn;
            Assert.True(Dist(dotPx, warn) < 25,
                $"el punto debe ser el ámbar Warn de la paleta {warn}, fue {dotPx}");
            // Aro knockout: entre el borde del punto (r=5) y el del borrado (r=7) queda TRANSPARENTE.
            var ringPx = b1.GetPixel(42 - 6, 6);
            Assert.True(ringPx.A < 64, $"el aro debe quedar perforado (alpha {ringPx.A})");
            // En el badge base esa zona era relleno opaco (el aro se NOTA).
            Assert.True(b0.GetPixel(42 - 6, 6).A > 200, "sin pending esa zona es relleno opaco");
        }
    }

    [Fact]
    public void Pending_dot_does_not_cover_the_digit()
    {
        // §3 #15: el punto de 18px tapaba el dígito. Ahora todo píxel que el modo pending CAMBIA
        // (punto + aro) debe caer fuera de la tinta del dígito: ningún píxel alterado era tinta
        // (ni su anti-aliasing fuerte) en el badge base. "88" = el par de dígitos más ancho.
        var (b0, b1) = RenderPair(88, UsageStatus.Ok);
        using (b0)
        using (b1)
        {
            var fill = ColorMath.RiskColor(88, Theme.Dark, Warn, Crit);
            var ink = ColorMath.Contrast(fill);
            var violations = new List<string>();
            for (int y = 0; y < b0.Height; y++)
                for (int x = 0; x < b0.Width; x++)
                {
                    var p0 = b0.GetPixel(x, y);
                    var p1 = b1.GetPixel(x, y);
                    if (p0.ToArgb() == p1.ToArgb()) continue;          // sin cambio
                    if (p0.A < 200) continue;                           // base transparente: no es tinta
                    if (Dist(p0, ink) < 100)                            // tinta o su AA fuerte
                        violations.Add($"({x},{y}) base={p0} pend={p1}");
                }
            Assert.True(violations.Count == 0,
                $"el punto pending pisa la tinta del dígito en {violations.Count} px: " +
                string.Join("; ", violations.Take(8)));
        }
    }
}
