using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.UI;

/// <summary>Cabecera de un vistazo: mascota + estado servicio + cuota crítica + pace. Sin estado.</summary>
public static class DashboardHeader
{
    // -------- tokens de espaciado del header (F12, §minor "espaciado fuera de 8pt") --------
    // Antes la geometría del header eran literales mágicos sueltos (los históricos 50/22/14/6 a 96 DPI →
    // hoy 24/16/8/4/12/10 dentro de Dpi.Scale). Aquí se nombran replicando el patrón de
    // DashboardSettingsView (tokens con nombre + Dpi.Scale para el DPI): MISMOS valores que antes (a
    // factor 1.0 son idénticos → render-test pixel-perfect), ahora legibles y centralizados. Los que
    // caen en la rejilla 8pt derivan de Spacing; los dos fuera de escala (16/10) van con nombre propio.

    /// <summary>Lado del botón ⚙ (cuadrado), en px de diseño escalados al DPI. =Spacing.Xl (24).</summary>
    internal static int GearSize => Dpi.Scale(Spacing.Xl);
    /// <summary>Alto de una fila de una línea del bloque de estado (salud / glance). FUERA de 8pt: 16px
    /// de diseño escalados (el texto de 11–12pt cabe holgado; subir a Xl=24 dejaría hueco muerto).</summary>
    internal static int StatusRowHeight => Dpi.Scale(16);
    /// <summary>Diámetro del punto de salud. =Spacing.Sm (8).</summary>
    internal static int HealthDotSize => Dpi.Scale(Spacing.Sm);
    /// <summary>Inset vertical del punto de salud dentro de su fila (lo centra ópticamente). =Spacing.Xs (4).</summary>
    internal static int HealthDotInsetY => Dpi.Scale(Spacing.Xs);
    /// <summary>Gutter del punto de salud a su texto. =Spacing.Md (12).</summary>
    internal static int HealthDotGutter => Dpi.Scale(Spacing.Md);
    /// <summary>Aire entre el final del bloque de estado y el divisor de "Cuota". =Spacing.Xs (4).</summary>
    internal static int DividerAir => Dpi.Scale(Spacing.Xs);
    /// <summary>Avance final bajo el divisor antes de la sección "Cuota". FUERA de 8pt: 10px de diseño
    /// escalados (separación afinada del divisor a la primera cabecera).</summary>
    internal static int DividerBottomGap => Dpi.Scale(10);

    /// <summary>
    /// Contorno PURO del engranaje (T9c, §3 #12): polígono de 4 puntos por diente — 2 en la cresta
    /// (radio exterior) y 2 en el valle (radio raíz) — con flancos radiales (cresta y valle comparten
    /// ángulo en el salto). El primer diente queda centrado arriba (-90°) para que el icono se vea
    /// simétrico en el botón. Determinista y sin GDI+ → testeable (misma entrada, mismos puntos).
    /// </summary>
    internal static PointF[] GearOutline(float cx, float cy, float rOuter, float rRoot, int teeth)
    {
        var pts = new PointF[teeth * 4];
        double step = Math.PI * 2 / teeth;            // arco total por diente (cresta + valle)
        double half = step / 2;                       // mitad cresta, mitad valle
        double start = -Math.PI / 2 - half / 2;       // cresta del primer diente centrada arriba
        static PointF Polar(float cx, float cy, float r, double a)
            => new(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
        for (int i = 0; i < teeth; i++)
        {
            double a0 = start + i * step;
            pts[i * 4 + 0] = Polar(cx, cy, rOuter, a0);          // arranque de cresta
            pts[i * 4 + 1] = Polar(cx, cy, rOuter, a0 + half);   // fin de cresta
            pts[i * 4 + 2] = Polar(cx, cy, rRoot, a0 + half);    // caída al valle (flanco radial)
            pts[i * 4 + 3] = Polar(cx, cy, rRoot, a0 + step);    // fin de valle
        }
        return pts;
    }

    /// <summary>
    /// Dibuja el engranaje como path GDI+ con anti-aliasing (T9c): silueta de 8 dientes + agujero
    /// central perforado (FillMode.Alternate). Sustituye al glifo "Settings" de Segoe MDL2, que se
    /// rasterizaba como texto ClearType (franjas de color, borroso sobre el botón, peor en tema
    /// claro). Los radios escalan con <paramref name="bounds"/> (exterior ≈ 0.32·lado, como el glifo
    /// de 15px en el botón de 24), así que el icono seguirá nítido cuando el layout escale con el
    /// DPI (T11).
    /// </summary>
    internal static void DrawGear(Graphics g, Rectangle bounds, Color color)
    {
        float cx = bounds.X + bounds.Width / 2f, cy = bounds.Y + bounds.Height / 2f;
        float rOuter = Math.Min(bounds.Width, bounds.Height) * 0.32f;
        float rRoot = rOuter * 0.74f;   // dientes ≈ 26% del radio
        float rHole = rOuter * 0.38f;   // agujero central
        using var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(GearOutline(cx, cy, rOuter, rRoot, teeth: 8));
        path.AddEllipse(cx - rHole, cy - rHole, rHole * 2, rHole * 2); // Alternate → queda perforado
        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var b = new SolidBrush(color);
            g.FillPath(b, path);
        }
        finally { g.SmoothingMode = sm; }
    }

