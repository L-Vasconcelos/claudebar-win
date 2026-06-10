using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>Resultado de <see cref="CredentialsWriter.WriteAtomic"/>.</summary>
public enum CredentialsWriteResult
{
    /// <summary>El JSON nuevo quedó escrito en el destino (swap atómico completado).</summary>
    Written,

    /// <summary>
    /// El archivo en disco tiene un claudeAiOauth.expiresAt MÁS NUEVO que el nuestro:
    /// otro proceso (Claude Code, sesiones 24/7) ya refrescó → no se pisa. Para el caller
    /// es éxito: el disco contiene un token más fresco que el que iba a escribir.
    /// </summary>
    SkippedNewerOnDisk,

    /// <summary>El JSON propuesto no parsea o su raíz no es un objeto → el disco no se toca.</summary>
    InvalidJson,

    /// <summary>Fallo de E/S al escribir o intercambiar; el destino queda como estaba.</summary>
    Error
}

/// <summary>
/// Escritura atómica de ~/.claude/.credentials.json — archivo que pertenece a Claude Code,
/// no a esta app (P0 de la auditoría 2026-06-10 §2.2). Garantías:
///  1. Valida que el JSON nuevo parsea ANTES de tocar el disco (antes, un crash a mitad de
///     File.WriteAllTextAsync dejaba el archivo corrupto = logout de Claude Code).
///  2. Escribe a &lt;path&gt;.tmp y hace File.Replace (swap atómico en NTFS); si el destino
///     no existe, File.Move. El destino nunca queda escrito a medias.
///  3. Re-lee el destino justo antes del swap: si su expiresAt es estrictamente más nuevo
///     que el nuestro, un refresh concurrente ya ganó → SkippedNewerOnDisk (no escribir
///     pisaría también su refreshToken rotado, que invalidaría la sesión de Claude Code).
/// </summary>
public static class CredentialsWriter
{
    public static CredentialsWriteResult WriteAtomic(string path, string json)
    {
        long? ourExpiresAt;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return CredentialsWriteResult.InvalidJson;
            ourExpiresAt = GetExpiresAt(doc.RootElement);
        }
        catch
        {
            return CredentialsWriteResult.InvalidJson;
        }

        try
        {
            // Guard anti-carrera: re-lee el disco justo antes de escribir. Solo bloquea un
            // expiresAt ESTRICTAMENTE más nuevo (a igualdad, reintentar el mismo refresh es válido).
            // Disco corrupto o sin expiresAt → no hay nada más fresco que proteger → escribimos.
            if (ourExpiresAt is not null
                && File.Exists(path)
                && TryGetExpiresAtFromFile(path) is long onDisk
                && onDisk > ourExpiresAt.Value)
            {
                return CredentialsWriteResult.SkippedNewerOnDisk;
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            try
            {
                if (File.Exists(path))
                    File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* best-effort: no dejar .tmp huérfano */ }
                throw;
            }
            return CredentialsWriteResult.Written;
        }
        catch
        {
            return CredentialsWriteResult.Error;
        }
    }

    private static long? TryGetExpiresAtFromFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? GetExpiresAt(doc.RootElement) : null;
        }
        catch
        {
            return null;
        }
    }

    private static long? GetExpiresAt(JsonElement root) =>
        root.TryGetProperty("claudeAiOauth", out var o)
        && o.ValueKind == JsonValueKind.Object
        && o.TryGetProperty("expiresAt", out var e)
        && e.ValueKind == JsonValueKind.Number
            ? e.GetInt64()
            : null;
}
