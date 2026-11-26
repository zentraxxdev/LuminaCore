using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LuminaCore.UI.Services;

/// <summary>
/// Generates a crisp vector-rendered neon icon in memory for the Windows System Tray.
/// </summary>
public static class TrayIconGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateTrayIcon(bool isBlackout)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Matte-black rounded backdrop
            using var bgBrush = new SolidBrush(Color.FromArgb(245, 13, 15, 22));
            g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);

            if (isBlackout)
            {
                using var dimPen = new Pen(Color.FromArgb(180, 100, 110, 130), 2.5f);
                g.DrawEllipse(dimPen, 4, 4, size - 8, size - 8);
                using var slashPen = new Pen(Color.FromArgb(220, 255, 65, 95), 2.5f);
                g.DrawLine(slashPen, 8, 8, size - 8, size - 8);
            }
            else
            {
                // Neon cyan-to-magenta glowing ring
                using var gradBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, size, size),
                    Color.FromArgb(255, 0, 240, 255),
                    Color.FromArgb(255, 255, 46, 147),
                    45f);
                using var ringPen = new Pen(gradBrush, 3.2f);
                g.DrawEllipse(ringPen, 4.5f, 4.5f, size - 9f, size - 9f);

                // Inner glowing core dot
                using var coreBrush = new SolidBrush(Color.FromArgb(255, 0, 240, 255));
                g.FillEllipse(coreBrush, 11.5f, 11.5f, 9f, 9f);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
