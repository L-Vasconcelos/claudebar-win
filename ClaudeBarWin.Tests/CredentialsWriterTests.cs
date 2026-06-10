using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T1 (auditoría 2026-06-10 §2.2): escritura atómica de ~/.claude/.credentials.json.
/// El archivo pertenece a Claude Code: un write directo a medias = JSON corrupto = logout,
/// y hay carrera real con las sesiones 24/7 de este PC. Goldens del plan:
/// round-trip, destino inexistente, JSON inválido no se escribe, token en disco más nuevo gana.
/// Todos los tests usan paths temporales inyectados — nunca el ~/.claude real.
/// </summary>
public class CredentialsWriterTests : IDisposable
{
    private readonly string _dir;

    public CredentialsWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "claudebar-credwriter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string CredPath => Path.Combine(_dir, ".credentials.json");
    private string TmpPath => CredPath + ".tmp";

    /// <summary>JSON con el shape real de .credentials.json (claudeAiOauth.expiresAt en ms epoch).</summary>
    private static string Creds(long expiresAt, string token = "tok") =>
        "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\",\"refreshToken\":\"r\",\"expiresAt\":" + expiresAt + "}}";

    // ---- Golden 1: round-trip sobre archivo existente ----

    [Fact]
    public void Roundtrip_overwrites_existing_file()
    {
        File.WriteAllText(CredPath, Creds(1000, "viejo"));
        var nuevo = Creds(2000, "nuevo");

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
        Assert.False(File.Exists(TmpPath)); // el .tmp no queda huérfano tras el swap
    }

    // ---- Golden 2: destino inexistente → File.Move fallback ----

    [Fact]
    public void Writes_when_destination_does_not_exist()
    {
        var nuevo = Creds(2000);

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
        Assert.False(File.Exists(TmpPath));
    }

    // ---- Golden 3: JSON inválido NUNCA toca el disco ----

    [Fact]
    public void Invalid_json_never_replaces_existing_file()
    {
        var original = Creds(1000, "intacto");
        File.WriteAllText(CredPath, original);

        var result = CredentialsWriter.WriteAtomic(CredPath, "{esto no es json");

        Assert.Equal(CredentialsWriteResult.InvalidJson, result);
        Assert.Equal(original, File.ReadAllText(CredPath)); // ni un byte cambiado
        Assert.False(File.Exists(TmpPath));                 // ni siquiera se creó el .tmp
    }

    [Fact]
    public void Invalid_json_with_no_destination_creates_nothing()
    {
        var result = CredentialsWriter.WriteAtomic(CredPath, "no-json");

        Assert.Equal(CredentialsWriteResult.InvalidJson, result);
        Assert.False(File.Exists(CredPath));
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public void Non_object_root_is_rejected()
    {
        // "42" parsea como JSON pero un .credentials.json no-objeto rompería Claude Code igual.
        var result = CredentialsWriter.WriteAtomic(CredPath, "42");

        Assert.Equal(CredentialsWriteResult.InvalidJson, result);
        Assert.False(File.Exists(CredPath));
    }

    // ---- Golden 4: el token en disco más nuevo GANA (refresh concurrente) ----

    [Fact]
    public void Newer_token_on_disk_wins()
    {
        var fresco = Creds(5000, "fresco-de-otro-proceso");
        File.WriteAllText(CredPath, fresco);

        var result = CredentialsWriter.WriteAtomic(CredPath, Creds(1000, "nuestro-stale"));

        Assert.Equal(CredentialsWriteResult.SkippedNewerOnDisk, result);
        Assert.Equal(fresco, File.ReadAllText(CredPath)); // no se pisa el refresh ajeno
        Assert.False(File.Exists(TmpPath));
    }

    [Fact]
    public void Older_token_on_disk_is_replaced()
    {
        File.WriteAllText(CredPath, Creds(1000, "viejo"));
        var nuevo = Creds(5000, "nuevo");

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
    }

    [Fact]
    public void Equal_expiresAt_still_writes()
    {
        // Solo un expiresAt ESTRICTAMENTE más nuevo en disco bloquea; a igualdad escribimos
        // (idempotente: reintentar el mismo refresh no debe fallar).
        File.WriteAllText(CredPath, Creds(3000, "a"));
        var nuevo = Creds(3000, "b");

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
    }

    // ---- Bordes del guard de concurrencia ----

    [Fact]
    public void Corrupt_disk_file_is_repaired()
    {
        // Si el archivo en disco está corrupto (el escenario que este writer previene),
        // no hay expiresAt comparable → nuestro JSON válido lo repara.
        File.WriteAllText(CredPath, "garbage{{{");
        var nuevo = Creds(1000);

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
    }

    [Fact]
    public void Disk_without_expiresAt_is_replaced()
    {
        File.WriteAllText(CredPath, """{"claudeAiOauth":{"accessToken":"x"}}""");
        var nuevo = Creds(1000);

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
    }

    [Fact]
    public void Ours_without_expiresAt_still_writes()
    {
        // Sin expiresAt propio no hay comparación posible → el caller manda (documenta la decisión).
        File.WriteAllText(CredPath, Creds(5000));
        var nuevo = """{"claudeAiOauth":{"accessToken":"sin-expiry"}}""";

        var result = CredentialsWriter.WriteAtomic(CredPath, nuevo);

        Assert.Equal(CredentialsWriteResult.Written, result);
        Assert.Equal(nuevo, File.ReadAllText(CredPath));
    }

    // ---- E/S: fallo de escritura no rompe nada ----

    [Fact]
    public void Missing_directory_returns_error()
    {
        var path = Path.Combine(_dir, "no-existe", ".credentials.json");

        var result = CredentialsWriter.WriteAtomic(path, Creds(1000));

        Assert.Equal(CredentialsWriteResult.Error, result);
        Assert.False(File.Exists(path));
    }
}
