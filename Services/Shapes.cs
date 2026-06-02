using System.Drawing.Drawing2D;

namespace ClaudeBarWin.Services;

/// <summary>
/// Geometría de dibujo centralizada (GDI+). Versión canónica de las rutinas que antes estaban
/// duplicadas en <c>DashboardDataView</c>, <c>DashboardHeader</c>, <c>DashboardSettingsView</c> y
/// <c>TrayIconRenderer</c>. Lleva las guardas de la copia de <c>DashboardDataView</c> (ancho/alto
/// no positivo no pinta; radio &lt;= 1 cae a rectángulo plano).
/// </summary>
public static class Shapes
{
    /// <summary>Rellena un rectángulo de esquinas redondeadas. Si el área no es positiva, no pinta.</summary>
    public static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 1) { g.FillRectangle(b, r); return; }
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }

    /// <summary>Construye el contorno de un rectángulo de esquinas redondeadas (el llamador lo libera).</summary>
    public static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
