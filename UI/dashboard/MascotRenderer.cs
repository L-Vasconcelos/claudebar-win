using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;

namespace ClaudeBarWin.UI;

/// <summary>Dibuja la mascota ASCII (color por fase/humor) + spinner de trabajo. Sin estado.</summary>
public static class MascotRenderer
{
    /// <summary>
    /// Dibuja el frame elegido por el <see cref="MascotAnimator"/> en (x,y) y devuelve el tamaño
    /// ocupado. El <paramref name="state"/> trae el <c>FrameIndex</c> ya resuelto (tempo/blink) y el
    /// glifo del spinner (Processing/Compacting). El color sale de la fase, salvo que el
    /// <paramref name="mood"/> lo tiña (Alert/Happy/Focused). El spinner se pinta a la derecha de la
    /// 1ª línea y NO cambia el tamaño reservado (cabe en el ancho de la mascota o se ignora en la medida).
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

        // Spinner de "trabajo vivo": glifo junto a la mascota (1ª línea). Atenuado y dentro de la
        // celda, así que no ensancha el tamaño reservado (el verbo de la cabecera ya da el ancho).
        if (draw && state.SpinnerGlyph != '\0')
        {
            using var sb = new SolidBrush(theme.TextMuted);
            // Aire entre el borde del ASCII y el spinner (≥ Spacing.Sm): antes quedaba pegado al gato
            // (auditoría visual, T9). Solo pintado y dentro de la celda → no ensancha la medida.
            g.DrawString(state.SpinnerGlyph.ToString(), mono, sb, x + maxW + Spacing.Sm, y);
        }

        return new Size((int)Math.Ceiling(maxW), (int)Math.Ceiling(frame.Length * lineH));
    }

    /// <summary>Color base por fase (sin humor). Compat con consumidores antiguos.</summary>
    public static Color PhaseColor(Theme theme, SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval => theme.Warn,
        SessionPhase.WaitingForInput => theme.Warn,
        SessionPhase.Processing => theme.Ok,
        SessionPhase.Compacting => theme.Ok,
        SessionPhase.Ended => theme.Critical,
        _ => theme.Neutral,
    };

    /// <summary>Color con tinte de humor: el humor manda cuando es expresivo; si no, cae a la fase.</summary>
    public static Color MoodColor(Theme theme, SessionPhase phase, Mood mood) => mood switch
    {
        Mood.Alert => theme.Warn,
        Mood.Happy => theme.Ok,
        _ => PhaseColor(theme, phase),  // Focused/Neutral siguen el color de la fase
    };
}
