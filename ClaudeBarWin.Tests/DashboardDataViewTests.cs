using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T9 (auditoría visual): la primera etiqueta del eje X de la gráfica se cortaba por la izquierda
/// ("Jun 00h" → "un 00h") porque se centraba bajo su tick y el tick más a la izquierda coincide con
/// el borde del plot. <see cref="DashboardDataView.AxisLabelX"/> es un helper PURO que recoloca cada
/// etiqueta dentro de [plotLeft, plotRight] sin solapar ni salirse, así que se testea sin GDI+.
/// </summary>
public class DashboardDataViewTests
{
    [Fact]
    public void First_label_is_not_clipped_on_the_left()
    {
        // Etiqueta de 40 px centrada en plotLeft (x=100) querría empezar en 80 (< plotLeft) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 100, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx >= 100, $"la primera etiqueta empezó en {lx} (< plotLeft 100): se cortaría por la izquierda");
        Assert.Equal(100, lx);
    }

    [Fact]
    public void Last_label_is_not_clipped_on_the_right()
    {
        // Etiqueta de 40 px centrada en plotRight (x=400) querría terminar en 420 (> plotRight) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 400, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx + 40 <= 400, $"la última etiqueta terminó en {lx + 40} (> plotRight 400): se cortaría por la derecha");
        Assert.Equal(360, lx);
    }

    [Fact]
    public void Middle_label_stays_centered()
    {
        // Bien dentro del plot: se centra bajo su tick (250 - 20 = 230).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.Equal(230, lx);
    }

    [Fact]
    public void Label_never_starts_before_plot_left_for_any_center()
    {
        for (float cx = 100; cx <= 400; cx += 13)
        {
            float lx = DashboardDataView.AxisLabelX(cx, labelW: 50, plotLeft: 100, plotRight: 400);
            Assert.True(lx >= 100, $"centerX {cx}: arranca en {lx} (< 100)");
        }
    }

    [Fact]
    public void Wide_label_is_clamped_to_plot_left_when_it_cannot_fit()
    {
        // Etiqueta más ancha que el plot: prioriza no cortar por la izquierda (Near en plotLeft).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 500, plotLeft: 100, plotRight: 400);
        Assert.Equal(100, lx);
    }

    // ================= v0.3.5 P2 #4: mini-fila de modelo (Opus/Sonnet 7d) con barra de referencia =================

    [Fact]
    public void ModelLine_measure_equals_paint()
    {
        // Invariante de 2 pasadas: medir (draw=false) y pintar (draw=true) avanzan el MISMO y.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(42, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();

        int measured = DashboardDataView.DrawModelLine(g, draw: false, "Sonnet 7d", win, 16, 30, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);
        int painted = DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, 16, 30, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ModelLine_reserves_room_for_the_reference_bar()
    {
        // P2 #4: la mini-fila ya no es solo el % suelto; reserva alto para una barrita de referencia
        // debajo (texto + barra + gap), así el % se lee como una mini-cuota anclada, no flotando.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(42, DateTimeOffset.UtcNow);
        var cfg = new AppConfig();

        const int y0 = 30;
        int after = DashboardDataView.DrawModelLine(g, draw: false, "Opus 7d", win, 16, y0, 300,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        // Crece más que una simple línea de 16px (texto + barra + gap).
        Assert.True(after - y0 > 16, $"la mini-fila debe reservar alto para la barra de referencia (avance={after - y0})");
    }

    [Fact]
    public void ModelLine_draws_a_reference_bar_under_the_text()
    {
        // La barrita de referencia (relleno por riesgo) se pinta DEBAJO del texto: hay píxeles del color de
        // riesgo en la banda de la barra (no solo el track), anclando el % a una mini-cuota.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(60, DateTimeOffset.UtcNow); // 60% → relleno visible
        var cfg = new AppConfig();
        var c = ColorMath.RiskColor(60, Theme.Dark, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);

        const int x = 16, y0 = 30, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, Localization.Get("en").Culture);

        // Banda de la barra: justo bajo la línea de texto (16px). Busca el color de riesgo cerca del inicio.
        bool foundFill = false;
        for (int py = y0 + 16; py <= y0 + 24 && !foundFill; py++)
            for (int px = x + 1; px < x + 20 && !foundFill; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (Math.Abs(p.R - c.R) <= 14 && Math.Abs(p.G - c.G) <= 14 && Math.Abs(p.B - c.B) <= 14)
                    foundFill = true;
            }
        Assert.True(foundFill, "la mini-fila de modelo debe pintar una barra de referencia rellena bajo el %");
    }
}
