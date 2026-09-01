using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.UI;

/// <summary>Dibuja la mascota ASCII (color por fase/humor) + spinner de trabajo. Sin estado.</summary>
public static class MascotRenderer
{
    /// <summary>Barrido (grados) del arco del spinner: deja un hueco para que se lea como anillo girando.</summary>
    internal const float SpinnerSweepDeg = 280f;

    /// <summary>
    /// Dibuja el frame elegido por el <see cref="MascotAnimator"/> en (x,y) y devuelve el tamaño
    /// ocupado. El <paramref name="state"/> trae el <c>FrameIndex</c> ya resuelto (tempo/blink) y la
    /// señal/ángulo del spinner (Processing/Compacting). El color sale de la fase, salvo que el
    /// <paramref name="mood"/> lo tiña (Alert/Happy/Focused). El spinner se pinta a la derecha de la
    /// fila de la CARA y NO cambia el tamaño reservado (cabe en el ancho de la mascota o se ignora en la medida).
    /// </summary>
    public static Size Draw(Graphics g, bool draw, int x, int y, SessionPhase phase,
                            MascotState state, Theme theme, Font mono, Mood mood = Mood.Neutral)
    {
        var frames = MascotSprite.Frames(phase);
        var frame = frames[state.FrameIndex % frames.Count];
        float lineH = mono.GetHeight(g);
        float maxW = 0;
        var color = MoodColor(theme, phase, mood);
        for (int i = 0; i < frame.Length; i++)
        {
            if (draw)
            {
                using var b = new SolidBrush(color);
                g.DrawString(frame[i], mono, b, x, y + i * lineH);
            }
            var w = g.MeasureString(frame[i], mono).Width;
            if (w > maxW) maxW = w;
        }

        // Spinner de "trabajo vivo": ARCO GDI+ que gira (T-v039 F2), no el viejo glifo braille a ~7px
        // (se veía como 1-3 píxeles muertos colgando de la oreja). Color = fase, grosor ~2px, ángulo
        // de arranque elapsed-driven (state.SpinnerAngleDeg). Anclado a la fila de la CARA (y+lineH),
        // no a la oreja. Solo pintado y dentro de la celda → no ensancha el tamaño reservado.
        if (draw && state.SpinnerGlyph != '\0')
            DrawSpinnerArc(g, x + maxW + Dpi.Scale(Spacing.Sm), y + lineH, lineH, state.SpinnerAngleDeg, PhaseColor(theme, phase));

        return new Size((int)Math.Ceiling(maxW), (int)Math.Ceiling(frame.Length * lineH));
    }

    /// <summary>
    /// Arco del spinner: anillo de <paramref name="rowH"/> de alto (el de una línea de la cara),
    /// centrado en la fila, con grosor ~2px (escala con DPI) y un hueco para que se lea girando. El
    /// arranque <paramref name="startDeg"/> viene del animador (elapsed-driven). AA on para que el
    /// arco no se vea dentado.
    /// </summary>
    private static void DrawSpinnerArc(Graphics g, float x, float rowTop, float rowH, float startDeg, Color color)
    {
        // Diámetro = ~70% de la altura de fila, centrado verticalmente en la fila de la cara.
        float d = Math.Max(6f, rowH * 0.70f);
        float thickness = Math.Max(1.5f, Dpi.Scale(2));
        float pad = thickness / 2f + 0.5f;            // margen para que el trazo no se recorte
        var rect = new RectangleF(x + pad, rowTop + (rowH - d) / 2f + pad, d - 2 * pad, d - 2 * pad);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var sm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, rect, startDeg, SpinnerSweepDeg);
        }
        finally { g.SmoothingMode = sm; }
    }

    /// <summary>Color base por fase (sin humor). Compat con consumidores antiguos.</summary>
    public static Color PhaseColor(Theme theme, SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval => theme.Warn,
        SessionPhase.WaitingForInput => theme.Warn,
        SessionPhase.Processing => theme.Ok,
        SessionPhase.Compacting => theme.Ok,
        SessionPhase.Ended => theme.Critical,
        // Idle/default: el gris Neutral caía a ~2.3:1 sobre el fondo oscuro (glifo no textual < 3:1,
        // T-v039 F1). MascotIdle → TextMuted (≥3:1, ~5:1 como texto) en los 3 temas. NO toca las fases
        // activas (Warn/Ok/Critical), que tienen su propio color semántico.
        _ => theme.MascotIdle,
    };

    /// <summary>Color con tinte de humor: el humor manda cuando es expresivo; si no, cae a la fase.</summary>
    public static Color MoodColor(Theme theme, SessionPhase phase, Mood mood) => mood switch
    {
        Mood.Alert => theme.Warn,
        // Celebración de reset (T-v039 F3): el gato contento se tiñe del ACENTO de marca (#CC785C),
        // no del verde Ok. Antes Happy heredaba Ok (verde) y la cara de Processing → "contento" se
        // confundía con "trabajando". El acento lo distingue de cualquier estado de cuota/fase.
        Mood.Happy => theme.Accent,
        _ => PhaseColor(theme, phase),  // Focused/Neutral siguen el color de la fase
    };
}
