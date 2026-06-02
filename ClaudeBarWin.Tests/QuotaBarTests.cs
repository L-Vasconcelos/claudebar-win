using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

public class QuotaBarTests
{
    [Fact]
    public void MarkerX_returns_midpoint_at_fifty_pct()
    {
        Assert.Equal(50, QuotaBarGeometry.MarkerX(0, 100, 50));
    }

    [Fact]
    public void MarkerX_returns_start_at_zero_and_end_at_hundred()
    {
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, 0));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 100));
    }

    [Fact]
    public void MarkerX_respects_x_offset()
    {
        Assert.Equal(20, QuotaBarGeometry.MarkerX(20, 100, 0));
        Assert.Equal(70, QuotaBarGeometry.MarkerX(20, 100, 50));
        Assert.Equal(120, QuotaBarGeometry.MarkerX(20, 100, 100));
    }

    [Fact]
    public void MarkerX_clamps_to_bar_bounds()
    {
        // pct fuera de [0,100] se recorta a los extremos de la barra [x, x+w].
        Assert.Equal(0, QuotaBarGeometry.MarkerX(0, 100, -30));
        Assert.Equal(100, QuotaBarGeometry.MarkerX(0, 100, 150));
    }

    [Fact]
    public void TickX_matches_markerX_for_same_pct()
    {
        // El tick de umbral comparte la misma proyección que el marker.
        Assert.Equal(QuotaBarGeometry.MarkerX(0, 100, 70), QuotaBarGeometry.TickX(0, 100, 70));
        Assert.Equal(QuotaBarGeometry.MarkerX(10, 200, 90), QuotaBarGeometry.TickX(10, 200, 90));
    }

    [Fact]
    public void Threshold_ticks_fall_within_bar_and_are_ordered()
    {
        // Warn (70%) y Critical (90%) por defecto: ambos ticks dentro del ancho y ordenados warn < crit.
        const int x = 20, w = 200;
        int warn = QuotaBarGeometry.TickX(x, w, 70);
        int crit = QuotaBarGeometry.TickX(x, w, 90);

        Assert.InRange(warn, x, x + w);
        Assert.InRange(crit, x, x + w);
        Assert.True(warn < crit, "el tick de warn debe quedar a la izquierda del de critical");
    }
}