    /// <summary>Dibuja la cabecera y devuelve el nuevo y. Registra el rect del ⚙ en gearRect.</summary>
    /// <param name="mascotBounceOffsetY">
    /// Bote de atención (px ≥ 0): desplaza el dibujo de la mascota hacia arriba dentro de su celda
    /// (vía <c>TranslateTransform</c>). NO cambia el <c>y</c> de layout (medir == pintar). Solo en la
    /// pasada de pintado; con reduce-motion el llamante pasa 0.
    /// </param>
    /// <param name="celebration">
    /// Destello de celebración in-panel (p.ej. "✓ cuota renovada") cuando una ventana se resetea.
    /// <c>null</c> = sin destello. NO toca el sistema de notificaciones (eso es F4).
    /// </param>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
                           AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
                           MascotState mascot, Mood mascotMood, Font labelFont, Font smallFont, Font mono,
                           ref Rectangle gearRect,
                           MotionState? motion = null, bool reduceMotion = false,
                           int mascotBounceOffsetY = 0, string? celebration = null)
    {
        // 1) botón ⚙ arriba a la derecha (siempre): botón con fondo redondeado + engranaje como path
        //    GDI+ con AA (T9c, §3 #12 — el glifo MDL2 se rasterizaba como texto ClearType: borroso y
        //    con franjas de color, peor en tema claro). T11: el botón escala con el DPI (los radios
        //    del path ya eran proporcionales a bounds, así que el icono sigue nítido a 125/150%).
        int gearSize = GearSize;
        var gear = new Rectangle(x + w - gearSize, y, gearSize, gearSize);
        gearRect = gear;
        if (draw)
        {
            using (var bgBrush = new SolidBrush(theme.BgElevated))
                Shapes.FillRounded(g, bgBrush, gear, 7);
            DrawGear(g, gear, theme.TextPrimary);
        }

        // 1b) destello de celebración de reset ("✓ cuota renovada"): chip in-panel breve en la fila
        //     superior, a la izquierda del ⚙. Solo en la pasada de pintado y SIN reservar alto (esa
        //     fila solo aloja el ⚙) → no rompe el invariante medir/pintar. NO es una notificación (F4).
        //     T8e: si el texto no cabe entre x y el ⚙ se ELIDE con elipsis medida (antes chip.X < x
        //     hacía desaparecer el destello entero con textos/locales largos).
        if (draw && !string.IsNullOrEmpty(celebration))
        {
            string flash = "✓ " + celebration;
            int textH = (int)Math.Ceiling(g.MeasureString(flash, smallFont).Height);
            var (shown, chip) = CelebrationChip(flash, x, gear.X, y, gearSize, textH,
                t => g.MeasureString(t, smallFont).Width);
            if (shown.Length > 0)
            {
                // Fondo del chip de celebración subido a alpha 64 (T-v039 F3): a ~36 el destello
                // "✓ cuota renovada" apenas se distinguía del fondo del panel. 64 lo hace presente
                // sin tapar el texto Ok que va encima.
                using var chipBg = new SolidBrush(Color.FromArgb(64, theme.Ok));
                Shapes.FillRounded(g, chipBg, chip, Dpi.Scale(Spacing.Sm));
                using var okBrush = new SolidBrush(theme.Ok);
                g.DrawString(shown, smallFont, okBrush,
                    chip.X + Dpi.Scale(Spacing.Sm), chip.Y + (chip.Height - textH) / 2);
            }
        }

        // 2) mascota en su PROPIA banda a ancho completo (si ShowMascot): sprite (frame del animador) +
        //    verbo localizado JUGUETÓN con elipsis bajo la mascota. El verbo reserva su alto en AMBAS
        //    pasadas (medir/pintar) para no romper el invariante de layout.
        //    P1 #2 (sin colisión): la mascota YA NO comparte fila con el bloque de estado (antes el sprite
        //    robaba ancho a la columna de texto y la línea de salud se cortaba en el borde). Ahora ocupa
        //    una banda propia ARRIBA, con aire (Spacing.Sm) antes del bloque de estado, que pasa a usar el
        //    ancho COMPLETO (textX = x). Adiós al recorte por columna robada.
        //    T0: la visibilidad se DESACOPLA de LiveSessionsEnabled (antes `live && mascota`, el bug):
        //    con ShowMascot on y live off se ve un gato Idle estático (ambiente). Las puertas de
        //    ANIMACIÓN siguen exigiendo live on (bote en SyncBounce, fast-tick/mascotAlive en
        //    DashboardForm; aquí el bounce llega ya gateado y, con draw=false, se fuerza a 0).
        // Margen superior del bloque (T9/§3.4): el contenido arrancaba en y+18 y se solapaba con la fila
        // del ⚙/✕ (24px). Ahora baja a `gearSize + Spacing.Xs` para despegarlo de los botones.
        int top = y + gearSize + Dpi.Scale(Spacing.Xs);
        // El bloque de estado usa el ancho COMPLETO: la mascota ya no comparte su fila (banda propia).
        int textX = x;
        if (cfg.ShowMascot)
        {
            // Bote de atención: desplaza SOLO el dibujo de la mascota hacia arriba (px ≥ 0) dentro de
            // su celda. El offset se mide con draw=false como 0 (no se aplica transform), así medir y
            // pintar reservan el MISMO alto (invariante de layout). Hacia arriba = y negativo.
            int bounce = draw ? mascotBounceOffsetY : 0;
            // try/finally (fix F3 minor, mismo patrón que DashboardDataView.Section): el pop del transform
            // del bote ocurre siempre, aunque MascotRenderer.Draw lance, para no contaminar frames siguientes.
            if (bounce != 0) g.TranslateTransform(0, -bounce);
            Size sz;
            try
            {
                sz = MascotRenderer.Draw(g, draw, x, top, live.GlobalPhase,
                    mascot, theme, mono, mascotMood);
            }
            finally
            {
                if (bounce != 0) g.TranslateTransform(0, bounce);
            }

            // Verbo + elipsis (p.ej. "thinking…"). Alto reservado siempre (medir == pintar).
            string verb = MascotAnimator.Verb(s, live.GlobalPhase, mascot.VerbIndex);
            int verbH = 0;
            if (!string.IsNullOrEmpty(verb))
            {
                verbH = (int)Math.Ceiling(smallFont.GetHeight(g));
                if (draw)
                {
                    using var vb = new SolidBrush(theme.TextMuted);
                    g.DrawString(verb + "…", smallFont, vb, x, top + sz.Height + 2);
                }
            }

            // Banda de la mascota: alto de sprite + verbo. El bloque de estado arranca DEBAJO con aire
            // Spacing.Sm (gutter vertical). Esto suma en AMBAS pasadas (no depende de draw) → medir==pintar.
            int mascotBandH = sz.Height + (verbH > 0 ? verbH + 2 : 0);
            top += mascotBandH + Dpi.Scale(Spacing.Sm);
        }

        // 3) bloque de estado a ancho completo: estado de servicio + UN glance de cuota (sin barra/reset/pace).
        // P0 #4: el header YA NO pinta la barra crítica completa (barra+reset+pace), que DUPLICABA la
        // primera fila de la sección "Cuota" (misma sesión y su pace pintados dos veces) y cuyo pace se
        // cortaba ("⚠ vie 1…") porque la columna de la mascota roba ancho. La "Sesión (5h)" completa vive
        // SOLO bajo "Cuota" (340px, pace entero). Aquí el header es: estado de servicio + un glance de UNA
        // línea (nombre de ventana + forma + %), SIN barra, reset ni pace. Así desaparece el duplicado y el
        // corte de un golpe, manteniendo el invariante medir==pintar (no hay nada dependiente de draw en el y).
        int rightX = x + w;
        int ty = top;
        if (cfg.ShowHealth && snap?.Health is { } h)
        {
            (string hl, Color hc) = h.Level switch
            {
                HealthLevel.Operational => (s.HealthOk, theme.Ok),
                HealthLevel.Degraded => (s.HealthDegraded, theme.Warn),
                HealthLevel.Outage => (s.HealthOutage, theme.Critical),
                _ => (h.Description, theme.TextSecondary)
            };
            if (draw)
            {
                // T11: punto de salud y su sangría escalan con el DPI (solo pintado: no tocan el y).
                using var dot = new SolidBrush(hc);
                g.FillEllipse(dot, textX, ty + HealthDotInsetY, HealthDotSize, HealthDotSize);
                using var b = new SolidBrush(theme.TextSecondary);
                // Estado de servicio ("Operativo"). Anti-corte: se envuelve/encoge dentro del ancho útil
                // (texto corto; FitHeaderLine deja margen derecho ≥ Spacing.Md). Determinista (medir==pintar).
                int hlX = textX + HealthDotGutter;
                string hlShown = FitHeaderLine(hl, hlX, rightX, Dpi.Scale(Spacing.Md), t => g.MeasureString(t, smallFont).Width);
                g.DrawString(hlShown, smallFont, b, hlX, ty);
            }
        }
        ty += StatusRowHeight; // fila de salud (token F12: alto de fila de estado, escala con el DPI)

        // Glance de cuota crítica = ventana de PEOR estado entre 5h/7d (T5a: Critical > Warn > Ok; a
        // igualdad, mayor %. Antes era "mayor %" a secas y el header enseñaba la semana en verde al 84%
        // mientras la sesión iba al 62% con pace CRÍTICO), en UNA línea: "Sesión (5h)" a la izquierda +
        // "◆ 87%" a la derecha (forma + % en color de estado), SIN barra, SIN línea de reset y SIN pace
        // (esos viven enteros en la sección "Cuota"). El % usa el valor eased si hay motion, pero el alto
        // reservado NO depende de draw → medir==pintar.
        // T5b: el glance solo aparece con la sección "Cuota" PLEGADA — expandida, sus barras completas
        // van justo debajo y el glance duplicaba la fila "Week (7d) · 84%" (§3 jerarquía). La condición
        // es de config (igual en ambas pasadas) → medir==pintar intacto.
        var w5 = snap?.Usage?.FiveHour; var w7 = snap?.Usage?.SevenDay;
        var crit = PickWorst(w5, snap?.PaceFive, w7, snap?.PaceSeven, cfg);
        if (crit is not null && cfg.CollapsedQuota)
        {
            bool isFive = ReferenceEquals(crit, w5);
            var critPace = isFive ? snap?.PaceFive : snap?.PaceSeven;
            string glanceLabel = isFive ? $"{s.SessionWord} (5h)" : $"{s.WeekWord} (7d)";
            if (draw)
            {
                // Estado por pace si lo hay, si no por umbral de riesgo (igual criterio que la barra).
                // El glance es TEXTO (glifo + %): usa las variantes AA WarnText/CriticalText (T6b).
                UsageStatus status = WindowStatus(crit, critPace, cfg);
                Color c = critPace is { } ps2
                    ? Theme.PaceTextColor(theme, ps2.Status)
                    : ColorMath.RiskColor(crit.UtilizationPct, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
                double shownPct = motion is not null ? motion.Display("num:crit", crit.UtilizationPct, reduceMotion) : crit.UtilizationPct;
                string glyph = Tray.ShapeGlyph(Tray.ShapeFor(status));
                // % con la cultura del idioma elegido (T2), igual que QuotaBar.
                string right = $"{glyph} {UsageFormat.Percent(shownPct, s.Culture)}";
                // Etiqueta a la izquierda (anti-corte: deja sitio al valor con gutter Spacing.Md).
                // T10 (§3 #16): medida central tipográfica + pintado con el mismo formato → el % del
                // glance cae en la MISMA columna derecha que los % de las barras de la sección Cuota.
                // T14: Typography.Mono escalado por UserScale para acompanhar o resize.
                bool scm = Math.Abs(Dpi.UserScale - 1f) >= 0.001f;
                using var scaledMono = scm ? new Font(Typography.Mono.FontFamily, Typography.Mono.SizeInPoints * Dpi.UserScale, Typography.Mono.Style) : null;
                Font monoScaled = scaledMono ?? Typography.Mono;
                int valW = TextMetrics.MeasureWidth(g, right, monoScaled);
                int valX = rightX - valW;
                using (var fg = new SolidBrush(theme.TextPrimary))
                {
                    string labelShown = FitHeaderLine(glanceLabel, textX, valX, Dpi.Scale(Spacing.Md),
                        t => g.MeasureString(t, smallFont).Width);
                    g.DrawString(labelShown, smallFont, fg, textX, ty);
                }
                using var valBrush = new SolidBrush(c);
                g.DrawString(right, monoScaled, valBrush, valX, ty, TextMetrics.Typographic);
            }
            ty += StatusRowHeight; // glance de UNA línea (token F12: misma fila de estado que la salud)
        }

        // La mascota vive en su banda ARRIBA (ya sumada en `top`), así que el fondo del header es el final
        // del bloque de estado. Divisor de "Cuota" debajo, con la mascota por ENCIMA (P1 #2).
        int bottom = ty;
        // separador (T11: el aire del divisor y el avance final escalan con el DPI)
        if (draw)
        {
            using var p = new Pen(theme.Separator);
            int dy = bottom + DividerAir;
            g.DrawLine(p, x, dy, x + w, dy);
        }
        return bottom + DividerBottomGap;
    }

    /// <summary>
    /// Estado de una ventana para el glance, con el MISMO criterio que <see cref="QuotaBar"/>: el pace
    /// manda si existe (Critical→Critical, Over→Warn, resto Ok) y, sin pace, caen los umbrales de
    /// riesgo de config. PURA y testeable (T5a).
    /// </summary>
    internal static UsageStatus WindowStatus(UsageWindow win, PaceResult? pace, AppConfig cfg) => pace is { } p
        ? (p.Status == PaceStatus.Critical ? UsageStatus.Critical
           : p.Status == PaceStatus.Over ? UsageStatus.Warn : UsageStatus.Ok)
        : win.UtilizationPct >= cfg.CriticalThresholdPct ? UsageStatus.Critical
          : win.UtilizationPct >= cfg.WarnThresholdPct ? UsageStatus.Warn : UsageStatus.Ok;

    /// <summary>
    /// Elige la ventana de PEOR estado para el glance del header (T5a, §3 jerarquía de la auditoría):
    /// Critical > Warn > Ok y, a igualdad de estado, la de mayor %. Sustituye al viejo PickCritical
    /// (solo mayor %), que elegía la semana en verde (84%) con la sesión al 62% y pace crítico.
    /// </summary>
    internal static UsageWindow? PickWorst(UsageWindow? a, PaceResult? paceA, UsageWindow? b, PaceResult? paceB, AppConfig cfg)
    {
        if (a is null) return b;
        if (b is null) return a;
        UsageStatus sa = WindowStatus(a, paceA, cfg);
        UsageStatus sb = WindowStatus(b, paceB, cfg);
        if (sa != sb) return sa > sb ? a : b;
        return a.UtilizationPct >= b.UtilizationPct ? a : b;
    }

    /// <summary>
    /// Recorta una línea de la cabecera para que NO rebase <c>rightEdge - margin</c>, partiendo de
    /// <paramref name="drawX"/>. PURA (delega en <see cref="TextWrap.FitLine"/>, la generalización T8
    /// de este mismo guard) y determinista: la misma entrada da la misma salida, así medir y pintar
    /// reservan lo mismo.
    /// </summary>
    internal static string FitHeaderLine(string text, int drawX, int rightEdge, int margin, Func<string, double> measure)
        => TextWrap.FitLine(text, drawX, rightEdge, margin, measure);

    /// <summary>
    /// Texto + geometría del chip de celebración (T8e): vive entre <paramref name="x"/> y el borde
    /// izquierdo del ⚙ (<paramref name="gearLeft"/>) con gutter <c>Spacing.Sm</c> y padding interior
    /// <c>Spacing.Sm</c> por lado. Si el texto no cabe se ELIDE con elipsis medida en vez de
    /// desaparecer (antes un texto/locale largo dejaba <c>chip.X &lt; x</c> y el destello no se
    /// pintaba). PURA (medición inyectada) y determinista. Devuelve el texto mostrado y el rect del
    /// chip; texto vacío + <see cref="Rectangle.Empty"/> si ni la elipsis cabe.
    /// </summary>
    internal static (string shown, Rectangle chip) CelebrationChip(string flash, int x, int gearLeft,
        int y, int gearSize, int textH, Func<string, double> measure)
    {
        // Techo del texto: chip.X ≥ x ⟺ chipW ≤ gearLeft - Sm - x ⟺ textoW ≤ eso - padding 2·Sm.
        int sm = Dpi.Scale(Spacing.Sm);
        int maxTextW = gearLeft - sm - x - sm * 2;
        string shown = TextWrap.Ellipsize(flash, maxTextW, measure);
        if (shown.Length == 0) return (shown, Rectangle.Empty);
        int chipW = (int)Math.Ceiling(measure(shown)) + sm * 2;
        int chipH = textH + Dpi.Scale(Spacing.Xs);
        var chip = new Rectangle(gearLeft - chipW - sm, y + (gearSize - chipH) / 2, chipW, chipH);
        return (shown, chip);
    }
}
