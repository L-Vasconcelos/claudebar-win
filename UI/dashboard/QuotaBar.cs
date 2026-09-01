using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Barra de cuota unificada (cuerpo + cabecera). Sustituye a las gemelas casi idénticas
/// <c>DashboardDataView.DrawBar</c> y <c>DashboardHeader.DrawCriticalBar</c>: una sola rutina
/// que ambas llaman, para que toda señal nueva (pace marker, ticks de umbral) se escriba una vez.
/// Conserva la simetría medir(draw=false)/pintar(draw=true): avanza y devuelve el mismo <c>y</c>.
/// </summary>
public static class QuotaBar
{
    // T11: alto/radio de la barra en px de diseño (96 DPI) proyectados al DPI vigente — antes eran
    // const y al 125/150% la barra quedaba fina respecto al texto crecido. A factor 1.0, idénticos.
    // v0.3.9 "meter": barra más alta y bloques más gruesos (look LED/segmentado) + fila superior más
    // alta para el número grande. Internal (no private) para que los tests deriven sus offsets de
    // AQUÍ en vez de repetir literales mágicos que se desincronizarían en el próximo ajuste visual.
    internal static int BarH => Dpi.Scale(20);
    private static int BarRadius => Dpi.Scale(3);
    /// <summary>Alto de la fila etiqueta + número grande (antes 22, T11).</summary>
    // v0.4 "meter": 52 (antes 40) — Typography.Hero a 28pt tiene una línea real de ~48px, más alta que
    // los 40px reservados; el sobrante lo tapaba la barra (bug real: número/sufijo recortados por la
    // barra, reproducido con --render-test). 52 deja aire por debajo del número.
    internal static int LabelRowH => Dpi.Scale(52);
    /// <summary>Aire entre la barra y la línea de reset (antes 3).</summary>
    internal static int BarBottomGap => Dpi.Scale(6);
    /// <summary>Alto reservado para la línea de reset (antes 14, T11).</summary>
    internal static int ResetRowH => Dpi.Scale(14);
    /// <summary>Bloques del look "segmentado/LED" del tramo lleno (estético).</summary>
    private const int Segments = 14;

