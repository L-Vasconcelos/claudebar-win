using System.Text.Json;

namespace ClaudeBarWin.Services;

/// <summary>
/// Fuente LOCAL de cuota: <c>%APPDATA%\Claude\plan-usage-history.json</c>, el histórico que la propia
/// app de escritorio de Claude va escribiendo (~cada 15 min). No requiere token, no toca la red y por
/// tanto NO puede caer por rate-limit.
///
/// <para>Por qué existe (diagnóstico 2026-08-10): el endpoint OAuth <c>/api/oauth/usage</c> llevaba 16
/// días devolviendo 429 de forma permanente — también el de refresh, y también sin tráfico del widget
/// durante horas, así que no era un backoff que se pudiera esperar. Mientras tanto la app de escritorio
/// seguía registrando la cota real en disco. Leer ese archivo devuelve el panel a la vida sin depender
/// de la API bloqueada.</para>
///
/// <para>Formato: <c>{ "version": 2, "samples": [ { "t": &lt;epoch ms&gt;, "org": "...",
/// "u": { "fh": &lt;% 5h&gt;, "sd": &lt;% 7d&gt;, "xu": &lt;extra usage&gt; } } ] }</c>. Las muestras NO
/// traen hora de reset: <see cref="DeriveReset"/> la estima a partir del propio histórico.</para>
/// </summary>
public sealed class LocalPlanUsageReader
{
    private readonly string _path;

    public LocalPlanUsageReader(string? path = null)
        => _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "plan-usage-history.json");

    /// <summary>Una muestra del histórico ya normalizada.</summary>
    public readonly record struct Sample(DateTimeOffset At, double? FiveHourPct, double? SevenDayPct);

    /// <summary>
    /// Última lectura disponible, o <c>null</c> si el archivo no existe, no se puede leer o no tiene
    /// muestras utilizables. Nunca lanza: es una fuente best-effort.
    /// </summary>
    public RealUsage? Read(DateTimeOffset now)
    {
        var samples = ReadSamples();
        if (samples.Count == 0) return null;

        var last = samples[^1];
        if (last.FiveHourPct is null && last.SevenDayPct is null) return null;

        return new RealUsage
        {
            FiveHour = last.FiveHourPct is { } fh
                ? new UsageWindow(fh, DeriveReset(samples, TimeSpan.FromHours(5), s => s.FiveHourPct, now))
                : null,
            SevenDay = last.SevenDayPct is { } sd
                ? new UsageWindow(sd, DeriveReset(samples, TimeSpan.FromDays(7), s => s.SevenDayPct, now))
                : null,
        };
    }

    /// <summary>Instante de la última muestra (para decidir si el dato local está fresco).</summary>
    public DateTimeOffset? LastSampleAt()
    {
        var samples = ReadSamples();
        return samples.Count == 0 ? null : samples[^1].At;
    }

    /// <summary>
    /// Estima la hora de reset de una ventana: busca hacia atrás el último punto en el que la ventana
    /// ARRANCÓ (la muestra en que el % pasa de 0 —o de un valle— a positivo) y le suma la duración de la
    /// ventana. Si no encuentra un arranque dentro de la propia ventana (histórico corto, o la ventana
    /// lleva activa desde antes del primer dato), devuelve <c>null</c> en vez de inventar una hora: el
    /// panel prefiere no mostrar countdown a mostrar uno falso.
    /// </summary>
    internal static DateTimeOffset? DeriveReset(
        IReadOnlyList<Sample> samples, TimeSpan window, Func<Sample, double?> pick, DateTimeOffset now)
    {
        DateTimeOffset? start = null;
        // Recorre de la más reciente a la más antigua buscando la transición "0 → positivo".
        for (int i = samples.Count - 1; i > 0; i--)
        {
            double? cur = pick(samples[i]);
            double? prev = pick(samples[i - 1]);
            if (cur is null || prev is null) continue;
            if (cur > 0 && prev <= 0) { start = samples[i].At; break; }
            // Caída brusca = la ventana anterior se reseteó y ya empezó otra en esta misma muestra.
            if (prev - cur >= 10) { start = samples[i].At; break; }
        }
        if (start is null) return null;

        var reset = start.Value + window;
        // Un reset ya pasado significa que la estimación quedó obsoleta (p.ej. el histórico tiene un
        // hueco): no sirve para un countdown, así que se descarta.
        return reset > now ? reset : null;
    }

    private List<Sample> ReadSamples()
    {
        var result = new List<Sample>();
        try
        {
            if (!File.Exists(_path)) return result;

            // FileShare.ReadWrite: la app de escritorio puede estar escribiendo el archivo ahora mismo.
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("samples", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in arr.EnumerateArray())
            {
                if (!el.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) continue;
                double? fh = null, sd = null;
                if (el.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    if (u.TryGetProperty("fh", out var f) && f.ValueKind == JsonValueKind.Number) fh = f.GetDouble();
                    if (u.TryGetProperty("sd", out var s) && s.ValueKind == JsonValueKind.Number) sd = s.GetDouble();
                }
                if (fh is null && sd is null) continue;
                result.Add(new Sample(DateTimeOffset.FromUnixTimeMilliseconds(t.GetInt64()), fh, sd));
            }
        }
        catch
        {
            // best-effort: un archivo a medio escribir o con otro formato no puede tumbar el refresco.
            return result;
        }
        return result;
    }
}
