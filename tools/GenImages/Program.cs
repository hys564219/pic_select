using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "testdata";
        int count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 40;
        Directory.CreateDirectory(dir);

        foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);

        var palette = new (Color A, Color B)[]
        {
            (Color.FromRgb(30, 60, 110), Color.FromRgb(90, 140, 200)),
            (Color.FromRgb(120, 40, 70), Color.FromRgb(210, 110, 120)),
            (Color.FromRgb(30, 100, 85), Color.FromRgb(110, 190, 150)),
            (Color.FromRgb(140, 105, 35), Color.FromRgb(230, 190, 100)),
            (Color.FromRgb(70, 50, 130), Color.FromRgb(150, 130, 220)),
            (Color.FromRgb(20, 85, 125), Color.FromRgb(80, 170, 200)),
            (Color.FromRgb(130, 60, 30), Color.FromRgb(220, 140, 90)),
            (Color.FromRgb(55, 60, 75), Color.FromRgb(130, 140, 160)),
        };

        var sizes = new (int W, int H)[]
        {
            (4000, 3000), (6000, 4000), (3000, 3000), (2400, 1600),
            (1800, 1200), (900, 600), (700, 950),
        };

        for (int i = 1; i <= count; i++)
        {
            var (w, h) = sizes[i % sizes.Length];
            var (ca, cb) = palette[i % palette.Length];

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new LinearGradientBrush(ca, cb, new Point(0, 0), new Point(1, 1));
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));

                var big = new FormattedText(
                    "img" + i,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    h / 5.5,
                    Brushes.White,
                    1.0);
                dc.DrawText(big, new Point(w * 0.07, h * 0.26));

                var small = new FormattedText(
                    $"{w} x {h}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    h / 20.0,
                    new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    1.0);
                dc.DrawText(small, new Point(w * 0.07, h * 0.60));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            string path = Path.Combine(dir, i % 9 == 0 ? $"img{i}.png" : $"img{i}.jpg");
            BitmapEncoder enc = path.EndsWith(".png")
                ? new PngBitmapEncoder()
                : new JpegBitmapEncoder { QualityLevel = 88 };
            enc.Frames.Add(BitmapFrame.Create(rtb));

            using (var fs = File.Create(path)) enc.Save(fs);

            rtb.Clear();
            Console.WriteLine($"{i}/{count}  {Path.GetFileName(path)}  {w}x{h}");
        }

        var files = new DirectoryInfo(dir).GetFiles();
        Console.WriteLine($"\n完成：{files.Length} 个文件，{files.Sum(f => f.Length) / 1024.0 / 1024.0:F1} MB");
    }
}
