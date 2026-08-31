using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Gauge semicircular de cuota (Mockup 2): reemplaza la barra lineal empilhada por un card con arco
/// que muestra %, estado (●/▲/◆), countdown y hora de reset. Se dibuja lado a lado (5h | 7d) dentro
/// de <see cref="DashboardDataView.DrawQuotaBody"/>. Conserva la simetría medir/pintar y escala con DPI.
/// </summary>
public static class QuotaGauge
{
    // Dimensiones de diseño (96 DPI), escaladas por Dpi.Scale.
    internal static int CardPadding => Dpi.Scale(10);
    internal static int ArcSize => Dpi.Scale(80);          // diámetro del arco
    internal static int ArcStroke => Dpi.Scale(8);          // grosor del trazo
    internal static int NumberFontPt => 22;                 // pt del número grande
    internal static int CardRadius => Dpi.Scale(12);        // radio de las esquinas del card (redondeado como el mockup)
    internal static int BadgeH => Dpi.Scale(16);            // alto del badge de estado
    internal static int ResetLineH => Dpi.Scale(14);        // alto de cada línea de reset
    internal static int CardGap => Dpi.Scale(10);           // gap entre los dos cards

    /// <summary>
    /// Alto total de un card (padding top + arco + número + badge + reset + padding bottom).
    /// Determinista: medir y pintar reservan el mismo espacio.
    /// </summary>
    internal static int CardHeight =>
        CardPadding                        // top padding
        + Dpi.Scale(14)                    // label
        + Dpi.Scale(6)                     // gap label → arco
        + ArcSize / 2 + ArcStroke          // arco semicircular (mitad superior del diámetro + stroke)
        + Dpi.Scale(4)                     // gap arco → número
        + Dpi.Scale(32)                    // número grande
        + Dpi.Scale(4)                     // gap número → badge
        + BadgeH                           // badge de estado
        + Dpi.Scale(4)                     // gap badge → reset
        + ResetLineH                       // countdown
        + ResetLineH                       // hora absoluta
        + CardPadding;                     // bottom padding

