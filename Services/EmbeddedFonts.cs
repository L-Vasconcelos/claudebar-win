using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services;

/// <summary>
/// Carrega fontes embutidas (Inter + JetBrains Mono) via PrivateFontCollection para que funcionem
/// sem instalação no sistema. A coleção vive toda a app (dispose no exit mataria as fontes em uso).
/// </summary>
public static class EmbeddedFonts
{
    private static readonly PrivateFontCollection _pfc = new();
    private static bool _loaded;

    /// <summary>Família "Inter" carregada, ou null se não disponível.</summary>
    public static FontFamily? Inter { get; private set; }

    /// <summary>Família "JetBrains Mono" carregada, ou null se não disponível.</summary>
    public static FontFamily? JetBrainsMono { get; private set; }

    /// <summary>Carrega as fontes dos recursos embutidos. Idempotente e seguro (falha silenciosa).</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            LoadFont("ClaudeBarWin.Resources.Fonts.Inter-Regular.ttf");
            LoadFont("ClaudeBarWin.Resources.Fonts.Inter-Bold.ttf");
            LoadFont("ClaudeBarWin.Resources.Fonts.Inter-SemiBold.ttf");
            LoadFont("ClaudeBarWin.Resources.Fonts.JetBrainsMono-Regular.ttf");

            foreach (var fam in _pfc.Families)
            {
                if (fam.Name.Equals("Inter", StringComparison.OrdinalIgnoreCase))
                    Inter = fam;
                else if (fam.Name.StartsWith("JetBrains Mono", StringComparison.OrdinalIgnoreCase))
                    JetBrainsMono = fam;
            }
        }
        catch
        {
            // Falha silenciosa: Typography cai no fallback (Segoe UI / Consolas).
        }
    }

    private static void LoadFont(string resourceName)
    {
        var asm = typeof(EmbeddedFonts).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return;

        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            _pfc.AddMemoryFont(handle.AddrOfPinnedObject(), data.Length);
        }
        finally
        {
            handle.Free();
        }
    }
}
