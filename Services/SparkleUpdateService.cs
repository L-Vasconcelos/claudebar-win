using System.Drawing;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace ClaudeBarWin.Services;

/// <summary>
/// Auto-update via NetSparkleUpdater. Appcast firmado (Ed25519) servido desde la
/// última release de GitHub; el binario descargado es el instalador Inno, que se
/// ejecuta en silencio, cierra la app, reemplaza el exe y la relanza.
///
/// El arranque comprueba en silencio (solo marca el menú); la ventana de update
/// de NetSparkle solo aparece cuando el usuario lo pide desde el menú.
/// </summary>
public sealed class SparkleUpdateService : IDisposable
{
    private const string AppCastUrl =
        "https://github.com/Yovancas/claudebar-win/releases/latest/download/appcast.xml";

    // Clave pública Ed25519 (la privada NUNCA va al repo). Generada con netsparkle-generate-appcast.
    private const string PublicKeyBase64 = "UxMaJjAdtg6sQZrydaUvC0N088ZIheKsFw+HjQCsLS8=";

    private readonly SparkleUpdater _sparkle;
    private System.Threading.Timer? _timer;

    /// <summary>Tag de la versión disponible, o null si estamos al día / sin comprobar.</summary>
    public string? AvailableTag { get; private set; }

    /// <summary>Se dispara cuando cambia AvailableTag (para refrescar el menú).</summary>
    public event Action? AvailabilityChanged;

    public SparkleUpdateService(Icon? icon)
    {
        _sparkle = new SparkleUpdater(AppCastUrl, new Ed25519Checker(SecurityMode.Strict, PublicKeyBase64))
        {
            UIFactory = new NetSparkleUpdater.UI.WinForms.UIFactory(icon),
            RelaunchAfterUpdate = false, // lo relanza el instalador Inno, no NetSparkle
            CustomInstallerArguments = "/VERYSILENT /SP- /SUPPRESSMSGBOXES /NORESTART",
        };
    }

    /// <summary>Comprobación silenciosa al arrancar + cada 6 h. Llamar UNA vez.</summary>
    public void StartLoop()
    {
        _ = CheckQuietly();
        _timer = new System.Threading.Timer(
            _ => _ = CheckQuietly(), null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
    }

    private async Task CheckQuietly()
    {
        try
        {
            var info = await _sparkle.CheckForUpdatesQuietly();
            string? tag = info is { Status: UpdateStatus.UpdateAvailable, Updates.Count: > 0 }
                ? info.Updates[0].Version
                : null;
            if (tag != AvailableTag)
            {
                AvailableTag = tag;
                AvailabilityChanged?.Invoke();
            }
        }
        catch
        {
            // Sin red / appcast inaccesible: silencioso, no molesta.
        }
    }

    /// <summary>Comprobación a petición del usuario (muestra la UI de NetSparkle).</summary>
    public Task CheckInteractive() => _sparkle.CheckForUpdatesAtUserRequest();

    public void Dispose()
    {
        _timer?.Dispose();
        _sparkle.Dispose();
    }
}
