using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicSelect.Core;

public sealed class DecodedImage
{
    public required BitmapSource Bitmap { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int Orientation { get; init; }
    public bool IsFullResolution { get; init; }

    /// <summary>解码后占用的近似内存（BGRA32）。</summary>
    public long Bytes => (long)Bitmap.PixelWidth * Bitmap.PixelHeight * 4;
}

public static class ImageDecoder
{
    /// <summary>
    /// 解码一张图。全程不持有文件句柄，解码完文件即可删除。
    /// </summary>
    /// <param name="maxWidth">预览模式下的目标显示宽度（像素），0 表示不限制</param>
    public static DecodedImage Decode(string path, int maxWidth, int maxHeight, bool fullResolution = false)
    {
        var (srcW, srcH, orientation) = ReadHeader(path);

        bool swap = orientation is >= 5 and <= 8;
        int dispW = swap ? srcH : srcW;
        int dispH = swap ? srcW : srcH;

        int targetW = srcW;
        if (!fullResolution && maxWidth > 0 && maxHeight > 0)
        {
            double scale = Math.Min((double)maxWidth / dispW, (double)maxHeight / dispH);
            if (scale < 1.0)
                targetW = Math.Max(1, (int)Math.Round(srcW * scale));
        }

        var bmp = new BitmapImage();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                       FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;          // 读完即脱离流，不留句柄
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = fs;
            if (targetW < srcW) bmp.DecodePixelWidth = targetW;   // 让解码器直接降采样
            bmp.EndInit();
        }
        bmp.Freeze();

        BitmapSource result = bmp;
        var tf = OrientationTransform(orientation);
        if (tf is not null)
        {
            var tb = new TransformedBitmap();
            tb.BeginInit();
            tb.Source = bmp;
            tb.Transform = tf;
            tb.EndInit();
            tb.Freeze();
            result = tb;
        }

        return new DecodedImage
        {
            Bitmap = result,
            SourceWidth = srcW,
            SourceHeight = srcH,
            Orientation = orientation,
            IsFullResolution = targetW >= srcW,
        };
    }

    /// <summary>只读文件头，拿到原始尺寸与 EXIF 方向。开销极小。</summary>
    public static (int Width, int Height, int Orientation) ReadHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite | FileShare.Delete, 4096);
        var decoder = BitmapDecoder.Create(fs,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);

        var frame = decoder.Frames[0];
        int w = frame.PixelWidth;
        int h = frame.PixelHeight;
        int o = 1;

        if (frame.Metadata is BitmapMetadata md)
        {
            try
            {
                object? v = md.GetQuery("/app1/ifd/{ushort=274}");
                o = v switch
                {
                    ushort us when us is >= 1 and <= 8 => us,
                    int iv when iv is >= 1 and <= 8 => iv,
                    short sv when sv is >= 1 and <= 8 => sv,
                    byte bv when bv is >= 1 and <= 8 => bv,
                    _ => 1,
                };
            }
            catch { o = 1; }
        }

        return (w, h, o);
    }

    private static Transform? OrientationTransform(int o) => o switch
    {
        2 => new ScaleTransform(-1, 1),
        3 => new RotateTransform(180),
        4 => new ScaleTransform(1, -1),
        5 => Group(new ScaleTransform(-1, 1), new RotateTransform(90)),
        6 => new RotateTransform(90),
        7 => Group(new ScaleTransform(-1, 1), new RotateTransform(270)),
        8 => new RotateTransform(270),
        _ => null,
    };

    private static TransformGroup Group(params Transform[] items)
    {
        var g = new TransformGroup();
        foreach (var t in items) g.Children.Add(t);
        return g;
    }
}