    /// <summary>
    /// Dibuja etiqueta + % + barra + línea de reset y devuelve el nuevo y.
    /// El color sigue el criterio de F1: PaceStatus→Ok/Warn/Critical, con fallback a <see cref="ColorMath.RiskColor"/>.
    ///
    /// <para>F3 (tween): <paramref name="displayUtil"/> es el override <b>eased</b> de la utilización para
    /// el <b>ancho de relleno y el número</b>; el <b>color se calcula con la utilización objetivo</b>
    /// (<c>win.UtilizationPct</c>) para que no parpadee de color durante el tween. Si es <c>null</c>
    /// (render-test, reduce-motion, cabecera sin motion) ⇒ comportamiento idéntico a hoy (usa <c>util</c>).</para>
    /// </summary>
    /// <param name="meterStyle">
    /// v0.4: estilo de la vista "meter" (referencia del usuario) — número grande + "% usado" en
    /// sufijo pequeño (en vez de "número%" todo del mismo tamaño) y countdown/reset en formato reloj
    /// ("03:15:51"/"4d 00:15" y "15:00" sin día de la semana). Default false: comportamiento previo,
    /// usado por cualquier otro consumidor de QuotaBar (p.ej. tests, futura vista completa).
    /// </param>
    public static int Draw(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim,
        double? displayUtil = null, bool meterStyle = false)
    {
        double util = win?.UtilizationPct ?? 0;
        // Número y ancho usan el valor eased (si lo hay); el color usa SIEMPRE el objetivo (sin arcoíris).
        double shown = displayUtil ?? util;
        double clamped = Math.Min(shown / 100.0, 1.0);
        // F4 (v0.3.9 g2): el RELLENO va por % REAL (RiskColor) — más lleno = más cálido, longitud y color
        // coherentes. Antes se coloreaba por PACE: una barra al 57% salía ROJA y otra al 84% VERDE en la
        // misma columna (el color contradecía la longitud). La señal de ritmo se mueve al pace-marker ▾
        // (más abajo), coloreado por PaceStatus. El relleno usa SIEMPRE el objetivo (no parpadea con el tween).
        // v0.4 "meter": dos tonos planos (dorado hasta el umbral crítico, rojo desde ahí) en vez del
        // degradado continuo Ok→Warn→Critical — la referencia del usuario no tiene un tercer estado
        // "verde", solo dorado/rojo. Solo aplica con meterStyle; el resto de consumidores conserva el
        // degradado continuo (RiskColor) sin cambios.
        Color c = meterStyle
            ? (util >= cfg.CriticalThresholdPct ? theme.Critical : theme.Warn)
            : ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        // Estado por forma (a11y, daltónicos): por UMBRAL de % real, coherente con el relleno — mismo
        // mapeo color↔forma que el tray. El ritmo (pace) ya no secuestra ni el relleno ni el glifo.
        UsageStatus status = util >= cfg.CriticalThresholdPct ? UsageStatus.Critical
            : util >= cfg.WarnThresholdPct ? UsageStatus.Warn : UsageStatus.Ok;
        // El TEXTO (glifo + %) usa la variante AA del color de estado por % real (T6b): como texto pequeño
        // Critical oscuro caía a 3.7:1 y Warn claro a 2.8:1. Sigue el % (no el pace), igual que el relleno.
        // meterStyle: el mismo criterio de dos tonos que el relleno (sin el "Ok" verde intermedio), para
        // que el número/glifo queden del MISMO color que la barra en vez de desentonar con el verde.
        Color textColor = meterStyle
            ? (util >= cfg.CriticalThresholdPct ? theme.CriticalText : theme.WarnText)
            : Theme.PaceTextColor(theme, StatusToPace(status));

        if (draw)
        {
            g.DrawString(label, labelFont, fg, x, y);
            // v0.3.9 "meter": número GRANDE y en negrita (antes mono pequeño junto al glifo). El % con
            // la cultura del idioma elegido (T2): "12.5%" en inglés, "12,5%" en español.
            // v0.4: en meterStyle NO se pinta el glifo de forma (●/▲/◆) — la referencia del usuario no
            // lo lleva, y el glifo (con métricas de línea muy distintas a un dígito) quedaba flotando
            // desalineado arriba del número al alinearlo por altura de línea (bug real, --render-test).
            // Fuera de meterStyle se conserva sin cambios (glifo + "%" en un solo tamaño, como antes).
            string glyph = meterStyle ? "" : Tray.ShapeGlyph(Tray.ShapeFor(status)) + " ";
            using var bigBrush = new SolidBrush(textColor);
            using var glyphBrush = new SolidBrush(textColor);
            using var suffixBrush = new SolidBrush(textColor);

            // v0.4 "meter": el número va SOLO en grande ("47") y "% usado" cuelga como sufijo chico a
            // la derecha (referencia del usuario) — antes "47%" entero iba al mismo tamaño.
            string bigNum = meterStyle ? UsageFormat.PercentNumber(shown, s.Culture) : UsageFormat.Percent(shown, s.Culture);
            string suffix = meterStyle ? $"% {s.MeterUsedSuffix}" : "";

            // v0.4: número más chico que el Typography.Hero original (28pt) — 22pt pedido tras ver el
            // print final. Font LOCAL (no el Typography.Hero cacheado) para no afectar a otros
            // consumidores futuros, y ya escalado por "Tamaño del panel" (Dpi.UserScale): a 85% el
            // número seguía a tamaño completo mientras la geometría se encogía (bug real reportado).
            const float BigFontPt = 22f;
            using var bigFont = new Font(Typography.Hero.FontFamily, BigFontPt * Dpi.UserScale, Typography.Hero.Style);
            {
                float bigW = g.MeasureString(bigNum, bigFont, int.MaxValue, TextMetrics.Typographic).Width;
                float suffixW = suffix.Length > 0
                    ? g.MeasureString(suffix, smallFont, int.MaxValue, TextMetrics.Typographic).Width : 0;
                float glyphW = glyph.Length > 0
                    ? g.MeasureString(glyph, smallFont, int.MaxValue, TextMetrics.Typographic).Width : 0;

                // Alineado por LÍNEA DE BASE real (no por la caja de GetHeight): la caja completa de una
                // fuente incluye "leading" por encima del ascent, así que anclar el sufijo al fondo de
                // esa caja lo dejaba flotando por encima de donde realmente termina la tinta del número
                // (pedido de ajuste: "alinha com o % usado"). BaselineOffset da la distancia real
                // top→línea-base a partir de las métricas de diseño de la fuente (ascent/line-spacing),
                // así que colocando cada texto en "y + suOffset" ambas líneas de base COINCIDEN.
                float bigBaseline = y + BaselineOffset(bigFont, g);
                float suffixY = bigBaseline - BaselineOffset(smallFont, g);

                // Orden de derecha a izquierda: sufijo "% usado" (si hay) → número grande → glifo chico,
                // con un hueco de seguridad entre cada tramo. Typography.Hero pide FontStyle.Bold sobre
                // una familia sin peso bold real ⇒ GDI+ lo simula RE-TRAZANDO el contorno más grueso SIN
                // ensanchar el ancho de avance que MeasureString reporta (bigW salía sistemáticamente
                // por debajo del ancho realmente pintado) — por eso el sufijo quedaba MONTADO sobre el
                // número (bug real, --render-test). El hueco absorbe ese margen de error.
                float gap = suffix.Length > 0 ? Dpi.Scale(6) : 0;
                float suffixX = x + w - suffixW;
                float bigX = suffixX - gap - bigW;
                g.DrawString(bigNum, bigFont, bigBrush, bigX, y, TextMetrics.Typographic);
                if (suffix.Length > 0)
                    g.DrawString(suffix, smallFont, suffixBrush, suffixX, suffixY, TextMetrics.Typographic);
                g.DrawString(glyph, smallFont, glyphBrush, bigX - gap - glyphW, suffixY, TextMetrics.Typographic);
            }
        }
        y += LabelRowH;

        if (draw)
        {
            using var trackBrush = new SolidBrush(theme.Track);
            Shapes.FillRounded(g, trackBrush, new Rectangle(x, y, w, BarH), BarRadius);
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                // Look "segmentado/LED": el tramo lleno se pinta en BLOQUES con un hueco de Track entre
                // ellos (no un rectángulo continuo) — el hueco deja ver el Track ya pintado arriba, así
                // el sampler de color de los tests (que descarta píxeles Track) sigue promediando solo
                // píxeles de relleno real y no se ve afectado por el nuevo look.
                int segGap = Math.Max(1, Dpi.Scale(3));
                float segW = (w - segGap * (Segments - 1)) / (float)Segments;
                using var fillBrush = new SolidBrush(c);
                for (int i = 0; i < Segments; i++)
                {
                    float segX = x + i * (segW + segGap);
                    if (segX >= x + fw) break;                         // bloque fuera del % lleno
                    float segRight = Math.Min(segX + segW, x + fw);    // recorta el último bloque parcial
                    int sw = (int)Math.Round(segRight - segX);
                    if (sw > 0)
                        Shapes.FillRounded(g, fillBrush, new Rectangle((int)Math.Round(segX), y, sw, BarH), Dpi.Scale(2));
                }
            }

            // Ticks de umbral: muescas finas (1px) en Warn/Critical, tras el relleno para no quedar
            // tapadas por las esquinas redondeadas. Neutro (theme.TickOnTrack — Separator era ≈ Track
            // en los 3 temas y el tick desaparecía, T3b), nunca Accent.
            using (var tickPen = new Pen(theme.TickOnTrack, 1f))
            {
                int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
                int cx = QuotaBarGeometry.TickX(x, w, cfg.CriticalThresholdPct);
                g.DrawLine(tickPen, wx, y, wx, y + BarH - 1);
                g.DrawLine(tickPen, cx, y, cx, y + BarH - 1);
            }

            // Pace marker: "dónde deberías ir" según el ritmo ideal. Sobresale MarkerOvershoot (2px)
            // arriba/abajo y lleva un ▾ clampado a esa misma altura: el triángulo antiguo subía hasta
            // y-5 e invadía los descendentes de la fila etiqueta/% (T3c). Solo cuando hay pace.
            // F4 (v0.3.9 g2): el marcador ES AHORA la señal de RITMO — se colorea por PaceStatus con la
            // variante AA del color (PaceTextColor: Ok→Ok, Over→WarnText, Critical→CriticalText) para que
            // el ritmo siga siendo glanceable en los 3 temas sin secuestrar el color del relleno (% real).
            if (pace is { } pm)
            {
                int mx = QuotaBarGeometry.MarkerX(x, w, pm.IdealPct);
                Color markerColor = Theme.PaceTextColor(theme, pm.Status);
                using var markerPen = new Pen(markerColor, 2f);
                g.DrawLine(markerPen, mx, y - QuotaBarGeometry.MarkerOvershoot, mx, y + BarH + 1);
                using var markerBrush = new SolidBrush(markerColor);
                g.FillPolygon(markerBrush, QuotaBarGeometry.PaceTriangle(mx, y));
            }
        }
        y += BarH + BarBottomGap;

