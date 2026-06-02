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

    public static Icon Render(int percent, Color bg, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg, pending);
    }

    /// <summary>Neutral badge for "no data / auth expired / offline".</summary>
    public static Icon RenderError(Color bg, bool pending = false) => RenderBadge("!", bg, pending);

    private static Icon RenderBadge(string text, Color bg, bool pending)
    {
        // Render at high resolution (48px) so Windows downscales to a crisp tray icon on any DPI.
        const int size = 48;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            using (var brush = new SolidBrush(bg))
                FillRounded(g, brush, new Rectangle(0, 0, size - 1, size - 1), 11);

            // Larger glyph that fills more of the badge → readable even when shrunk into the tray.
            // 3-char ("99+") gets a smaller size and NoWrap so it stays on a single line (no "99 / +").
            float fontPx = text.Length >= 3 ? 18f : 30f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, -2f, size, size), sf);

            if (pending)
            {
                var amber = Color.FromArgb(0xF5, 0xA6, 0x23);
                int d = 18;
                var badge = new Rectangle(size - d - 1, 0, d, d);
                using var fill = new SolidBrush(amber);
                using var ring = new Pen(Color.FromArgb(0x1A, 0x1A, 0x1A), 2.5f);
                g.FillEllipse(fill, badge);
                g.DrawEllipse(ring, badge);
            }
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
