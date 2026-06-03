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
}