        // v0.4 "meter": countdown/reset en formato reloj ("03:15:51"/"4d 00:15" y "15:00" sin día de la
        // semana), la referencia del usuario — el resto de consumidores conserva "2h 13m"/"ddd HH:mm".
        string cd = meterStyle
            ? UsageFormat.CountdownClock(win?.ResetsAt, s.Resetting)
            : UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        if (draw)
        {
            // dos columnas — hora absoluta de reset a la izquierda, countdown en mono a la derecha
            // (antes una sola línea combinada). T8c: la izquierda se elide al ancho de la fila (locales
            // largos / panel angosto la desbordaban); la derecha SIEMPRE termina en x+w (DrawRight), así
            // que nunca pinta más allá del ancho de la fila.
            string abs = meterStyle
                ? UsageFormat.ResetAbsoluteTimeOnly(win?.ResetsAt, s.Culture)
                : UsageFormat.ResetAbsolute(win?.ResetsAt, s.Culture);
            string left = abs.Length > 0 ? $"{s.ResetsIn} {abs}" : s.ResetsIn;
            left = TextWrap.FitLine(left, x, x + w, 0, t => g.MeasureString(t, smallFont).Width);
            g.DrawString(left, smallFont, dim, x, y);
            if (cd.Length > 0)
            {
                // T14: Typography.Mono escalado por UserScale para acompanhar o resize.
                bool scm = Math.Abs(Dpi.UserScale - 1f) >= 0.001f;
                using var scaledMono = scm ? new Font(Typography.Mono.FontFamily, Typography.Mono.SizeInPoints * Dpi.UserScale, Typography.Mono.Style) : null;
                Font mono = scaledMono ?? Typography.Mono;
                TextMetrics.DrawRight(g, cd, mono, dim, x + w, y);
            }
        }
        return y + ResetRowH;
    }

    /// <summary>
    /// Distancia en px, desde el TOP de la caja de línea de <paramref name="f"/>, hasta su línea de
    /// base real — a partir de las métricas de DISEÑO de la fuente (ascent/line-spacing, en unidades de
    /// em independientes del punto/píxel), aplicadas sobre <see cref="Font.GetHeight(Graphics)"/> (la
    /// línea completa ya en píxeles para este <paramref name="g"/>). Dibujar dos fuentes de tamaño
    /// distinto en <c>y + BaselineOffset</c> dos dibuja con la MISMA línea de base, en vez de compartir
    /// solo el tope o el fondo de su caja (que no coinciden entre fuentes de tamaño distinto).
    /// </summary>
    private static float BaselineOffset(Font f, Graphics g)
    {
        var fam = f.FontFamily;
        return f.GetHeight(g) * fam.GetCellAscent(f.Style) / fam.GetLineSpacing(f.Style);
    }

    /// <summary>
    /// Mapea el estado de cuota por % real (<see cref="UsageStatus"/>) al estado de pace que entiende
    /// <see cref="Theme.PaceTextColor"/>, para reutilizar sus variantes AA de texto (Ok/WarnText/CriticalText)
    /// en el % y el glifo de la barra. F4: el texto sigue el % real, no el ritmo (Warn≡Over como texto).
    /// </summary>
    private static PaceStatus StatusToPace(UsageStatus s) => s switch
    {
        UsageStatus.Critical => PaceStatus.Critical,
        UsageStatus.Warn => PaceStatus.Over,
        _ => PaceStatus.Ok
    };
}

