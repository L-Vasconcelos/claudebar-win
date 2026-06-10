using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class QuotaBarTests
{
    [Fact]
    public void MarkerX_returns_midpoint_at_fifty_pct()
    {
        Assert.Equal(50, QuotaBarGeometry.MarkerX(0, 100, 50));
    }

    [Fact]
    public void MarkerX_returns_start_at_zero_and_end_at_hundred()
    {
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, 0));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 100));
    }

    [Fact]
    public void MarkerX_respects_x_offset()
    {
        Assert.Equal(20, QuotaBarGeometry.MarkerX(20, 100, 0));
        Assert.Equal(70, QuotaBarGeometry.MarkerX(20, 100, 50));
        Assert.Equal(120, QuotaBarGeometry.MarkerX(20, 100, 100));
    }

    [Fact]
    public void MarkerX_clamps_to_bar_bounds()
    {
        // pct fuera de [0,100] se recorta a los extremos de la barra [x, x+w].
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, -30));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 150));
    }

    [Fact]
    public void TickX_matches_markerX_for_same_pct()
    {
        // El tick de umbral comparte la misma proyección que el marker.
        Assert.Equal(QuotaBarGeometry.MarkerX(0, 100, 70), QuotaBarGeometry.TickX(0, 100, 70));
        Assert.Equal(QuotaBarGeometry.MarkerX(10, 200, 90), QuotaBarGeometry.TickX(10, 200, 90));
    }

    [Fact]
    public void Threshold_ticks_fall_within_bar_and_are_ordered()
    {
        // Warn (70%) y Critical (90%) por defecto: ambos ticks dentro del ancho y ordenados warn < crit.
        const int x = 20, w = 200;
        int warn = QuotaBarGeometry.TickX(x, w, 70);
        int crit = QuotaBarGeometry.TickX(x, w, 90);

        Assert.InRange(warn, x, x + w);
        Assert.InRange(crit, x, x + w);
        Assert.True(warn < crit, "el tick de warn debe quedar a la izquierda del de critical");
    }

    // ================= T3a: el track de una fila compacta se corta antes del texto right-aligned =================

    [Fact]
    public void CompactTrackWidth_stops_before_the_right_text()
    {
        // Fila de 300px con texto de 40px y gap de 8: el track ocupa 252px (no tacha el %).
        Assert.Equal(252, QuotaBarGeometry.CompactTrackWidth(300, 40, 8));
    }

    [Fact]
    public void CompactTrackWidth_is_never_negative()
    {
        // Texto más ancho que la fila (locales largos / panel estrecho): el track desaparece, no revienta.
        Assert.Equal(0, QuotaBarGeometry.CompactTrackWidth(30, 40, 8));
        Assert.Equal(0, QuotaBarGeometry.CompactTrackWidth(48, 40, 8));
    }

    // ================= T3c: el triángulo del pace no invade la fila del label =================

    [Fact]
    public void PaceTriangle_never_rises_above_the_marker_overshoot()
    {
        // El ▾ antiguo subía hasta barY-5 e invadía los descendentes de la fila etiqueta/%; el clamp lo
        // deja a la altura donde ya arrancaba la línea vertical (barY - MarkerOvershoot).
        const int barY = 50;
        var tri = QuotaBarGeometry.PaceTriangle(mx: 100, barY: barY);
        Assert.All(tri, p => Assert.True(p.Y >= barY - QuotaBarGeometry.MarkerOvershoot,
            $"punto del ▾ en y={p.Y}, por encima del clamp {barY - QuotaBarGeometry.MarkerOvershoot}"));
    }

    [Fact]
    public void PaceTriangle_points_down_into_the_bar_centered_on_marker()
    {
        var tri = QuotaBarGeometry.PaceTriangle(mx: 100, barY: 50);
        var tip = tri.OrderByDescending(p => p.Y).First();
        Assert.Equal(100, tip.X);                            // la punta sigue señalando el marker
        Assert.True(tip.Y > tri.Min(p => p.Y), "el ▾ debe conservar altura (seguir siendo un triángulo)");
    }

    [Fact]
    public void Draw_pace_marker_stays_out_of_the_label_row()
    {
        // Pinta una barra con pace al 50% y comprueba que la banda [barY-5, barY-3] — la que el ▾
        // antiguo invadía — queda libre de píxeles TextMuted alrededor del marker.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(50, DateTimeOffset.UtcNow);
        var pace = new PaceResult("5h", 50, 1.0, 50.0, null, null, false, PaceStatus.Ok);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;                                   // la barra arranca 22px bajo la fila del label
        int mx = QuotaBarGeometry.MarkerX(x, w, 50);
        var mk = Theme.Dark.TextMuted;
        for (int py = barY - 5; py <= barY - 3; py++)
            for (int px = mx - 6; px <= mx + 6; px++)
            {
                var p = bmp.GetPixel(px, py);
                bool muted = Math.Abs(p.R - mk.R) <= 8 && Math.Abs(p.G - mk.G) <= 8 && Math.Abs(p.B - mk.B) <= 8;
                Assert.False(muted, $"píxel del marker en ({px},{py}) invade la fila del label (barY={barY})");
            }
    }

    // ================= T3b: ticks de umbral visibles sobre el tramo vacío del track =================

    [Fact]
    public void Draw_threshold_ticks_are_visible_on_the_empty_track()
    {
        // Con 0% de uso, el tramo de los ticks es track puro: el tick debe contrastar ≥3:1 (WCAG 1.4.11)
        // con el Track. Con theme.Separator (≈ Track en los 3 temas) el tick era invisible (~1:1).
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(0, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace: null, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        int barY = y0 + 22;
        int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
        bool visible = false;
        for (int px = wx - 1; px <= wx + 1 && !visible; px++)
        {
            var p = bmp.GetPixel(px, barY + 5);               // mitad de la barra (BarH=11)
            if (ColorMath.ContrastRatio(p, Theme.Dark.Track) >= 3.0) visible = true;
        }
        Assert.True(visible, "el tick de warn no se distingue del track (necesita TickOnTrack, ≥3:1)");
    }

    // ================= T6b: el % crítico usa la variante de TEXTO (CriticalText), el relleno no cambia =================

    [Fact]
    public void Draw_critical_percent_text_uses_critical_text_variant()
    {
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        // AA en escala de grises (sin ClearType subpixel): cada píxel del texto queda EXACTAMENTE en la
        // recta fondo→color del brush, así el muestreo por tolerancia es determinista.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(62, DateTimeOffset.UtcNow.AddHours(2));
        var pace = new PaceResult("5h", 62, 1.5, 40.0, null, null, true, PaceStatus.Critical);
        var cfg = new AppConfig();
        var s = Localization.Get("en");

        const int x = 16, y0 = 10, w = 300;
        QuotaBar.Draw(g, draw: true, "Session (5h)", win, pace, x, y0, w,
            cfg, s, Theme.Dark, Typography.Body, Typography.Caption, fg, dim);

        // La fila etiqueta/% ocupa [y0, y0+18); la barra (cuyo RELLENO sí sigue siendo Critical)
        // arranca en y0+22. En la fila de texto: ni un píxel del rojo de relleno #DC2626 (3.7:1)
        // y al menos uno del rojo de texto CriticalText (#F87171, 6.4:1).
        var fill = Theme.Dark.Critical;
        var txt = Theme.Dark.CriticalText;
        bool foundText = false;
        for (int py = y0; py < y0 + 18; py++)
            for (int px = x; px < x + w; px++)
            {
                var p = bmp.GetPixel(px, py);
                bool isFill = Math.Abs(p.R - fill.R) <= 10 && Math.Abs(p.G - fill.G) <= 10 && Math.Abs(p.B - fill.B) <= 10;
                Assert.False(isFill, $"({px},{py}): el % crítico sigue pintado con el rojo de RELLENO #DC2626");
                if (Math.Abs(p.R - txt.R) <= 10 && Math.Abs(p.G - txt.G) <= 10 && Math.Abs(p.B - txt.B) <= 10)
                    foundText = true;
            }
        Assert.True(foundText, "el glifo/% crítico no usa CriticalText (#F87171) en tema oscuro");
    }
}
