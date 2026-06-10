using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class UsageFormatTests
{
    [Fact]
    public void ResetAbsolute_formats_local_time_as_ddd_HHmm()
    {
        // Un instante UTC concreto; ResetAbsolute lo pasa a hora local en formato "ddd HH:mm"
        // con la cultura del idioma elegido (T2), no la CurrentCulture del SO.
        var ci = Localization.Get("en").Culture;
        var when = new DateTimeOffset(2026, 6, 2, 16, 42, 0, TimeSpan.Zero);
        var expected = when.ToLocalTime().ToString("ddd HH:mm", ci);

        Assert.Equal(expected, UsageFormat.ResetAbsolute(when, ci));
    }

    [Fact]
    public void ResetAbsolute_returns_empty_for_null()
    {
        Assert.Equal("", UsageFormat.ResetAbsolute(null, Localization.Get("en").Culture));
    }

    [Fact]
    public void Relative_formats_minutes_localized()
    {
        var s = Localization.Get("es");
        var fiveMinAgo = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        Assert.Equal("hace 5 min", UsageFormat.Relative(fiveMinAgo, s));
    }

    [Fact]
    public void Relative_formats_seconds_localized()
    {
        var s = Localization.Get("es");
        var thirtySecAgo = DateTime.UtcNow - TimeSpan.FromSeconds(30);

        Assert.Equal("hace 30 s", UsageFormat.Relative(thirtySecAgo, s));
    }

    [Fact]
    public void IsStale_is_true_past_three_times_refresh()
    {
        // 10 min de antigüedad con refresh de 60s → umbral 3×60=180s → stale.
        Assert.True(UsageFormat.IsStale(DateTime.UtcNow - TimeSpan.FromMinutes(10), 60));
    }

    [Fact]
    public void IsStale_is_false_within_three_times_refresh()
    {
        // 30s de antigüedad con refresh de 60s → umbral 180s → fresco.
        Assert.False(UsageFormat.IsStale(DateTime.UtcNow - TimeSpan.FromSeconds(30), 60));
    }

    [Fact]
    public void Relative_normalizes_unspecified_kind_as_utc()
    {
        // Un DateTime con Kind=Unspecified debe tratarse como UTC (no como local),
        // si no el cálculo del relativo se desvía por el offset de zona.
        var unspecifiedFiveMinAgo = DateTime.SpecifyKind(
            DateTime.UtcNow - TimeSpan.FromMinutes(5), DateTimeKind.Unspecified);
        var s = Localization.Get("es");

        Assert.Equal("hace 5 min", UsageFormat.Relative(unspecifiedFiveMinAgo, s));
    }

    [Fact]
    public void IsStale_normalizes_unspecified_kind_as_utc()
    {
        var unspecifiedTenMinAgo = DateTime.SpecifyKind(
            DateTime.UtcNow - TimeSpan.FromMinutes(10), DateTimeKind.Unspecified);

        Assert.True(UsageFormat.IsStale(unspecifiedTenMinAgo, 60));
    }
}
