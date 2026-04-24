using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AlbionPrices.Helpers;

public static class IconHelper
{
    // Matches the app's visual theme
    private static readonly Color Accent     = Color.FromArgb(255, 117,  80); // #FF7550
    private static readonly Color AccentText = Color.FromArgb(255, 165, 100);
    private static readonly Color Background = Color.FromArgb( 26,  21,  32); // #1A1520

    public static System.Drawing.Icon CreateTrayIcon() =>
        BuildIcon(DrawAt(16), DrawAt(32), DrawAt(48));

    public static System.Windows.Media.ImageSource CreateWindowIcon()
    {
        using var icon = BuildIcon(DrawAt(32), DrawAt(48), DrawAt(256));
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }

    private static Bitmap DrawAt(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode        = SmoothingMode.AntiAlias;
        g.InterpolationMode    = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode      = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float pad  = size * 0.06f;
        var   rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

        // Dark coin body
        using var bgBrush = new SolidBrush(Background);
        g.FillEllipse(bgBrush, rect);

        // Gold outer ring
        float borderW = MathF.Max(1f, size * 0.075f);
        using var borderPen = new Pen(Accent, borderW);
        g.DrawEllipse(borderPen, rect);

        // Thinner inner ring detail (gives depth)
        float innerPad = pad + borderW + size * 0.04f;
        var   innerRect = new RectangleF(innerPad, innerPad, size - innerPad * 2, size - innerPad * 2);
        using var innerPen = new Pen(Color.FromArgb(120, Accent), MathF.Max(0.5f, size * 0.02f));
        g.DrawEllipse(innerPen, innerRect);

        // "A" letter centred
        float fontSize = size * 0.50f;
        using var font = new Font("Georgia", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new LinearGradientBrush(
            new PointF(0, 0), new PointF(0, size),
            AccentText, Color.FromArgb(200, 100, 50));
        var sf = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        // Slight shadow for legibility
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

    // Assembles a proper multi-size ICO (PNG-inside-ICO, Vista+ compatible).
    private static System.Drawing.Icon BuildIcon(params Bitmap[] bitmaps)
    {
        var pngs = new byte[bitmaps.Length][];
        for (int i = 0; i < bitmaps.Length; i++)
        {
            using var ms = new MemoryStream();
            bitmaps[i].Save(ms, ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        var stream = new MemoryStream();
        var w      = new BinaryWriter(stream);

        // ICO header
        w.Write((short)0);              // reserved
        w.Write((short)1);              // type: ICO
        w.Write((short)pngs.Length);

        // Directory entries
        int offset = 6 + 16 * pngs.Length;
        for (int i = 0; i < pngs.Length; i++)
        {
            int sz = bitmaps[i].Width;
            w.Write((byte)(sz < 256 ? sz : 0));  // 0 = 256
            w.Write((byte)(sz < 256 ? sz : 0));
            w.Write((byte)0);    // colour count
            w.Write((byte)0);    // reserved
            w.Write((short)1);   // planes
            w.Write((short)32);  // bpp
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs) w.Write(png);

        w.Flush();
        stream.Position = 0;

        foreach (var bmp in bitmaps) bmp.Dispose();
        return new System.Drawing.Icon(stream);
    }
}
