using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// 生成 PicSelect 的应用图标：矢量绘制 → 多尺寸 ICO
// 用法: GenIcon <out.ico> <previewDir>
internal static class Program
{
    // 设计基准画布 256×256，所有坐标按 size/256 缩放
    const double U = 256.0;

    [STAThread]
    static void Main(string[] args)
    {
        string outIco = args.Length > 0 ? args[0] : "app.ico";
        string previewDir = args.Length > 1 ? args[1] : "preview";
        Directory.CreateDirectory(previewDir);

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<(int Size, byte[] Png, byte[]? Dib)>();

        foreach (int s in sizes)
        {
            var bmp = Render(s);
            var png = EncodePng(bmp);
            File.WriteAllBytes(Path.Combine(previewDir, $"icon{s}.png"), png);
            // 128 以上用 PNG 压缩帧（Vista+ 支持），小尺寸用 32bpp DIB 兼容性最好
            frames.Add((s, png, s >= 128 ? null : ToDib(bmp)));
        }

        WriteIco(outIco, frames);
        Console.WriteLine($"写出 {outIco}  {new FileInfo(outIco).Length / 1024} KB  帧数 {frames.Count}");

        // 预览拼版：小尺寸放大 8 倍（最近邻），方便人眼检查细节
        MakeSheet(Path.Combine(previewDir, "sheet.png"));
    }

    // ────────────────────────────── 绘制 ──────────────────────────────

    static BitmapSource Render(int size)
    {
        double k = size / U;                       // 缩放系数
        int detail = size >= 48 ? 2 : size >= 24 ? 1 : 0;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 背景：深色圆角方块 + 垂直渐变
            var bgRect = new Rect(4 * k, 4 * k, 248 * k, 248 * k);
            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(0x3C, 0x50, 0x66), Color.FromRgb(0x12, 0x19, 0x21), 90);
            dc.DrawRoundedRectangle(bgBrush, null, bgRect, 56 * k, 56 * k);

