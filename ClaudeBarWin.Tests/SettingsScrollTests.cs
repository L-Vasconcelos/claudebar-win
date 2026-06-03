using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// v0.3.7: el panel de ajustes limita su alto (MaxPanelHeightPct del área útil) y el contenido
/// rueda. Aquí se verifica la matemática PURA del scroll (clamp + geometría del pulgar) que vive en
/// <see cref="DashboardSettingsView"/>; el estado/los eventos de rueda viven en DashboardForm.
/// </summary>
public class SettingsScrollTests
{
    // ---------------- ClampScroll ----------------

    [Fact]
    public void Clamp_is_zero_when_content_fits()
    {
        // Sin overflow no hay scroll posible: cualquier offset colapsa a 0.
        Assert.Equal(0, DashboardSettingsView.ClampScroll(0, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(120, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(-50, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(10, contentH: 400, viewportH: 400)); // justo
    }

    [Fact]
    public void Clamp_bounds_scroll_to_overflow_range()
    {
        // contenido 1000, viewport 400 → rango válido [0, 600].
        Assert.Equal(0, DashboardSettingsView.ClampScroll(-1, 1000, 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(0, 1000, 400));
        Assert.Equal(250, DashboardSettingsView.ClampScroll(250, 1000, 400));
        Assert.Equal(600, DashboardSettingsView.ClampScroll(600, 1000, 400));
        Assert.Equal(600, DashboardSettingsView.ClampScroll(9999, 1000, 400));
    }

    // ---------------- ThumbRect ----------------

    [Fact]
    public void Thumb_is_empty_when_content_fits()
    {
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 300, scroll: 0));
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 400, scroll: 0));
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 0, contentH: 400, scroll: 0));
    }

    [Fact]
    public void Thumb_proportional_and_pinned_to_track_ends()
    {
        const int trackX = 300, trackTop = 70, viewportH = 400, contentH = 1000;

        // Alto proporcional: viewport²/contenido = 160px (≥ mínimo de 24).
        var top = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 0);
        Assert.Equal(trackX, top.X);
        Assert.Equal(DashboardSettingsView.ScrollBarW, top.Width);
        Assert.Equal(160, top.Height);
        Assert.Equal(trackTop, top.Y); // scroll 0 → pegado arriba

        // Scroll máximo (600) → el pulgar termina exactamente al final de la pista.
        var bottom = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 600);
        Assert.Equal(trackTop + viewportH, bottom.Bottom);

        // Scroll intermedio: dentro de la pista, monótono.
        var mid = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 300);
        Assert.InRange(mid.Y, top.Y + 1, bottom.Y - 1);
    }

    [Fact]
    public void Thumb_never_smaller_than_minimum_grabbable_size()
    {
        // Contenido enorme → el pulgar no baja de 24px (visible/agarrable) y sigue dentro de la pista.
        var t = DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 100_000, scroll: 0);
        Assert.Equal(24, t.Height);
        var tMax = DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 100_000, scroll: 99_600);
        Assert.Equal(70 + 400, tMax.Bottom);
    }

    [Fact]
    public void Scroll_constants_are_sane()
    {
        // El tope deja SIEMPRE aire en pantalla (<100%) y la rueda avanza algo perceptible.
        Assert.InRange(DashboardSettingsView.MaxPanelHeightPct, 40, 90);
        Assert.True(DashboardSettingsView.WheelStepPx > 0);
    }
}