    /// <summary>
    /// Dibuja un card de gauge semicircular para una ventana de cuota (5h o 7d).
    /// </summary>
    /// <param name="displayUtil">Override eased (tween) del %; null = valor crudo.</param>
    public static void DrawCard(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace,
        int x, int y, int cardW, AppConfig cfg, Strings s, Theme theme, Font smallFont,
        double? displayUtil = null)
    {
        double util = win?.UtilizationPct ?? 0;
        double shown = displayUtil ?? util;

        // Color por % real (no por pace), coherente con QuotaBar.
        Color fillColor = ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        UsageStatus status = util >= cfg.CriticalThresholdPct ? UsageStatus.Critical
            : util >= cfg.WarnThresholdPct ? UsageStatus.Warn : UsageStatus.Ok;
        Color textColor = Theme.PaceTextColor(theme, StatusToPace(status));

        if (!draw) return; // el alto es fijo (CardHeight), no depende del contenido

        // --- Fondo del card (BgElevated + borde) ---
        var cardRect = new Rectangle(x, y, cardW, CardHeight);
        using (var bg = new SolidBrush(theme.BgElevated))
            Shapes.FillRounded(g, bg, cardRect, CardRadius);
        using (var border = new Pen(theme.Separator))
            Shapes.DrawRounded(g, border, cardRect, CardRadius);

        int cx = x + cardW / 2;  // centro horizontal del card
        int cy = y + CardPadding;

        // --- Label ("Session (5h)" / "Week (7d)") ---
        using var dimBrush = new SolidBrush(theme.TextSecondary);
        var labelSize = g.MeasureString(label, smallFont);
        g.DrawString(label, smallFont, dimBrush, cx - labelSize.Width / 2, cy);
        cy += Dpi.Scale(14) + Dpi.Scale(6);

        // --- Arco semicircular ---
        int arcDiameter = ArcSize;
        int arcX = cx - arcDiameter / 2;
        int arcY = cy;
        int arcH = arcDiameter / 2 + ArcStroke; // solo la mitad superior es visible

        // Track (fondo del arco)
        using (var trackPen = new Pen(theme.Track, ArcStroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(trackPen, arcX, arcY, arcDiameter, arcDiameter, 180, 180);

        // Fill (arco proporcional al %)
        double clamped = Math.Clamp(shown / 100.0, 0, 1);
        float sweepAngle = (float)(180 * clamped);
        if (sweepAngle > 0.5f)
        {
            // Gradiente: el arco se colorea con un brush lineal de izquierda a derecha
            var gradRect = new Rectangle(arcX - ArcStroke, arcY, arcDiameter + ArcStroke * 2, arcDiameter);
            Color startColor = ColorMath.RiskColor(0, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
            Color endColor = fillColor;
            using var gradBrush = new LinearGradientBrush(gradRect, startColor, endColor, LinearGradientMode.Horizontal);
            using var fillPen = new Pen(gradBrush, ArcStroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(fillPen, arcX, arcY, arcDiameter, arcDiameter, 180, sweepAngle);
        }

        cy += arcH + Dpi.Scale(4);

        // --- Número grande (%) ---
        using var numBrush = new SolidBrush(textColor);
        using var numFont = new Font(Typography.Hero.FontFamily, NumberFontPt * Dpi.UserScale, Typography.Hero.Style);
        string numText = UsageFormat.PercentNumber(shown, s.Culture);
        string suffix = $"% {s.MeterUsedSuffix}";
        var numSize = g.MeasureString(numText, numFont, int.MaxValue, TextMetrics.Typographic);
        var suffixSize = g.MeasureString(suffix, smallFont, int.MaxValue, TextMetrics.Typographic);
        float totalNumW = numSize.Width + Dpi.Scale(3) + suffixSize.Width;
        float numX = cx - totalNumW / 2;
        g.DrawString(numText, numFont, numBrush, numX, cy, TextMetrics.Typographic);

        // Sufijo "% used" alineado por baseline
        float numBaseline = cy + BaselineOffset(numFont, g);
        float suffixY = numBaseline - BaselineOffset(smallFont, g);
        using var suffixBrush = new SolidBrush(textColor);
        g.DrawString(suffix, smallFont, suffixBrush, numX + numSize.Width + Dpi.Scale(3), suffixY, TextMetrics.Typographic);

        cy += Dpi.Scale(32) + Dpi.Scale(4);

        // --- Badge de estado (● OK / ▲ WARN / ◆ CRIT) ---
        string glyph = Tray.ShapeGlyph(Tray.ShapeFor(status));
        string statusLabel = status switch
        {
            UsageStatus.Critical => "CRIT",
            UsageStatus.Warn => "WARN",
            _ => "OK"
        };
        string badgeText = $"{glyph} {statusLabel}";
        var badgeSize = g.MeasureString(badgeText, smallFont);
        int badgeW = (int)badgeSize.Width + Dpi.Scale(12);
        int badgeX = cx - badgeW / 2;
        var badgeRect = new Rectangle(badgeX, cy, badgeW, BadgeH);
        Color badgeBg = Color.FromArgb(30, fillColor);
        using (var badgeBgBrush = new SolidBrush(badgeBg))
            Shapes.FillRounded(g, badgeBgBrush, badgeRect, Dpi.Scale(6));
        using var badgeFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var badgeBrush = new SolidBrush(textColor);
        g.DrawString(badgeText, smallFont, badgeBrush, badgeRect, badgeFmt);

        cy += BadgeH + Dpi.Scale(4);

        // --- Reset info (countdown + hora absoluta) ---
        string countdown = UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        string resetAbs = UsageFormat.ResetAbsolute(win?.ResetsAt, s.Culture);

        if (countdown.Length > 0)
        {
            string resetLine = $"↻ {countdown}";
            var resetSize = g.MeasureString(resetLine, Typography.Mono);
            g.DrawString(resetLine, Typography.Mono, dimBrush, cx - resetSize.Width / 2, cy);
        }
        cy += ResetLineH;

        if (resetAbs.Length > 0)
        {
            var absSize = g.MeasureString(resetAbs, smallFont);
            g.DrawString(resetAbs, smallFont, dimBrush, cx - absSize.Width / 2, cy);
        }
    }

    /// <summary>BaselineOffset reutilizado de QuotaBar (misma lógica).</summary>
    private static float BaselineOffset(Font f, Graphics g)
    {
        var fam = f.FontFamily;
        return f.GetHeight(g) * fam.GetCellAscent(f.Style) / fam.GetLineSpacing(f.Style);
    }

    private static PaceStatus StatusToPace(UsageStatus s) => s switch
    {
        UsageStatus.Critical => PaceStatus.Critical,
        UsageStatus.Warn => PaceStatus.Over,
        _ => PaceStatus.Ok
    };
}
