using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="Stagger"/> es PURO y elapsed-driven: dado <c>tSinceOpenMs</c> y el índice de la
/// sección devuelve un alfa∈[0,1] (cuánto ha "entrado" esa sección) y una traslación vertical
/// <c>OffsetY</c> que parte de <c>max</c> px y llega a 0. La cabecera entra primero (index 0), las
/// secciones de datos detrás (1..n) y el footer al final (n+1). Sin reloj/GDI+ por dentro →
/// determinista. El <c>y</c> de layout NO depende de esto: solo desplaza el dibujo dentro de su celda.
/// </summary>
public class StaggerTests
{
    private const double Stag = 40.0;   // Motion.StaggerMs
    private const double Dur = 180.0;   // Motion.StaggerDurMs

    // --- Alpha ------------------------------------------------------------

    [Fact]
    public void Alpha_is_zero_before_the_section_starts()
    {
        // index 2 arranca en 2*40 = 80 ms. A 79 ms aún no ha empezado.
        Assert.Equal(0.0, Stagger.Alpha(79.0, 2, Stag, Dur), 9);
        Assert.Equal(0.0, Stagger.Alpha(0.0, 2, Stag, Dur), 9);
    }

    [Fact]
    public void Alpha_at_section_start_is_zero()
    {
        // justo en su inicio (t == index*stagger) el tramo todavía no ha avanzado.
        Assert.Equal(0.0, Stagger.Alpha(80.0, 2, Stag, Dur), 9);
    }

    [Fact]
    public void Alpha_rises_within_the_segment()
    {
        // index 0 entra en [0, dur]; a media duración debe estar en (0,1).
        double mid = Stagger.Alpha(Dur / 2, 0, Stag, Dur);
        Assert.True(mid > 0.0 && mid < 1.0, $"esperado en (0,1), got {mid}");
    }

    [Fact]
    public void Alpha_is_one_past_start_plus_duration()
    {
        // index 1 entra en [40, 40+180=220]; pasado 220 está asentada.
        Assert.Equal(1.0, Stagger.Alpha(220.0, 1, Stag, Dur), 9);
        Assert.Equal(1.0, Stagger.Alpha(999.0, 1, Stag, Dur), 9);
    }

    [Fact]
    public void Alpha_is_monotonically_non_decreasing_in_time()
    {
        double prev = double.NegativeInfinity;
        for (int ms = 0; ms <= 500; ms += 5)
        {
            double a = Stagger.Alpha(ms, 3, Stag, Dur);
            Assert.True(a >= prev - 1e-9, $"alpha bajó en t={ms}: {a} < {prev}");
            Assert.InRange(a, 0.0, 1.0);
            prev = a;
        }
    }

    [Fact]
    public void Later_sections_lag_earlier_ones()
    {
        // a un t intermedio, una sección con índice mayor ha entrado MENOS (o igual).
        const double t = 120.0;
        double a0 = Stagger.Alpha(t, 0, Stag, Dur);
        double a1 = Stagger.Alpha(t, 1, Stag, Dur);
        double a2 = Stagger.Alpha(t, 2, Stag, Dur);
        Assert.True(a0 >= a1 && a1 >= a2, $"se esperaba a0>=a1>=a2, got {a0},{a1},{a2}");
    }

    [Fact]
    public void Alpha_with_non_positive_duration_is_instant()
    {
        // dur<=0 ⇒ la sección está entrada en cuanto su inicio se alcanza (sin tramo).
        Assert.Equal(0.0, Stagger.Alpha(39.0, 1, Stag, 0.0), 9);
        Assert.Equal(1.0, Stagger.Alpha(40.0, 1, Stag, 0.0), 9);
        Assert.Equal(1.0, Stagger.Alpha(41.0, 1, Stag, -5.0), 9);
    }

    // --- OffsetY ----------------------------------------------------------

    [Fact]
    public void OffsetY_is_max_at_alpha_zero()
    {
        Assert.Equal(6, Stagger.OffsetY(0.0, 6));
    }

    [Fact]
    public void OffsetY_is_zero_at_alpha_one()
    {
        Assert.Equal(0, Stagger.OffsetY(1.0, 6));
    }

    [Fact]
    public void OffsetY_interpolates_between_max_and_zero()
    {
        // alfa 0.5 ⇒ a mitad de camino (≈3 px de 6).
        int off = Stagger.OffsetY(0.5, 6);
        Assert.InRange(off, 1, 5);
    }

    [Fact]
    public void OffsetY_clamps_alpha_out_of_range()
    {
        Assert.Equal(6, Stagger.OffsetY(-1.0, 6));
        Assert.Equal(0, Stagger.OffsetY(2.0, 6));
    }

    [Fact]
    public void OffsetY_decreases_as_alpha_grows()
    {
        int prev = int.MaxValue;
        for (double a = 0.0; a <= 1.0; a += 0.1)
        {
            int off = Stagger.OffsetY(a, 6);
            Assert.True(off <= prev, $"offset subió en alpha={a}: {off} > {prev}");
            Assert.InRange(off, 0, 6);
            prev = off;
        }
    }
}
