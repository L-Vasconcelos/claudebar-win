using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public enum MascotSize { Compact, Large }

/// <summary>
/// Bestiario ASCII propio (clean-room). Frames multilínea por (fase, tamaño).
/// Compact ≈ 4 líneas (6×6), Large ≈ 7 líneas (8×8). El animador cicla frames; idle es estático.
/// </summary>
public static class MascotSprite
{
    public static MascotSize ParseSize(string? s) =>
        string.Equals(s, "large", StringComparison.OrdinalIgnoreCase) ? MascotSize.Large : MascotSize.Compact;

    /// <summary>Frames multilínea (cada frame = varias líneas) por fase y tamaño.</summary>
    public static IReadOnlyList<string[]> Frames(SessionPhase phase, MascotSize size) =>
        size == MascotSize.Large ? Large(phase) : Compact(phase);

    /// <summary>Shim 1-línea para consumidores antiguos (se retirará al migrar el dashboard).</summary>
    public static IReadOnlyList<string> Frames(SessionPhase phase) =>
        Compact(phase).Select(f => f[1].Trim()).ToList();

    public static string LabelKey(SessionPhase phase) => phase.ToString();

    // Gato propio. Compact: 4 líneas. Cara cambia por estado; el cuerpo se mantiene.
    private static IReadOnlyList<string[]> Compact(SessionPhase p)
    {
        string face = p switch
        {
            SessionPhase.Idle => "-.-",
            SessionPhase.Processing => "o.o",
            SessionPhase.WaitingForApproval => "O.O",
            SessionPhase.WaitingForInput => "^.^",
            SessionPhase.Compacting => ">.<",
            SessionPhase.Ended => "x.x",
            _ => "-.-",
        };
        string[] f1 = { " /\\_/\\", $"( {face} )", " > ^ <", " (\")(\")" };
        if (p == SessionPhase.Processing) // parpadeo simple
        {
            string[] f2 = { " /\\_/\\", "( -.o )", " > ^ <", " (\")(\")" };
            return new[] { f1, f2 };
        }
        return new[] { f1 };
    }

    // Large: 7 líneas, gato sentado con cuerpo.
    private static IReadOnlyList<string[]> Large(SessionPhase p)
    {
        string eyes = p switch
        {
            SessionPhase.Idle => "-   -",
            SessionPhase.Processing => "o   o",
            SessionPhase.WaitingForApproval => "O   O",
            SessionPhase.WaitingForInput => "^   ^",
            SessionPhase.Compacting => ">   <",
            SessionPhase.Ended => "x   x",
            _ => "-   -",
        };
        string[] f1 =
        {
            "   /\\_/\\",
            $"  ( {eyes} )",
            "  (  =^=  )",
            "  /|     |\\",
            " ( |     | )",
            "   |     |",
            "   (__|__)",
        };
        if (p == SessionPhase.Processing)
        {
            string[] f2 =
            {
                "   /\\_/\\",
                "  ( o   - )",
                "  (  =^=  )",
                "  /|     |\\",
                " ( |     | )",
                "   |     |",
                "   (__|__)",
            };
            return new[] { f1, f2 };
        }
        return new[] { f1 };
    }
}
