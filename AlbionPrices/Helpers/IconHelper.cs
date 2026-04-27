using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Avalonia.Controls;

namespace AlbionPrices.Helpers;

public static class IconHelper
{
    private static readonly Color Accent     = Color.FromArgb(255, 117,  80);
    private static readonly Color AccentText = Color.FromArgb(255, 165, 100);
    private static readonly Color Background = Color.FromArgb( 26,  21,  32);

    public static WindowIcon CreateTrayIcon()   => MakeWindowIcon(32);
    public static WindowIcon CreateWindowIcon() => MakeWindowIcon(256);

    private static WindowIcon MakeWindowIcon(int size)
    {
        using var bmp = DrawAt(size);
        using var ms  = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    private static Bitmap DrawAt(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float pad  = size * 0.06f;
        var   rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

        using var bgBrush = new SolidBrush(Background);
        g.FillEllipse(bgBrush, rect);

        float borderW = MathF.Max(1f, size * 0.075f);
        using var borderPen = new Pen(Accent, borderW);
        g.DrawEllipse(borderPen, rect);

        float innerPad  = pad + borderW + size * 0.04f;
        var   innerRect = new RectangleF(innerPad, innerPad, size - innerPad * 2, size - innerPad * 2);
        using var innerPen = new Pen(Color.FromArgb(120, Accent), MathF.Max(0.5f, size * 0.02f));
        g.DrawEllipse(innerPen, innerRect);

        float fontSize = size * 0.50f;
        using var font      = new Font("Georgia", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new LinearGradientBrush(
            new PointF(0, 0), new PointF(0, size),
            AccentText, Color.FromArgb(200, 100, 50));
        var sf = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        if (size >= 24)
        {
            using var shadowBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            float off = size * 0.04f;
            g.DrawString("A", font, shadowBrush,
                new RectangleF(off, off + size * 0.03f, size, size), sf);
        }
        g.DrawString("A", font, textBrush, new RectangleF(0, size * 0.03f, size, size), sf);

        return bmp;
    }
}
