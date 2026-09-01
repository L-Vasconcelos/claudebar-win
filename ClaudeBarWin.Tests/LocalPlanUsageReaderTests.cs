using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Respaldo local de cuota (<c>%APPDATA%\Claude\plan-usage-history.json</c>) que sustituye a la API
/// OAuth cuando esta devuelve 429 (diagnóstico 2026-08-10: 429 permanente durante 16 días).
/// </summary>
public class LocalPlanUsageReaderTests : IDisposable
{
    private readonly string _dir;

    public LocalPlanUsageReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cbw-localusage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFile(string json)
    {
        var p = Path.Combine(_dir, "plan-usage-history.json");
        File.WriteAllText(p, json);
        return p;
    }

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    [Fact]
    public void Read_returns_null_when_file_missing()
    {
        var r = new LocalPlanUsageReader(Path.Combine(_dir, "no-existe.json"));
        Assert.Null(r.Read(DateTimeOffset.UtcNow));
        Assert.Null(r.LastSampleAt());
    }

    [Fact]
    public void Read_returns_null_on_malformed_json()
    {
        // Archivo a medio escribir por la app de escritorio: best-effort, nunca lanza.
        var r = new LocalPlanUsageReader(WriteFile("{ esto no es json"));
        Assert.Null(r.Read(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Read_takes_the_latest_sample_percentages()
    {
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
        { "version": 2, "samples": [
          { "t": {{Ms(now.AddHours(-2))}}, "org": "o", "u": { "fh": 3, "sd": 1 } },
          { "t": {{Ms(now.AddMinutes(-5))}}, "org": "o", "u": { "fh": 16, "sd": 5 } }
        ] }
        """;
        var usage = new LocalPlanUsageReader(WriteFile(json)).Read(now);

        Assert.NotNull(usage);
        Assert.Equal(16, usage!.FiveHour!.UtilizationPct);
        Assert.Equal(5, usage.SevenDay!.UtilizationPct);
    }

    [Fact]
    public void LastSampleAt_reports_the_newest_timestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
        { "version": 2, "samples": [
          { "t": {{Ms(now.AddHours(-3))}}, "u": { "fh": 1, "sd": 1 } },
          { "t": {{Ms(now.AddMinutes(-7))}}, "u": { "fh": 2, "sd": 1 } }
        ] }
        """;
        var at = new LocalPlanUsageReader(WriteFile(json)).LastSampleAt();

        Assert.NotNull(at);
        Assert.True((now - at!.Value).TotalMinutes < 8);
    }

    [Fact]
    public void DeriveReset_adds_the_window_to_the_zero_to_positive_transition()
    {
        // La ventana arrancó hace 1h (0 → 4%): un reset de 5h cae dentro de 4h.
        var now = DateTimeOffset.UtcNow;
        var samples = new List<LocalPlanUsageReader.Sample>
        {
            new(now.AddHours(-2), 0, 0),
            new(now.AddHours(-1), 4, 1),
            new(now.AddMinutes(-5), 16, 5),
        };

        var reset = LocalPlanUsageReader.DeriveReset(samples, TimeSpan.FromHours(5), s => s.FiveHourPct, now);

        Assert.NotNull(reset);
        Assert.Equal(now.AddHours(4), reset!.Value, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DeriveReset_returns_null_when_the_estimate_is_already_in_the_past()
    {
        // Arranque hace 9h con ventana de 5h ⇒ el reset ya pasó: mejor SIN countdown que con uno falso.
        var now = DateTimeOffset.UtcNow;
        var samples = new List<LocalPlanUsageReader.Sample>
        {
            new(now.AddHours(-10), 0, 0),
            new(now.AddHours(-9), 4, 1),
            new(now.AddMinutes(-5), 16, 5),
        };

        Assert.Null(LocalPlanUsageReader.DeriveReset(samples, TimeSpan.FromHours(5), s => s.FiveHourPct, now));
    }

    [Fact]
    public void DeriveReset_returns_null_without_a_detectable_window_start()
    {
        // Histórico plano y creciente: no hay transición ni caída ⇒ no se inventa una hora.
        var now = DateTimeOffset.UtcNow;
        var samples = new List<LocalPlanUsageReader.Sample>
        {
            new(now.AddHours(-3), 20, 5),
            new(now.AddHours(-2), 22, 5),
            new(now.AddMinutes(-5), 25, 6),
        };

        Assert.Null(LocalPlanUsageReader.DeriveReset(samples, TimeSpan.FromHours(5), s => s.FiveHourPct, now));
    }

    [Fact]
    public void DeriveReset_treats_a_steep_drop_as_a_new_window()
    {
        // 80% → 3% = la ventana se reseteó y ya empezó otra en esa misma muestra (hace 30 min).
        var now = DateTimeOffset.UtcNow;
        var samples = new List<LocalPlanUsageReader.Sample>
        {
            new(now.AddHours(-2), 80, 40),
            new(now.AddMinutes(-30), 3, 40),
            new(now.AddMinutes(-5), 7, 41),
        };

        var reset = LocalPlanUsageReader.DeriveReset(samples, TimeSpan.FromHours(5), s => s.FiveHourPct, now);

        Assert.NotNull(reset);
        Assert.Equal(now.AddHours(5).AddMinutes(-30), reset!.Value, TimeSpan.FromMinutes(1));
    }
}
