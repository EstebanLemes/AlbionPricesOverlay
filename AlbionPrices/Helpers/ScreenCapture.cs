using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlbionPrices.Helpers;

public class ScreenCapture : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private static readonly string LogPath;
    private static string TessDataPath;
    public static string LastLog { get; private set; } = "";
    private static readonly string CaptureFolder;

    static ScreenCapture()
    {
        CaptureFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlbionPrices", "Captures");
        if (!Directory.Exists(CaptureFolder))
            Directory.CreateDirectory(CaptureFolder);
        
        LogPath = Path.Combine(CaptureFolder, "albion_ocr_log.txt");
        
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var binDebug = Path.Combine(baseDir, "..", "..", "tessdata");
        if (!Directory.Exists(binDebug))
            binDebug = Path.Combine(baseDir, "tessdata");
        if (!Directory.Exists(binDebug))
            binDebug = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "tessdata"));
        
        TessDataPath = Directory.Exists(binDebug) ? binDebug : baseDir;
        
        if (!Directory.Exists(TessDataPath))
            TessDataPath = baseDir;
        
        Log($"TessDataPath: {TessDataPath}, exists: {Directory.Exists(TessDataPath)}");
    }

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        LastLog += line + Environment.NewLine;
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    public static Rectangle GetTooltipArea(int width = 350, int height = 40)
    {
        GetCursorPos(out POINT cursor);
        int x = Math.Max(0, cursor.X - width + 30);
        int y = Math.Max(0, cursor.Y - height - 3);
        return new Rectangle(x, y, width, height);
    }

    public static Bitmap Capture(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size);
        }
        return bitmap;
    }

    private static Bitmap? PreprocessImage(Bitmap source)
    {
        int w = source.Width;
        int h = source.Height;

        // The tooltip is the only element that simultaneously has:
        //   - A very dark background  (R < 60  — the semi-transparent black overlay)
        //   - Near-white text          (R > 200  — item name; anti-aliased edges are 200-225)
        // Game-world bright areas (vegetation, bloom) have white but NOT dark in the same row.
        // Game-world shadow areas have dark but NOT white in the same row.
        // Only the tooltip satisfies BOTH conditions at once.
        int[] rowDark  = new int[h];
        int[] rowWhite = new int[h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var px = source.GetPixel(x, y);
                int b = (px.R + px.G + px.B) / 3;
                if (b < 60) rowDark[y]++;
                else if (px.R > 200 && px.G > 200 && px.B > 200) rowWhite[y]++;
            }

        // A "tooltip row" needs: ≥15% of pixels very dark AND ≥10 near-white pixels.
        // 0.15 * 350 = 52 px — handles small tooltips (~100 px wide) over bright warm-colored
        // backgrounds where the tooltip background only contributes ~70-80 dark pixels per row.
        // whiteMin=10 rejects game-world dark areas that have no text.
        int darkThresh  = (int)(w * 0.15);   // ~52 px out of 350
        const int whiteMin = 10;

        int tooltipMinY = -1, tooltipMaxY = -1;
        int peakRow = -1, peakWhite = 0;
        for (int y = 0; y < h; y++)
        {
            if (rowDark[y] < darkThresh || rowWhite[y] < whiteMin) continue;
            if (tooltipMinY < 0) tooltipMinY = y;
            tooltipMaxY = y;
            if (rowWhite[y] > peakWhite) { peakWhite = rowWhite[y]; peakRow = y; }
        }

        if (tooltipMinY < 0)
        {
            Log($"PreprocessImage: bimodal test failed (maxDark={rowDark.Max()}, maxWhite={rowWhite.Max()})");
            return null;
        }

        // In the peak tooltip row, find the LONGEST contiguous run of pure-white pixels.
        // Word gaps are ~4-6 px (bridge with maxGap=8); tier-indicator gap is ~15+ px (excluded).
        const int maxGap = 8;
        int bestStart = -1, bestEnd = -1, bestLen = 0;
        int runStart = -1, lastWhiteX = -1;
        for (int x = 0; x < w; x++)
        {
            var px = source.GetPixel(x, peakRow);
            bool isW = px.R > 200 && px.G > 200 && px.B > 200;
            if (isW)
            {
                if (runStart < 0) runStart = x;
                lastWhiteX = x;
            }
            else if (runStart >= 0 && x - lastWhiteX > maxGap)
            {
                int len = lastWhiteX - runStart + 1;
                if (len > bestLen) { bestLen = len; bestStart = runStart; bestEnd = lastWhiteX; }
                runStart = -1;
            }
        }
        if (runStart >= 0)
        {
            int len = lastWhiteX - runStart + 1;
            if (len > bestLen) { bestLen = len; bestStart = runStart; bestEnd = lastWhiteX; }
        }

        if (bestLen < 15)
        {
            Log($"PreprocessImage: text run too short ({bestLen}px)");
            return null;
        }

        int textMinX = bestStart, textMaxX = bestEnd;

        // Vertical extent: tooltip rows that have ≥2 pure-white pixels within [textMinX, textMaxX].
        int textMinY = -1, textMaxY = -1;
        for (int y = tooltipMinY; y <= tooltipMaxY; y++)
        {
            if (rowDark[y] < darkThresh) continue;
            int cnt = 0;
            for (int x = textMinX; x <= textMaxX; x++)
            {
                var px = source.GetPixel(x, y);
                if (px.R > 200 && px.G > 200 && px.B > 200) cnt++;
            }
            if (cnt < 2) continue;
            if (textMinY < 0) textMinY = y;
            textMaxY = y;
        }

        if (textMinY < 0)
        {
            Log("PreprocessImage: no vertical text extent");
            return null;
        }

        Log($"PreprocessImage: tooltip rows {tooltipMinY}-{tooltipMaxY}, text cols {textMinX}-{textMaxX} ({bestLen}px run), peakWhite={peakWhite}");

        // Crop to the text bounding box plus a margin so Tesseract sees full character shapes.
        int margin = 5;
        int cropX = Math.Max(0, textMinX - margin);
        int cropY = Math.Max(0, textMinY - margin);
        int cropW = Math.Min(w - cropX, textMaxX - textMinX + margin * 2 + 1);
        int cropH = Math.Min(h - cropY, textMaxY - textMinY + margin * 2 + 1);

        if (cropW < 10 || cropH < 5)
        {
            Log("PreprocessImage: text crop too small");
            return null;
        }

        var cropped = source.Clone(new Rectangle(cropX, cropY, cropW, cropH), source.PixelFormat);

        // Scale 3x — Tesseract accuracy improves significantly with larger text.
        const int scale = 3;
        var scaled = new Bitmap(cropped.Width * scale, cropped.Height * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(cropped, 0, 0, scaled.Width, scaled.Height);
        }
        cropped.Dispose();

        // Binarise: white text → black (Tesseract foreground), dark bg → white.
        for (int y = 0; y < scaled.Height; y++)
            for (int x = 0; x < scaled.Width; x++)
            {
                var p = scaled.GetPixel(x, y);
                int brightness = (p.R + p.G + p.B) / 3;
                scaled.SetPixel(x, y, brightness > 140 ? Color.Black : Color.White);
            }

        Log($"PreprocessImage: final {scaled.Width}x{scaled.Height}");
        return scaled;
    }

    public static async Task<string> ExtractTextAsync()
    {
        return await Task.Run<string>(() =>
        {
            try
            {
                Log("=== OCR START ===");
                LastLog = "";
                
                if (!Directory.Exists(TessDataPath))
                {
                    Log("ERROR: Tessdata not found at " + TessDataPath);
                    return "";
                }
                Log("Tessdata found: " + TessDataPath);

                var area = GetTooltipArea(350, 40);
                Log($"Capturing area: X={area.X}, Y={area.Y}, W={area.Width}, H={area.Height}");

                var raw = Capture(area);
                Log("Screenshot captured, size: " + raw.Size);

                var processed = PreprocessImage(raw);
                Bitmap bitmap;
                if (processed != null)
                {
                    raw.Dispose();
                    bitmap = processed;
                }
                else
                {
                    Log("Preprocessing found no tooltip, using raw capture");
                    bitmap = raw;
                }

                var fileName = $"albion_ocr_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var savePath = Path.Combine(CaptureFolder, fileName);
                bitmap.Save(savePath, ImageFormat.Png);
                Log($"Saved to: {savePath}");
                
                bitmap.Dispose();

                using var image = Tesseract.Pix.LoadFromFile(savePath);
                if (image == null)
                {
                    Log("ERROR: Failed to load image for OCR");
                    return "";
                }
                Log("Image loaded for OCR, depth: " + image.Depth);

                using var engine = new Tesseract.TesseractEngine(
                    TessDataPath, 
                    "eng", 
                    Tesseract.EngineMode.Default,
                    new[] { "tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 '-@." });
                Log("Tesseract engine created");
                
                using var page = engine.Process(image, Tesseract.PageSegMode.SingleBlock);
                var text = page.GetText()?.Trim() ?? string.Empty;
                var confidence = page.GetMeanConfidence();
                Log($"OCR result: '{text}' (confidence: {confidence:F2})");
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log($"No text detected. Image at: {savePath}");
                }
                
                return text;
            }
            catch (Exception ex)
            {
                Log("EXCEPTION: " + ex.Message + "\n" + ex.StackTrace);
                return "";
            }
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}