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

    // ================= T4a: colisión de etiquetas del eje X (filtro de espaciado mínimo) =================

    [Fact]
    public void Axis_labels_without_collision_are_all_kept()
    {
        var labels = new (float x, float w)[] { (0, 30), (40, 30), (80, 30) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 1, 2 }, keep);
    }

    [Fact]
    public void Axis_label_overlapping_the_previous_kept_is_skipped()
    {
        // Caso real del render auditado (§3 #1): la 1ª etiqueta anclada a plotLeft [0,46] y la 2ª
        // centrada bajo su tick arrancando en 33 → se superponían ("lun 1{2/2}h"). La 3ª (89) sí cabe.
        var labels = new (float x, float w)[] { (0, 46), (33, 46), (89, 46) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 2 }, keep);
    }

    [Fact]
    public void Skipped_axis_label_does_not_block_the_next_one()
    {
        // El hueco se mide contra la última MANTENIDA, no contra la saltada: 1 colisiona con 0 (28 <
        // 30+4) y se salta; 2 (36 ≥ 34) cabe respecto a 0 aunque pisara a la saltada.
        var labels = new (float x, float w)[] { (0, 30), (28, 30), (36, 30) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0, 2 }, keep);
    }

    [Fact]
    public void First_axis_label_is_always_kept()
    {
        var labels = new (float x, float w)[] { (10, 200) };
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        Assert.Equal(new[] { 0 }, keep);
    }

    [Fact]
    public void Kept_axis_labels_never_overlap_for_dense_input()
    {
        // Propiedad: tras el filtro, cada etiqueta mantenida respeta el gap con la anterior mantenida.
        var labels = new List<(float x, float w)>();
        for (int i = 0; i < 20; i++) labels.Add((i * 17f, 40f)); // densas: cada una pisa a la siguiente
        var keep = DashboardDataView.VisibleAxisLabels(labels, minGap: 4);
        for (int k = 1; k < keep.Count; k++)
        {
            var prev = labels[keep[k - 1]];
            var cur = labels[keep[k]];
            Assert.True(cur.x >= prev.x + prev.w + 4,
                $"etiquetas {keep[k - 1]} y {keep[k]} mantenidas pero solapadas ({prev.x + prev.w} → {cur.x})");
        }
    }

    // ================= T4b: la etiqueta "peak …" no debe pisar el punto del pico =================

    // Geometría compartida con producción: punto de r=2.5 px centrado en (peakX, peakY).
    private static RectangleF MarkerRect(float peakX, float peakY) => new(peakX - 2.5f, peakY - 2.5f, 5, 5);

    [Fact]
    public void Peak_label_clears_the_marker_when_peak_is_in_the_last_bucket()
    {
        // §3 #9: pico en el último bucket ⇒ etiqueta clavada al borde derecho (clamp) con el punto
        // ENCIMA del texto ("peak $41,81" pisado). En modo $ el pico siempre cae en la fila del clamp
        // superior (peakY = top+14), así que el rect centrado intersecta el punto → debe apartarse.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 310, peakY: 34, label, x: 10, w: 300, top: 20,
            avoid: new RectangleF(10, 19, 60, 15));
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(310, 34)),
            $"la etiqueta {rect} sigue pisando el punto del pico {MarkerRect(310, 34)}");
        Assert.True(pos.X >= 10 && pos.X + label.Width <= 310, $"se sale del plot: x={pos.X}");
    }

    [Fact]
    public void Peak_label_stays_centered_above_the_marker_when_there_is_room()
    {
        // Pico a media altura y centrado: la etiqueta queda centrada sobre el punto, sin tocarlo.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 160, peakY: 60, label, x: 10, w: 300, top: 20,
            avoid: new RectangleF(10, 19, 60, 15));
        Assert.Equal(160 - 35, pos.X);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(160, 60)), "centrada pero pisando el punto");
        Assert.True(pos.Y + label.Height <= 60 - 2.5f, $"el borde inferior ({pos.Y + label.Height}) entra en el punto");
    }

    [Fact]
    public void Peak_label_goes_right_of_the_marker_when_there_is_no_room_left()
    {
        // Pico clavado al borde izquierdo en la fila del clamp: a la izquierda no cabe → a la derecha.
        var label = new SizeF(70, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 12, peakY: 34, label, x: 10, w: 300, top: 20,
            avoid: RectangleF.Empty);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(MarkerRect(12, 34)), $"etiqueta {rect} sobre el punto");
        Assert.True(pos.X >= 12 + 2.5f, $"debía colocarse a la derecha del punto (x={pos.X})");
    }

    [Fact]
    public void Peak_label_still_drops_below_when_colliding_with_topleft_label()
    {
        // Comportamiento previo conservado: si la etiqueta del pico cae sobre el total/% actual
        // (esquina superior izquierda), baja bajo su marcador.
        var label = new SizeF(70, 15);
        var avoid = new RectangleF(10, 19, 80, 15);
        var pos = DashboardDataView.PeakLabelPos(peakX: 40, peakY: 34, label, x: 10, w: 300, top: 20, avoid);
        var rect = new RectangleF(pos.X, pos.Y, label.Width, label.Height);
        Assert.False(rect.IntersectsWith(avoid), $"etiqueta {rect} sigue sobre la fija {avoid}");
        Assert.True(pos.Y >= 34 + 2.5f, $"debía caer bajo el marcador (y={pos.Y})");
    }

    [Fact]
    public void Peak_label_never_leaves_the_plot()
    {
        var label = new SizeF(70, 15);
        for (float px = 10; px <= 310; px += 7)
        {
            var pos = DashboardDataView.PeakLabelPos(px, peakY: 34, label, x: 10, w: 300, top: 20,
                avoid: new RectangleF(10, 19, 60, 15));
            Assert.True(pos.X >= 10 && pos.X + label.Width <= 310,
                $"peakX {px}: etiqueta fuera del plot (x={pos.X})");
        }
    }

    // ================= T4d: color del pico unificado ($ y % usan Foreground) =================

    [Fact]
    public void Spend_peak_annotation_uses_foreground_like_percent_mode()
    {
        // §T4d: en modo $ el marcador/etiqueta del pico caían al pincel dim (theme era null en el
        // body de gasto) mientras el modo % usaba Foreground: mismo dato, dos jerarquías. Unificado:
        // el band del pico debe contener píxeles ~Foreground (≥200 en los 3 canales; dim ≈ 161-170).
        using var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        double[] costs = { 1, 2, 10, 2, 1 };               // pico en el centro
        for (int i = 0; i < costs.Length; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"{i:00}h", costs[i], 0, 0, 0));

        const int x = 10, top = 20, w = 300;
        DashboardDataView.DrawSpendBody(g, draw: true, x, top, w, s, Theme.Dark, Typography.Caption,
            data, chartLoading: false, dim);

        // Banda del pico (etiqueta arriba, hacia el centro-derecha; el total dim queda a la izquierda).
        bool found = false;
        for (int py = top - 2; py <= top + 16 && !found; py++)
            for (int px = x + w / 3; px <= x + w && !found; px++)
            {
                var p = bmp.GetPixel(px, py);
                if (p.R >= 200 && p.G >= 200 && p.B >= 200) found = true;
            }
        Assert.True(found, "el pico en modo $ debe pintarse con Foreground (como el modo %), no con dim");
    }

    [Fact]
    public void Spend_body_measure_equals_paint_with_chart_data()
    {
        // Invariante medir==pintar: el filtro de etiquetas y el recolocado del pico solo actúan en la
        // pasada de pintado; ambas pasadas devuelven el MISMO y final.
        using var bmp = new Bitmap(360, 160);
        using var g = Graphics.FromImage(bmp);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var s = Localization.Get("en");
        var t0 = new DateTime(2026, 6, 1);
        var data = new List<HistoryBucket>();
        for (int i = 0; i < 12; i++)
            data.Add(new HistoryBucket(t0.AddHours(i), $"lun {i:00}h", 5 + i, 1, 0.5, 0));

        int measured = DashboardDataView.DrawSpendBody(g, draw: false, 10, 20, 300, s, Theme.Dark,
            Typography.Caption, data, chartLoading: false, dim);
        int painted = DashboardDataView.DrawSpendBody(g, draw: true, 10, 20, 300, s, Theme.Dark,
            Typography.Caption, data, chartLoading: false, dim);
        Assert.Equal(measured, painted);
    }

    // ================= T7a (§3 #5): tabs de rango 1H/5H/24H/7D/30D = chips DrawSegments =================

    // Llama a DrawChart con datos vacíos (las tabs se registran igual) y devuelve los 3 dicts de rects.
    private static (Dictionary<ChartRange, Rectangle> tabs, Dictionary<string, Rectangle> modes)
        DrawChartTabs(Graphics g, bool draw, Theme theme)
    {
        var s = Localization.Get("en");
        var cfg = new AppConfig();
        using var tabFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        var tabRects = new Dictionary<ChartRange, Rectangle>();
        var modeRects = new Dictionary<string, Rectangle>();
        var pctWinRects = new Dictionary<string, Rectangle>();
        using var dim = new SolidBrush(theme.TextSecondary);
        DashboardDataView.DrawChart(g, draw, 10, 20, 320, s, theme, cfg, Typography.Caption, tabFont,
            "spend", ChartRange.Hours5, "7d", new List<HistoryBucket>(), new List<PctPoint>(),
            chartLoading: false, tabRects, modeRects, pctWinRects, dim);
        return (tabRects, modeRects);
    }

    [Fact]
    public void Chart_range_tabs_share_chip_geometry_with_mode_segments()
    {
        // §3 #5: las tabs de rango tenían geometría propia (alto 20, radio 5, padX 6, gap 4) distinta
        // de los chips vecinos (Spend $/Quota % vía DrawSegments: alto 18, padX 7, gap 3). Unificadas:
        // mismo alto que los segmentos del modo y mismo gap entre chips.
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        var (tabs, modes) = DrawChartTabs(g, draw: false, Theme.Dark);

        Assert.Equal(5, tabs.Count);
        Assert.All(tabs.Values, r => Assert.Equal(18, r.Height));
        Assert.All(modes.Values, r => Assert.Equal(18, r.Height));

        var ordered = tabs.Values.OrderBy(r => r.X).ToList();
        for (int i = 1; i < ordered.Count; i++)
            Assert.Equal(3, ordered[i].X - ordered[i - 1].Right);
    }

    [Fact]
    public void Chart_inactive_range_tab_shows_border_in_light_theme()
    {
        // §3 #5: el chip inactivo era BgElevated puro (#FFF sobre #FAFAFA → invisible en claro). Debe
        // llevar el borde Separator de DrawSegments: en el contorno superior del chip inactivo hay
        // píxeles ≈ Separator (≠ relleno y ≠ fondo).
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Light.Background);
        var (tabs, _) = DrawChartTabs(g, draw: true, Theme.Light);

        var r = tabs[ChartRange.Hour1]; // 1H inactiva (la activa es 5H)
        var sep = Theme.Light.Separator;
        bool border = false;
        for (int px = r.X + 4; px <= r.Right - 4 && !border; px++)
        {
            var p = bmp.GetPixel(px, r.Y);
            if (Math.Abs(p.R - sep.R) <= 8 && Math.Abs(p.G - sep.G) <= 8 && Math.Abs(p.B - sep.B) <= 8)
                border = true;
        }
        Assert.True(border, "la tab de rango inactiva debe llevar el borde Separator (era invisible en claro)");
    }

    [Fact]
    public void Chart_tabs_measure_equals_paint()
    {
        // Invariante de 2 pasadas para DrawChart: medir y pintar registran los MISMOS rects de tabs.
        using var bmp = new Bitmap(360, 240);
        using var g = Graphics.FromImage(bmp);
        var (measured, _) = DrawChartTabs(g, draw: false, Theme.Dark);
        var (painted, _) = DrawChartTabs(g, draw: true, Theme.Dark);

        Assert.Equal(measured.Count, painted.Count);
        foreach (var (k, r) in measured) Assert.Equal(r, painted[k]);
    }

    [Fact]
    public void ModelLine_track_stops_before_the_percent_text()
    {
        // T3a (auditoría §3 #2): el track de la fila compacta cruzaba a todo el ancho por debajo del %
        // right-aligned y lo tachaba (el Mono de 12pt baja hasta la banda de la barrita). Ahora el track
        // se corta un gap ANTES del texto: la banda del gap debe ser fondo puro, sin píxeles de Track.
        using var bmp = new Bitmap(360, 120);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(12, DateTimeOffset.UtcNow);     // 12% como en el render auditado
        var cfg = new AppConfig();
        var culture = Localization.Get("en").Culture;

        const int x = 16, y0 = 30, w = 300;
        DashboardDataView.DrawModelLine(g, draw: true, "Sonnet 7d", win, x, y0, w,
            Typography.Caption, fg, dim, Theme.Dark, cfg, culture);

        // Mide el % igual que producción para localizar dónde arranca el texto right-aligned.
        string val = UsageFormat.Percent(win.UtilizationPct, culture);
        int textLeft = (int)(x + w - g.MeasureString(val, Typography.Mono).Width);

        var track = Theme.Dark.Track;
        for (int py = y0 + 16; py <= y0 + 19; py++)               // banda de la barrita (ModelBarH=4)
            for (int px = textLeft - 7; px <= textLeft - 1; px++) // el gap previo al texto
            {
                var p = bmp.GetPixel(px, py);
                bool isTrack = Math.Abs(p.R - track.R) <= 6 && Math.Abs(p.G - track.G) <= 6 && Math.Abs(p.B - track.B) <= 6;
                Assert.False(isTrack, $"track en ({px},{py}): la barrita sigue cruzando bajo el % (texto en x={textLeft})");
            }
    }
}
