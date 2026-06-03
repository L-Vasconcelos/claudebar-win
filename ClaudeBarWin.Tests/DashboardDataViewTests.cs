using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T9 (auditoría visual): la primera etiqueta del eje X de la gráfica se cortaba por la izquierda
/// ("Jun 00h" → "un 00h") porque se centraba bajo su tick y el tick más a la izquierda coincide con
/// el borde del plot. <see cref="DashboardDataView.AxisLabelX"/> es un helper PURO que recoloca cada
/// etiqueta dentro de [plotLeft, plotRight] sin solapar ni salirse, así que se testea sin GDI+.
/// </summary>
public class DashboardDataViewTests
{
    [Fact]
    public void First_label_is_not_clipped_on_the_left()
    {
        // Etiqueta de 40 px centrada en plotLeft (x=100) querría empezar en 80 (< plotLeft) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 100, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx >= 100, $"la primera etiqueta empezó en {lx} (< plotLeft 100): se cortaría por la izquierda");
        Assert.Equal(100, lx);
    }

    [Fact]
    public void Last_label_is_not_clipped_on_the_right()
    {
        // Etiqueta de 40 px centrada en plotRight (x=400) querría terminar en 420 (> plotRight) → se ancla.
        float lx = DashboardDataView.AxisLabelX(centerX: 400, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.True(lx + 40 <= 400, $"la última etiqueta terminó en {lx + 40} (> plotRight 400): se cortaría por la derecha");
        Assert.Equal(360, lx);
    }

    [Fact]
    public void Middle_label_stays_centered()
    {
        // Bien dentro del plot: se centra bajo su tick (250 - 20 = 230).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 40, plotLeft: 100, plotRight: 400);
        Assert.Equal(230, lx);
    }

    [Fact]
    public void Label_never_starts_before_plot_left_for_any_center()
    {
        for (float cx = 100; cx <= 400; cx += 13)
        {
            float lx = DashboardDataView.AxisLabelX(cx, labelW: 50, plotLeft: 100, plotRight: 400);
            Assert.True(lx >= 100, $"centerX {cx}: arranca en {lx} (< 100)");
        }
    }

    [Fact]
    public void Wide_label_is_clamped_to_plot_left_when_it_cannot_fit()
    {
        // Etiqueta más ancha que el plot: prioriza no cortar por la izquierda (Near en plotLeft).
        float lx = DashboardDataView.AxisLabelX(centerX: 250, labelW: 500, plotLeft: 100, plotRight: 400);
        Assert.Equal(100, lx);
    }
}
