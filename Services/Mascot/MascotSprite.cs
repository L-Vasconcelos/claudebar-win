using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public enum MascotSize { Compact, Large }

/// <summary>
/// Bestiario ASCII propio (clean-room). Frames multilínea por (fase, tamaño).
/// Compact = 4 líneas (gatito sentado), Large = 7 líneas (gato con cuerpo).
/// La cara (ojos + nariz) cambia por fase y se comparte entre tamaños; el cuerpo es fijo.
/// El animador cicla frames con (frameIndex % Count): Idle y Ended son estáticos;
/// el resto parpadea/pulsa con 2 frames.
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

    // Cara de 3 chars (ojo · nariz · ojo) por fase. Item1 = pose base, Item2 = pose alterna (parpadeo/guiño/pulso).
    private static (string Base, string Alt) Face(SessionPhase p) => p switch
    {
        SessionPhase.Idle               => ("-.-", "-.-"), // relajado, ojos entornados
        SessionPhase.Processing         => ("o.o", "-.-"), // trabajando: parpadeo
        SessionPhase.WaitingForApproval => ("O.O", "o.o"), // ¡atención! ojos abiertos que pulsan
        SessionPhase.WaitingForInput    => ("^.^", "^.-"), // contento, te espera: guiño
        SessionPhase.Compacting         => ("@.@", "o.o"), // mareado comprimiendo memoria
        SessionPhase.Ended              => ("x.x", "x.x"), // KO
        _                               => ("-.-", "-.-"),
    };

    // Idle y Ended se quedan quietos; los estados con trabajo o que piden atención cobran vida.
    private static bool Animated(SessionPhase p) =>
        p is SessionPhase.Processing or SessionPhase.Compacting
          or SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput;

    // Gato propio, 4 líneas. Centrado en la columna de la nariz; cara variable, cuerpo fijo.
    private static IReadOnlyList<string[]> Compact(SessionPhase p)
    {
        var (baseF, altF) = Face(p);
        static string[] Frame(string f) => new[]
        {
            " /\\_/\\",
            $"( {f} )",
            " > ^ <",
            "(\")_(\")",
        };
        return Animated(p) ? new[] { Frame(baseF), Frame(altF) } : new[] { Frame(baseF) };
    }

    // Large: 7 líneas, gato "loaf" (hogaza) — la cabeza de la compacta (orejas + cara + bigotes)
    // sobre un cuerpo redondeado de interior vacío (el tinte de fase resalta el contorno) con las
    // patitas (")_(") asomando bajo la base. Diseño v0.3.6 (jurado de 3 lentes sobre 12 candidatos;
    // motivos: pose loaf = "cómodo y a salvo", baby-schema cabeza ancha, ADN compartido con la
    // compacta). El frame alterno parpadea Y saca un latigazo de cola (J) sin cambiar el ancho
    // máximo (9 cols) ni el nº de líneas, para que la medida no baile entre frames.
    private static IReadOnlyList<string[]> Large(SessionPhase p)
    {
        var (baseF, altF) = Face(p);
        static string[] Frame(string f, bool tail) => new[]
        {
            "  /\\_/\\",
            $" ( {f} )",
            "  > ^ <",
            " /     \\",
            "(       )",
            " \\_____/",
            tail ? " (\")_(\")J" : " (\")_(\")",
        };
        // Estáticas (Idle/Ended): cola en reposo. Vivas: el frame alterno = guiño + latigazo de cola.
        return Animated(p) ? new[] { Frame(baseF, false), Frame(altF, true) } : new[] { Frame(baseF, false) };
    }
}
