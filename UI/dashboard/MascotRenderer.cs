using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>Dibuja la mascota ASCII (tamaño + color por fase). Sin estado.</summary>
public static class MascotRenderer
{
    /// <summary>Dibuja el frame indicado en (x,y) y devuelve el tamaño ocupado.</summary>
    public static Size Draw(Graphics g, bool draw, int x, int y, SessionPhase phase, MascotSize size,
                            int frameIndex, Theme theme, Font mono)
    {
        var frames = MascotSprite.Frames(phase, size);
        var frame = frames[frameIndex % frames.Count];
        float lineH = mono.GetHeight(g);
        float maxW = 0;
        var color = PhaseColor(theme, phase);
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
        return new Size((int)Math.Ceiling(maxW), (int)Math.Ceiling(frame.Length * lineH));
    }

    public static Color PhaseColor(Theme theme, SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval => theme.Warn,
        SessionPhase.WaitingForInput => theme.Warn,
        SessionPhase.Processing => theme.Ok,
        SessionPhase.Compacting => theme.Ok,
        SessionPhase.Ended => theme.Critical,
        _ => theme.Neutral,
    };
}