            // 顶部高光边，让方块有厚度
            if (detail >= 1)
            {
                var hl = new Pen(new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)), 2 * k);
                dc.DrawRoundedRectangle(null, hl, new Rect(6.5 * k, 6.5 * k, 243 * k, 243 * k), 54 * k, 54 * k);
            }

            // ── 照片卡片 ──
            double px = detail == 0 ? 34 : 38;
            double py = detail == 0 ? 44 : 50;
            double pw = detail == 0 ? 168 : 180;
            double ph = detail == 0 ? 138 : 136;
            var photo = new Rect(px * k, py * k, pw * k, ph * k);
            double pr = (detail == 0 ? 20 : 16) * k;

            // 卡片投影（只在大尺寸做）
            if (detail == 2)
            {
                var shadow = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
                dc.DrawRoundedRectangle(shadow, null,
                    new Rect((px + 2) * k, (py + 5) * k, pw * k, ph * k), pr, pr);
            }

            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFA)), null, photo, pr, pr);

            // 卡片里的山与太阳（裁剪在卡片内）
            dc.PushClip(new RectangleGeometry(photo, pr, pr));

            double baseY = py + ph - (detail == 0 ? 10 : 6);
            var mountain = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point((px + 8) * k, baseY * k), IsClosed = true, IsFilled = true };
            if (detail == 0)
            {
                fig.Segments.Add(new LineSegment(new Point((px + pw * 0.46) * k, (py + ph * 0.30) * k), true));
                fig.Segments.Add(new LineSegment(new Point((px + pw - 6) * k, baseY * k), true));
            }
            else
            {
                fig.Segments.Add(new LineSegment(new Point((px + pw * 0.33) * k, (py + ph * 0.40) * k), true));
                fig.Segments.Add(new LineSegment(new Point((px + pw * 0.52) * k, (py + ph * 0.72) * k), true));
                fig.Segments.Add(new LineSegment(new Point((px + pw * 0.68) * k, (py + ph * 0.52) * k), true));
                fig.Segments.Add(new LineSegment(new Point((px + pw - 6) * k, baseY * k), true));
            }
            mountain.Figures.Add(fig);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x3D, 0x5A, 0x78)), null, mountain);

            if (detail == 2)
            {
                double sr = 13 * k;
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF0, 0xB0, 0x3C)), null,
                    new Point((px + pw * 0.74) * k, (py + ph * 0.27) * k), sr, sr);
            }

            dc.Pop();

            // ── 绿色勾选徽章 ──
            double cx = (detail == 0 ? 196 : 200) * k;
            double cy = (detail == 0 ? 190 : 194) * k;
            double rOuter = (detail == 0 ? 48 : 44) * k;
            double rInner = (detail == 0 ? 39 : 37) * k;

            // 外圈用背景色，制造和照片之间的分离
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x14, 0x1B, 0x23)), null,
                new Point(cx, cy), rOuter, rOuter);

            var green = new LinearGradientBrush(
                Color.FromRgb(0x66, 0xD1, 0x77), Color.FromRgb(0x33, 0x93, 0x48), 90);
            dc.DrawEllipse(green, null, new Point(cx, cy), rInner, rInner);

            double t = (detail == 0 ? 16 : detail == 1 ? 13 : 10) * k;
            var check = new PathGeometry();
            var cf = new PathFigure { StartPoint = new Point(cx - rInner * 0.44, cy + rInner * 0.03) };
            cf.Segments.Add(new LineSegment(new Point(cx - rInner * 0.08, cy + rInner * 0.36), true));
            cf.Segments.Add(new LineSegment(new Point(cx + rInner * 0.48, cy - rInner * 0.36), true));
            check.Figures.Add(cf);
            var cp = new Pen(Brushes.White, t)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, cp, check);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ────────────────────────────── 编码 ──────────────────────────────

    static byte[] EncodePng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>转成 ICO 内嵌的 32bpp DIB（BITMAPINFOHEADER + BGRA 倒序 + AND 掩码）。</summary>
    static byte[] ToDib(BitmapSource src)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        int stride = w * 4;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);          // Pbgra32，需还原为直通 alpha

        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a == 0) { px[i] = px[i + 1] = px[i + 2] = 0; }
            else if (a < 255)
            {
                px[i] = (byte)Math.Min(255, px[i] * 255 / a);
                px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
                px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
            }
        }

        int maskStride = ((w + 31) / 32) * 4;
        var mask = new byte[maskStride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] == 0)
                    mask[y * maskStride + (x / 8)] |= (byte)(0x80 >> (x % 8));

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(40);                 // biSize
        bw.Write(w);                  // biWidth
        bw.Write(h * 2);              // biHeight（XOR + AND）
        bw.Write((short)1);           // biPlanes
        bw.Write((short)32);          // biBitCount
        bw.Write(0);                  // biCompression = BI_RGB
        bw.Write(stride * h + mask.Length);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        for (int y = h - 1; y >= 0; y--)
            bw.Write(px, y * stride, stride);       // 倒序写行
        for (int y = h - 1; y >= 0; y--)
            bw.Write(mask, y * maskStride, maskStride);

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>把 16/24/32/48/64/128/256 排成一张预览图，小尺寸按整数倍放大。</summary>
    static void MakeSheet(string path)
    {
        int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
        const int cell = 272;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null,
                new Rect(0, 0, cell * sizes.Length, cell));
            double x = 0;
            foreach (int s in sizes)
            {
                var bmp = Render(s);
                int scale = Math.Max(1, 128 / s);
                dc.DrawImage(bmp, new Rect(x + 8, 8, s * scale, s * scale));
                x += cell;
            }
        }
        var rtb = new RenderTargetBitmap(cell * sizes.Length, cell, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    static void WriteIco(string path, List<(int Size, byte[] Png, byte[]? Dib)> frames)    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);                  // reserved
        bw.Write((short)1);                  // type = icon
        bw.Write((short)frames.Count);

        int offset = 6 + 16 * frames.Count;
        var datas = new List<byte[]>();
        foreach (var f in frames)
        {
            var data = f.Dib ?? f.Png;
            datas.Add(data);
            bw.Write((byte)(f.Size >= 256 ? 0 : f.Size));
            bw.Write((byte)(f.Size >= 256 ? 0 : f.Size));
            bw.Write((byte)0);               // 调色板数
            bw.Write((byte)0);               // reserved
            bw.Write((short)1);              // planes
            bw.Write((short)32);             // bpp
            bw.Write(data.Length);
            bw.Write(offset);
            offset += data.Length;
        }
        foreach (var d in datas) bw.Write(d);
    }
}