/// <summary>
/// Geometría pura y testeable de la barra de cuota: proyección de un porcentaje al eje X de la barra.
/// Sin estado ni dependencias de GDI+; usada por el pace marker y los ticks de umbral (Tarea 3).
/// </summary>
public static class QuotaBarGeometry
{
    /// <summary>
    /// X (px) del marcador para un porcentaje <paramref name="pct"/> dentro de la barra que arranca en
    /// <paramref name="x"/> con ancho <paramref name="w"/>. Recorta el resultado a [x, x+w].
    /// </summary>
    public static int MarkerX(int x, int w, double pct)
    {
        double p = Math.Clamp(pct, 0.0, 100.0);
        return x + (int)Math.Round(w * p / 100.0);
    }

    /// <summary>X (px) de un tick de umbral; comparte la proyección de <see cref="MarkerX"/>.</summary>
    public static int TickX(int x, int w, double thresholdPct) => MarkerX(x, w, thresholdPct);

    /// <summary>
    /// Overshoot máximo del pace marker por encima de la barra (px): el mismo que ya usa la línea
    /// vertical. Es el techo del clamp del ▾ (T3c): nada del marcador sube más que esto.
    /// </summary>
    public const int MarkerOvershoot = 2;

    /// <summary>
    /// Puntos del ▾ del pace marker, clampados a la fila de la barra (T3c): la base queda en
    /// <c>barY - MarkerOvershoot</c> (donde ya arrancaba la línea) y la punta entra 1px en la barra,
    /// apuntando hacia abajo. El triángulo antiguo (base en barY-5) invadía la fila del label.
    /// </summary>
    public static Point[] PaceTriangle(int mx, int barY) => new[]
    {
        new Point(mx - 3, barY - MarkerOvershoot),
        new Point(mx + 3, barY - MarkerOvershoot),
        new Point(mx, barY + 1),
    };

    /// <summary>
    /// Ancho del track de una fila compacta (mini-cuota con el % right-aligned en la MISMA banda
    /// vertical): el track se corta <paramref name="gap"/> px antes del texto para no tacharlo (T3a).
    /// Nunca negativo (locales largos / panel estrecho ⇒ el track desaparece antes que pisar el texto).
    /// </summary>
    public static int CompactTrackWidth(int w, int rightTextW, int gap) => Math.Max(0, w - rightTextW - gap);
}
