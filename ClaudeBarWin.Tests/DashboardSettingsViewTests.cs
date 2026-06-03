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

    // ---------------- T2: TogglePill (cápsula+knob, sustituye ☑/☐) ----------------

    [Fact]
    public void TogglePill_measure_equals_paint()
    {
        // El track derecho debe reservar el mismo rect en medir y pintar (geometría idéntica).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var measured = DashboardSettingsView.TogglePill(g, draw: false, on: true, rightX: X + W, y: 100, rowH: 18, theme: Theme.Dark);
        var painted = DashboardSettingsView.TogglePill(g, draw: true, on: true, rightX: X + W, y: 100, rowH: 18, theme: Theme.Dark);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void TogglePill_right_edge_anchors_at_rightX_with_safe_margin()
    {
        // El borde derecho del pill respeta un margen interno ≥ Spacing.Sm respecto a rightX.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W;
        var pill = DashboardSettingsView.TogglePill(g, draw: false, on: false, rightX: rightX, y: 100, rowH: 18, theme: Theme.Dark);

        Assert.True(pill.Right <= rightX - Spacing.Sm, "el pill no debe tocar el borde derecho (margen ≥ Sm)");
        Assert.True(pill.X >= X, "el pill no se sale por la izquierda del contenido");
    }

    [Fact]
    public void TogglePill_knob_left_when_off_right_when_on()
    {
        // El knob se desliza: a la izquierda del track si OFF, a la derecha si ON.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W, y = 100, rowH = 18;
        var track = DashboardSettingsView.TogglePill(g, draw: false, on: false, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);

        int knobOff = DashboardSettingsView.PillKnobCenterX(track, on: false);
        int knobOn = DashboardSettingsView.PillKnobCenterX(track, on: true);

        Assert.True(knobOff < track.X + track.Width / 2, "OFF: knob en la mitad izquierda");
        Assert.True(knobOn > track.X + track.Width / 2, "ON: knob en la mitad derecha");
        Assert.True(knobOn > knobOff, "ON desplaza el knob a la derecha respecto a OFF");
    }

    [Fact]
    public void TogglePill_track_color_accent_when_on_separator_when_off()
    {
        // ON → track Theme.Accent; OFF → track Theme.Separator. Comprobado por píxel en el centro-izquierda
        // del track (zona de track sin knob: el knob está a la derecha cuando ON, a la izquierda cuando OFF,
        // así que muestreamos el lado opuesto para leer el color del track).
        int rightX = 300, y = 40, rowH = 18;
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);

        // ON: knob a la derecha → muestrear cuarto izquierdo (track puro).
        g.Clear(Theme.Dark.Background);
        var onTrack = DashboardSettingsView.TogglePill(g, draw: true, on: true, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);
        var cOn = bmp.GetPixel(onTrack.X + onTrack.Width / 4, onTrack.Y + onTrack.Height / 2);
        var acc = Theme.Dark.Accent;
        Assert.True(Math.Abs(cOn.R - acc.R) <= 8 && Math.Abs(cOn.G - acc.G) <= 8 && Math.Abs(cOn.B - acc.B) <= 8,
            $"track ON debe ser Accent (#{acc.R:X2}{acc.G:X2}{acc.B:X2}), fue #{cOn.R:X2}{cOn.G:X2}{cOn.B:X2}");

        // OFF: knob a la izquierda → muestrear cuarto derecho (track puro).
        g.Clear(Theme.Dark.Background);
        var offTrack = DashboardSettingsView.TogglePill(g, draw: true, on: false, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark);
        var cOff = bmp.GetPixel(offTrack.Right - offTrack.Width / 4, offTrack.Y + offTrack.Height / 2);
        var sep = Theme.Dark.Separator;
        Assert.True(Math.Abs(cOff.R - sep.R) <= 8 && Math.Abs(cOff.G - sep.G) <= 8 && Math.Abs(cOff.B - sep.B) <= 8,
            $"track OFF debe ser Separator (#{sep.R:X2}{sep.G:X2}{sep.B:X2}), fue #{cOff.R:X2}{cOff.G:X2}{cOff.B:X2}");
    }

    // ---------------- T2: ToggleRow con título + subtítulo + pill, sin glifos ☑/☐ ----------------

    [Fact]
    public void ToggleRow_measure_equals_paint_single_line()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Mostrar mascota", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.ToggleRow(g, draw: true, "toggle:X", "Mostrar mascota", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ToggleRow_with_subtitle_is_taller_than_without()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int plain = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", null, true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int withSub = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", "Coste equivalente por modelo", true,
            X, 0, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(withSub > plain, "una fila con subtítulo debe ocupar más alto que sin subtítulo");
    }

    [Fact]
    public void ToggleRow_subtitle_measure_equals_paint()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.ToggleRow(g, draw: false, "toggle:X", "Notificaciones", "Coste equivalente por modelo", false,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);
        int painted = DashboardSettingsView.ToggleRow(g, draw: true, "toggle:X", "Notificaciones", "Coste equivalente por modelo", false,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void ToggleRow_hit_rect_covers_full_row_width()
    {
        // El hit-test es el rect completo de la fila (clic en cualquier punto alterna el toggle).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Hit", "Estado del servicio", null, true,
            X, 100, W, Theme.Dark, Typography.Body, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("toggle:Hit", out var r));
        Assert.Equal(X, r.X);
        Assert.Equal(W, r.Width);
        Assert.True(r.Height > 0);
    }

    [Fact]
    public void ToggleRow_does_not_draw_unicode_checkbox_glyphs()
    {
        // Regresión: la fila ya NO usa los glifos ☑/☐. La cápsula viene de TogglePill (Accent/Separator),
        // no de un carácter Unicode. Verificamos que la columna izquierda del texto (donde antes iba el
        // glifo) NO tiene un cuadro coloreado de check: el primer no-fondo a la altura del baseline es texto.
        // Comprobación indirecta: el pill ON pinta Accent en el lado derecho del rect, nunca a la izquierda.
        int x = 20, w = 240, y = 30;
        using var bmp = new Bitmap(x + w + 40, 80);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.ToggleRow(g, draw: true, "toggle:Z", "Mostrar mascota", null, true,
            x, y, w, Theme.Dark, Typography.Body, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        bool IsAccent(int px, int py)
        {
            var c = bmp.GetPixel(px, py);
            return Math.Abs(c.R - acc.R) <= 8 && Math.Abs(c.G - acc.G) <= 8 && Math.Abs(c.B - acc.B) <= 8;
        }
        // En el cuarto izquierdo (donde iba ☑) no debe haber Accent en ninguna fila.
        bool accentOnLeft = false;
        for (int py = y; py < y + 18 && !accentOnLeft; py++)
            for (int px = x; px < x + (w / 4); px++)
                if (IsAccent(px, py)) { accentOnLeft = true; break; }
        Assert.False(accentOnLeft, "no debe haber cápsula/check a la izquierda (sin glifo ☑)");
    }
}
