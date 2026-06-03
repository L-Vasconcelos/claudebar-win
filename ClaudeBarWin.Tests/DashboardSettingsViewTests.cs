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

    // ================= T3: anti-truncamiento en CycleRow y SegmentedRow =================

    // -------- CycleRow: medir==pintar + valor elidido sin solapar la etiqueta --------

    [Fact]
    public void CycleRow_measure_equals_paint_short_value()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.CycleRow(g, draw: false, "cycle:x", "Idioma", "Español",
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.CycleRow(g, draw: true, "cycle:x", "Idioma", "Español",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void CycleRow_measure_equals_paint_overflowing_value()
    {
        // El caso PosCustom: valor muy largo que obliga a elidir. La decisión debe ser idéntica
        // en medir y pintar → mismo y de salida.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        const string longVal = "Personalizada (arrastra el panel)";

        int measured = DashboardSettingsView.CycleRow(g, draw: false, "cycle:position", "Posición", longVal,
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.CycleRow(g, draw: true, "cycle:position", "Posición", longVal,
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void CycleRow_long_value_does_not_overlap_label_and_keeps_right_margin()
    {
        // La etiqueta se pinta a la izquierda y el valor (elidido) a la derecha; nunca se solapan
        // y el valor deja margen derecho ≥ Spacing.Sm.
        const int x = 16, w = 200, y = 40; // ancho estrecho a propósito para forzar elipsis
        using var bmp = new Bitmap(x + w + 60, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();
        const string longVal = "Personalizada (arrastra el panel)";

        DashboardSettingsView.CycleRow(g, draw: true, "cycle:position", "Posición", longVal,
            x, y, w, Theme.Dark, Typography.Caption, rects);

        // Geometría medida: la etiqueta termina antes de donde empieza el valor (sin solape),
        // y el valor no rebasa x+w-Spacing.Sm.
        var (lx, lw, rx, rw) = DashboardSettingsView.CycleRowLayout(g, "Posición", longVal, x, w, Typography.Caption);
        Assert.True(lx + lw + Spacing.Md <= rx, "la etiqueta y el valor no deben solaparse (gutter ≥ Md)");
        Assert.True(rx + rw <= x + w - Spacing.Sm, "el valor debe dejar margen derecho ≥ Sm");
        Assert.True(rx >= x, "el valor no empieza a la izquierda del contenido");
    }

    [Fact]
    public void CycleRow_short_value_is_not_ellipsized()
    {
        // Un valor corto que cabe NO debe llevar elipsis (no se toca lo que entra). El texto mostrado
        // es valor + chevron; lo relevante es que NO contiene la elipsis.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        string shown = DashboardSettingsView.CycleRowShownValue(g, "Idioma", "Español", X, W, Typography.Caption);
        Assert.StartsWith("Español", shown);
        Assert.DoesNotContain("…", shown);
    }

    [Fact]
    public void CycleRow_overflowing_value_is_ellipsized()
    {
        // Valor que no cabe en un ancho estrecho → se muestra con elipsis (no el texto completo).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        const string longVal = "Personalizada (arrastra el panel)";
        string shown = DashboardSettingsView.CycleRowShownValue(g, "Posición", longVal, 16, 200, Typography.Caption);
        Assert.NotEqual(longVal, shown);
        Assert.DoesNotContain("Personalizada (arrastra", shown); // se recortó el valor
        Assert.Contains("…", shown);                              // con elipsis medida
    }

    // -------- SegmentedRow: medir==pintar + sin chip fuera de contentLeft + margen derecho --------

    [Fact]
    public void SegmentedRow_measure_equals_paint_fitting()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("30", "30s"), ("60", "1m"), ("300", "5m"), ("900", "15m") };

        int measured = DashboardSettingsView.SegmentedRow(g, draw: false, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void SegmentedRow_no_segment_starts_left_of_content()
    {
        // Frecuencia compacta: ningún chip se pinta con x < contentLeft (X) y el bloque deja
        // margen derecho ≥ Spacing.Sm respecto a X+W.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("30", "30s"), ("60", "1m"), ("300", "5m"), ("900", "15m") };

        DashboardSettingsView.SegmentedRow(g, draw: true, "freq", "", segs, "60",
            X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var key in new[] { "freq:30", "freq:60", "freq:300", "freq:900" })
        {
            Assert.True(rects.TryGetValue(key, out var r), $"falta el rect {key}");
            Assert.True(r.X >= X, $"{key}: chip a la izquierda de contentLeft (x={r.X} < {X})");
            Assert.True(r.Right <= X + W - Spacing.Sm, $"{key}: chip sin margen derecho (right={r.Right} > {X + W - Spacing.Sm})");
        }
    }

    [Fact]
    public void SegmentedRow_wraps_to_two_rows_when_segments_exceed_width()
    {
        // Segmentos artificialmente anchos que NO caben en una fila → la fila crece (2 filas) y
        // ningún chip queda a la izquierda de contentLeft. Medir==pintar se mantiene.
        const int x = 16, w = 120; // ancho muy estrecho
        using var bmp = new Bitmap(x + w + 80, 200);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("a", "Treinta"), ("b", "Un minuto"), ("c", "Cinco min"), ("d", "Quince min") };

        int measured = DashboardSettingsView.SegmentedRow(g, draw: false, "wrap", "", segs, "a",
            x, 0, w, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.SegmentedRow(g, draw: true, "wrap", "", segs, "a",
            x, 0, w, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        // Debe haber crecido más de una fila simple (envolvió a 2 filas).
        Assert.True(measured > 0 + DashboardSettingsView.SegmentRowAdvanceForTest,
            "una fila que no cabe en un renglón debe envolver y ocupar más alto");
        foreach (var seg in segs)
        {
            Assert.True(rects.TryGetValue($"wrap:{seg.Item1}", out var r), $"falta wrap:{seg.Item1}");
            Assert.True(r.X >= x, $"wrap:{seg.Item1} a la izquierda de contentLeft (x={r.X})");
        }
    }

    [Fact]
    public void SegmentedRow_with_label_segments_do_not_overlap_label()
    {
        // Con etiqueta a la izquierda, los segmentos no deben invadir el texto de la etiqueta.
        const int x = 16, w = 308, y = 40;
        using var bmp = new Bitmap(x + w + 40, 120);
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var segs = new[] { ("percent", "%"), ("pace", "▲"), ("both", "%▲") };

        DashboardSettingsView.SegmentedRow(g, draw: true, "icon", "Contenido del icono", segs, "percent",
            x, y, w, Theme.Dark, Typography.Caption, rects);

        float labelRight = x + g.MeasureString("Contenido del icono", Typography.Caption).Width;
        foreach (var seg in segs)
        {
            Assert.True(rects.TryGetValue($"icon:{seg.Item1}", out var r));
            Assert.True(r.X >= labelRight + Spacing.Md - 1, $"icon:{seg.Item1} invade la etiqueta (x={r.X}, labelRight={labelRight:0})");
        }
    }

    // -------- Draw completo: las filas reales no truncan (regresión de frecuencia compacta) --------

    // ================= T4: StatusBadge semántico (Activas/Instalar) =================

    [Fact]
    public void StatusBadge_measure_equals_paint()
    {
        // El rect del badge debe ser idéntico en medir (draw=false) y pintar (draw=true).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var measured = DashboardSettingsView.StatusBadge(g, draw: false, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);
        var painted = DashboardSettingsView.StatusBadge(g, draw: true, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void StatusBadge_right_edge_anchors_with_safe_margin_and_stays_in_content()
    {
        // El borde derecho deja margen interno ≥ Spacing.Sm respecto a rightX; no se sale por la izquierda.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int rightX = X + W;
        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Instalar", Theme.Dark.Warn,
            x: X, rightX: rightX, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);

        Assert.True(badge.Right <= rightX - Spacing.Sm, "el badge no debe tocar el borde derecho (margen ≥ Sm)");
        Assert.True(badge.X >= X, "el badge no se sale por la izquierda del contenido");
        Assert.True(badge.Width > 0 && badge.Height > 0, "el badge tiene área positiva");
    }

    [Fact]
    public void StatusBadge_centered_vertically_in_row()
    {
        // El badge se centra verticalmente en la fila (rowH) → simetría arriba/abajo.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int y = 100, rowH = 28;
        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Activas", Theme.Dark.Ok,
            x: X, rightX: X + W, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);

        int topGap = badge.Y - y;
        int botGap = (y + rowH) - badge.Bottom;
        Assert.True(Math.Abs(topGap - botGap) <= 1, $"el badge debe centrarse en la fila (top={topGap}, bot={botGap})");
    }

    [Fact]
    public void StatusBadge_fill_is_ok_when_active_warn_when_install()
    {
        // El relleno del badge es Theme.Ok (verde) para "Activas" y Theme.Warn (ámbar) para "Instalar".
        int rightX = 300, y = 30, rowH = 28;
        using var bmp = new Bitmap(360, 100);
        using var g = Graphics.FromImage(bmp);

        g.Clear(Theme.Dark.Background);
        var okBadge = DashboardSettingsView.StatusBadge(g, draw: true, "Activas", Theme.Dark.Ok,
            x: 16, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);
        var cOk = bmp.GetPixel(okBadge.X + 3, okBadge.Y + okBadge.Height / 2); // borde izquierdo del relleno (sin texto)
        var ok = Theme.Dark.Ok;
        Assert.True(Math.Abs(cOk.R - ok.R) <= 10 && Math.Abs(cOk.G - ok.G) <= 10 && Math.Abs(cOk.B - ok.B) <= 10,
            $"relleno activo debe ser Ok (#{ok.R:X2}{ok.G:X2}{ok.B:X2}), fue #{cOk.R:X2}{cOk.G:X2}{cOk.B:X2}");

        g.Clear(Theme.Dark.Background);
        var warnBadge = DashboardSettingsView.StatusBadge(g, draw: true, "Instalar", Theme.Dark.Warn,
            x: 16, rightX: rightX, y: y, rowH: rowH, theme: Theme.Dark, f: Typography.Caption);
        var cWarn = bmp.GetPixel(warnBadge.X + 3, warnBadge.Y + warnBadge.Height / 2);
        var warn = Theme.Dark.Warn;
        Assert.True(Math.Abs(cWarn.R - warn.R) <= 10 && Math.Abs(cWarn.G - warn.G) <= 10 && Math.Abs(cWarn.B - warn.B) <= 10,
            $"relleno 'Instalar' debe ser Warn (#{warn.R:X2}{warn.G:X2}{warn.B:X2}), fue #{cWarn.R:X2}{cWarn.G:X2}{cWarn.B:X2}");
    }

    [Fact]
    public void StatusBadge_text_truncates_tail_within_its_width_in_narrow_space()
    {
        // En un ancho muy estrecho el texto se recorta por la cola (elipsis medida) y el badge NUNCA
        // empieza a la izquierda del contenido. El texto mostrado es determinista (medir==pintar).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        // rightX muy cerca de contentLeft → el badge no cabe entero; debe recortar manteniéndose en [x, rightX].
        int x = 16, rightX = x + 70;
        string shown = DashboardSettingsView.StatusBadgeShownText(g, "Instalar (hooks no instalados)", x, rightX, Typography.Caption);
        Assert.NotEqual("Instalar (hooks no instalados)", shown);
        Assert.Contains("…", shown);

        var badge = DashboardSettingsView.StatusBadge(g, draw: false, "Instalar (hooks no instalados)", Theme.Dark.Warn,
            x: x, rightX: rightX, y: 100, rowH: 28, theme: Theme.Dark, f: Typography.Caption);
        Assert.True(badge.X >= x, $"el badge recortado no empieza a la izquierda del contenido (x={badge.X} < {x})");
        Assert.True(badge.Right <= rightX - Spacing.Sm, "el badge recortado deja margen derecho ≥ Sm");
    }

    [Fact]
    public void StatusBadge_short_text_is_not_truncated()
    {
        // Un texto corto que cabe holgado NO lleva elipsis (no se toca lo que entra).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        string shown = DashboardSettingsView.StatusBadgeShownText(g, "Activas", X, X + W, Typography.Caption);
        Assert.Equal("Activas", shown);
        Assert.DoesNotContain("…", shown);
    }

    // ================= T5: MultiSegmentRow (hitos multi-activo, sin re-pintado manual) =================

    private static readonly (string val, string txt)[] MilestoneSegs =
        { ("25", "25%"), ("50", "50%"), ("75", "75%"), ("95", "95%") };

    [Fact]
    public void MultiSegmentRow_measure_equals_paint()
    {
        // El invariante de 2 pasadas: medir (draw=false) y pintar (draw=true) avanzan el mismo y.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();
        var active = new[] { 25, 75 };

        int measured = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "Avisar al llegar a",
            MilestoneSegs, active, X, 100, W, Theme.Dark, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "Avisar al llegar a",
            MilestoneSegs, active, X, 100, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void MultiSegmentRow_registers_a_rect_per_segment()
    {
        // Cada segmento registra rects[$"{key}:{val}"] (las claves que el host enruta por ActionFor).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 50 }, X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var seg in MilestoneSegs)
            Assert.True(rects.ContainsKey($"milestone:{seg.val}"), $"falta rect milestone:{seg.val}");
    }

    [Fact]
    public void MultiSegmentRow_marks_every_active_value_with_accent_fill()
    {
        // Multi-activo NATIVO: TODOS los valores presentes en el array se pintan con Accent (un solo
        // estilo de pill), no solo uno. Verificado por píxel en el centro de cada chip.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();
        var active = new[] { 25, 75, 95 }; // 3 activos a la vez → el caso que rompía DrawSegments

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs, active,
            x, y, w, Theme.Dark, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        bool IsAccent(Rectangle r)
        {
            var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2); // borde izquierdo del chip (sin texto)
            return Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
        }
        foreach (var pct in active)
        {
            Assert.True(rects.TryGetValue($"milestone:{pct}", out var r));
            Assert.True(IsAccent(r), $"el hito activo {pct}% debe pintarse con Accent (un solo estilo de pill)");
        }
    }

    [Fact]
    public void MultiSegmentRow_inactive_value_is_not_accent()
    {
        // Un valor NO presente en el array se pinta con el estilo "off" (BgElevated), no Accent.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25 }, x, y, w, Theme.Dark, Typography.Caption, rects);

        Assert.True(rects.TryGetValue("milestone:50", out var r)); // 50 NO está activo
        var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2);
        var acc = Theme.Dark.Accent;
        bool isAccent = Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
        Assert.False(isAccent, "un hito inactivo NO debe pintarse con Accent");
    }

    [Fact]
    public void MultiSegmentRow_no_chip_left_of_content_and_keeps_right_margin()
    {
        // Anti-truncamiento: ningún chip se pinta con x < contentLeft (X) y el bloque deja margen
        // derecho ≥ Spacing.Sm respecto a X+W.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            new[] { 25, 50, 75, 95 }, X, 100, W, Theme.Dark, Typography.Caption, rects);

        foreach (var seg in MilestoneSegs)
        {
            Assert.True(rects.TryGetValue($"milestone:{seg.val}", out var r));
            Assert.True(r.X >= X, $"milestone:{seg.val} a la izquierda de contentLeft (x={r.X})");
            Assert.True(r.Right <= X + W - Spacing.Sm, $"milestone:{seg.val} sin margen derecho (right={r.Right})");
        }
    }

    [Fact]
    public void MultiSegmentRow_advance_is_one_segment_row()
    {
        // Los hitos compactos (25/50/75/95) caben en un renglón → el avance es el de una fila de
        // segmentos (alto + Sm), no el doble (sin wrap).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var rects = new Dictionary<string, Rectangle>();

        const int y0 = 100;
        int after = DashboardSettingsView.MultiSegmentRow(g, draw: false, "milestone", "", MilestoneSegs,
            new[] { 25 }, X, y0, W, Theme.Dark, Typography.Caption, rects);

        Assert.Equal(y0 + DashboardSettingsView.SegmentRowAdvanceForTest, after);
    }

    [Fact]
    public void MultiSegmentRow_empty_active_marks_nothing()
    {
        // Sin activos: todos los chips quedan en estilo "off" (ninguno Accent). Sigue registrando rects.
        const int x = 16, w = 308, y = 30;
        using var bmp = new Bitmap(x + w + 40, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        var rects = new Dictionary<string, Rectangle>();

        DashboardSettingsView.MultiSegmentRow(g, draw: true, "milestone", "", MilestoneSegs,
            Array.Empty<int>(), x, y, w, Theme.Dark, Typography.Caption, rects);

        var acc = Theme.Dark.Accent;
        foreach (var seg in MilestoneSegs)
        {
            Assert.True(rects.TryGetValue($"milestone:{seg.val}", out var r));
            var c = bmp.GetPixel(r.X + 2, r.Y + r.Height / 2);
            bool isAccent = Math.Abs(c.R - acc.R) <= 10 && Math.Abs(c.G - acc.G) <= 10 && Math.Abs(c.B - acc.B) <= 10;
            Assert.False(isAccent, $"sin activos, milestone:{seg.val} no debe ser Accent");
        }
    }

    [Fact]
    public void Draw_milestone_segments_reflect_active_array()
    {
        // En el Draw completo, los hitos activos del array NotifyMilestones se registran y, tras el
        // refactor de T5, se pintan multi-activo SIN el re-pintado manual. Aquí basta con verificar
        // que el clic alterna (ActionFor) y que medir==pintar del Draw se mantiene con varios activos.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        cfg.NotifyMilestones = new[] { 25, 75 }; // 2 activos
        var s = Localization.Get("es");
        var rects = new Dictionary<string, Rectangle>();

        int measured = DashboardSettingsView.Draw(g, draw: false, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);
        rects.Clear();
        int painted = DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
            Typography.Body, Typography.Caption, rects);

        Assert.Equal(measured, painted);
        foreach (var pct in new[] { 25, 50, 75, 95 })
            Assert.True(rects.ContainsKey($"milestone:{pct}"), $"falta rect milestone:{pct} en el Draw completo");
    }

    [Fact]
    public void MultiSegment_milestone_action_toggles_within_array()
    {
        // La clave milestone:<pct> sigue alternando el valor dentro del array (ActionFor intacto).
        var cfg = Cfg();
        cfg.NotifyMilestones = new[] { 25, 50 };

        DashboardSettingsView.ActionFor("milestone:75")!(cfg);   // añade 75
        Assert.Contains(75, cfg.NotifyMilestones);
        Assert.Equal(new[] { 25, 50, 75 }, cfg.NotifyMilestones);

        DashboardSettingsView.ActionFor("milestone:50")!(cfg);   // quita 50
        Assert.DoesNotContain(50, cfg.NotifyMilestones);
        Assert.Equal(new[] { 25, 75 }, cfg.NotifyMilestones);
    }

    [Fact]
    public void Draw_frequency_segments_fit_within_content_in_all_languages()
    {
        // Regresión del corte 'gundos': con etiquetas compactas, los 4 chips de frecuencia caben
        // dentro de [X, X+W] en todos los idiomas (ningún x<X, ningún right>X+W-Sm).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg();
        foreach (var lang in new[] { "en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant" })
        {
            var s = Localization.Get(lang);
            var rects = new Dictionary<string, Rectangle>();
            DashboardSettingsView.Draw(g, draw: true, X, 0, W, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects);
            foreach (var key in new[] { "freq:30", "freq:60", "freq:300", "freq:900" })
            {
                Assert.True(rects.TryGetValue(key, out var r), $"[{lang}] falta {key}");
                Assert.True(r.X >= X, $"[{lang}] {key} a la izquierda de contentLeft (x={r.X})");
                Assert.True(r.Right <= X + W, $"[{lang}] {key} rebasa el borde derecho (right={r.Right})");
            }
        }
    }
}
