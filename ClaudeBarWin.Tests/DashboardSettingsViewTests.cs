using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T1: infra de helpers del panel de ajustes. <see cref="DashboardSettingsView.SectionHeader"/> debe
/// cumplir el invariante de 2 pasadas (medir==pintar) y avanzar exactamente
/// <c>Spacing.Md + altoTexto + Spacing.Sm</c> sobre la rejilla de 8pt. El divisor de 1px (Theme.Separator)
/// queda dentro de <c>[x, x+w]</c>. Además se verifica la simetría del <see cref="DashboardSettingsView.Draw"/>
/// completo (medir==pintar) tras reexpresar los avances con tokens de <see cref="Spacing"/>.
/// </summary>
public class DashboardSettingsViewTests
{
    private const int X = 16, W = 308;

    // Bitmap/Graphics reales para que MeasureString tenga un contexto válido.
    private static Bitmap NewBmp() => new(W + X * 2, 1200);

    private static AppConfig Cfg() => new()
    {
        ShowMascot = true,
        ShowHealth = true,
        ShowChart = true,
        ShowSpendEstimate = true,
        NotificationsEnabled = true,
        NotifyMilestones = new[] { 25, 50, 75, 95 },
        MascotSize = "compact",
        Theme = "dark",
        DashboardPosition = "BottomRight",
        Language = "es",
    };

    // ---------------- SectionHeader: medir==pintar + avance 8pt + divisor en rango ----------------

    [Fact]
    public void SectionHeader_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int measured = DashboardSettingsView.SectionHeader(g, draw: false, "ACTUALIZACIÓN", X, 100, W, Theme.Dark, Typography.Caption);
        int painted = DashboardSettingsView.SectionHeader(g, draw: true, "ACTUALIZACIÓN", X, 100, W, Theme.Dark, Typography.Caption);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void SectionHeader_advance_is_md_plus_text_plus_sm()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        const int y0 = 200;
        int after = DashboardSettingsView.SectionHeader(g, draw: false, "NOTIFICACIONES", X, y0, W, Theme.Dark, Typography.Caption);

        int textH = (int)Math.Ceiling(g.MeasureString("NOTIFICACIONES", Typography.Caption).Height);
        Assert.Equal(y0 + Spacing.Md + textH + Spacing.Sm, after);
    }

    [Fact]
    public void SectionHeader_reserves_top_and_bottom_air()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        const int y0 = 50;
        int after = DashboardSettingsView.SectionHeader(g, draw: true, "PANEL", X, y0, W, Theme.Dark, Typography.Caption);

        int textH = (int)Math.Ceiling(g.MeasureString("PANEL", Typography.Caption).Height);
        Assert.Equal(y0 + Spacing.Md + textH + Spacing.Sm, after);
    }

    [Fact]
    public void SectionHeader_divider_stays_within_content_width()
    {
        // El divisor de 1px (Theme.Separator) debe pintarse dentro de [x, x+w]: presente en una
        // columna interior, ausente a la izquierda de x y a la derecha de x+w. Línea horizontal
        // axis-aligned → sin antialias, comprobable con GetPixel.
        const int x = 20, w = 200, y0 = 40;
        using var bmp = new Bitmap(x + w + 60, 200);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);

        DashboardSettingsView.SectionHeader(g, draw: true, "PANEL", x, y0, w, Theme.Dark, Typography.Caption);

        var sep = Theme.Dark.Separator;
        bool IsSep(int px, int py)
        {
            var c = bmp.GetPixel(px, py);
            return c.R == sep.R && c.G == sep.G && c.B == sep.B;
        }
        // Busca la fila del divisor en la banda inferior del header.
        int textH = (int)Math.Ceiling(g.MeasureString("PANEL", Typography.Caption).Height);
        int bandTop = y0 + Spacing.Md + textH, bandBot = bandTop + Spacing.Sm;
        int midX = x + w / 2;
        int divY = -1;
        for (int py = bandTop; py <= bandBot && divY < 0; py++)
            if (IsSep(midX, py)) divY = py;

        Assert.True(divY >= 0, "debe existir un divisor con color Theme.Separator bajo el texto");
        Assert.True(IsSep(x, divY) || IsSep(x + 1, divY), "el divisor empieza en x");
        Assert.True(IsSep(x + w - 1, divY) || IsSep(x + w, divY), "el divisor llega hasta x+w");
        Assert.False(IsSep(x - 5, divY), "el divisor no rebasa por la izquierda de x");
        Assert.False(IsSep(x + w + 5, divY), "el divisor no rebasa por la derecha de x+w");
    }

    // ---------------- Draw completo: invariante de 2 pasadas con el nuevo ritmo ----------------

    [Fact]
    public void Draw_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Draw_registers_clickable_rects()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        // Claves estables que el host enruta; se mantienen tras reexpresar avances con Spacing.
        Assert.Contains("toggle:ShowSpend", rects.Keys);
        Assert.Contains("special:hooktoggle", rects.Keys);
        Assert.Contains("cycle:position", rects.Keys);
    }
}
