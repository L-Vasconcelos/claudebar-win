using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Bestiario ASCII propio (clean-room). Cada fase devuelve N frames de texto monoespaciado.
/// El animador cicla los frames; idle es estático. LabelKey devuelve un identificador estable
/// de fase que el DashboardForm mapea a la propiedad concreta de Localization.Strings.
/// </summary>
public static class MascotSprite
{
    // Un bicho propio de ClaudeBar (gato terminal). Frames de 1 línea por simplicidad de layout.
    public static IReadOnlyList<string> Frames(SessionPhase phase) => phase switch
    {
        SessionPhase.Idle => new[] { "( -.- ) zzz" },
        SessionPhase.Processing => new[] { "( o.o )", "( o.- )", "( -.o )" },
        SessionPhase.Compacting => new[] { "( >.< )~", "( >.< )≈" },
        SessionPhase.WaitingForApproval => new[] { "( O.O )!", "( o.o )!" },
        SessionPhase.WaitingForInput => new[] { "( ^.^ )?", "( ^.~ )?" },
        SessionPhase.Ended => new[] { "( x.x )" },
        _ => new[] { "( -.- )" },
    };

    /// <summary>Identificador estable de fase para la etiqueta del estado (el caller lo mapea a Strings).</summary>
    public static string LabelKey(SessionPhase phase) => phase.ToString();
}
