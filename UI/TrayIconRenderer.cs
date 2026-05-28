using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeBarWin.UI;

public enum UsageStatus
{
    Ok = 0,
    Warn = 1,
    Critical = 2
}

/// <summary>
/// Draws a small badge icon (percentage + status colour) for the system tray.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(int percent, Color bg)
    {
        percent = Math.Clamp(percent, 0, 999);
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg);
    }

    /// <summary>Neutral badge for "no data / auth expired / offline".</summary>
    public static Icon RenderError(Color bg) => RenderBadge("!", bg);

    private static Icon RenderBadge(string text, Color bg)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var brush = new SolidBrush(bg))
                FillRounded(g, brush, new Rectangle(0, 0, size - 1, size - 1), 8);

            float fontPx = text.Length >= 3 ? 13f : 17f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, -1, size, size), sf);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            using var ms = new MemoryStream();
            tmp.Save(ms);
            ms.Position = 0;
            return new Icon(ms);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }
}
