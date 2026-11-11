using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PicSelect.Controls;

/// <summary>
/// 可缩放/平移的图片显示区。缩放锚点跟随鼠标，双击在「适应窗口」与「100%」之间切换。
/// </summary>
public sealed class ImageViewer : Grid
{
    private readonly Image _image = new();
    private readonly Canvas _canvas = new();
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _offset = new();

    private BitmapSource? _source;
    private bool _dragging;
    private Point _dragOrigin;
    private Point _offsetOrigin;
    private bool _fitMode = true;

    /// <summary>缩放比例变化（用于触发高清图加载）。</summary>
    public event EventHandler? ZoomChanged;

    public double ZoomLevel => _scale.ScaleX;
    public bool IsFitMode => _fitMode;
    public string SourceSize => _source is null ? "none" : $"{_source.PixelWidth}x{_source.PixelHeight}";

    /// <summary>当前图「适应窗口」时的缩放比例。</summary>
    public double FitZoom => _source is null || ActualWidth <= 0 || ActualHeight <= 0
        ? 0
        : Math.Min(1.0, Math.Min(ActualWidth / _source.PixelWidth, ActualHeight / _source.PixelHeight));

    /// <summary>测试用：把缩放强制设到指定倍数。</summary>
    public void SetZoomForTest(double zoom)
    {
        if (_source is null) return;
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), zoom / _scale.ScaleX);
    }

    public string DebugRect(Visual ancestor)
    {
        try
        {
            var t = _image.TransformToAncestor(ancestor);
            var a = t.Transform(new Point(0, 0));
            var b = t.Transform(new Point(_image.ActualWidth, _image.ActualHeight));
            return $"imgRect=({a.X:F1},{a.Y:F1})-({b.X:F1},{b.Y:F1}) " +
                   $"imgActual={_image.ActualWidth:F1}x{_image.ActualHeight:F1} " +
                   $"imgRender={_image.RenderSize.Width:F1}x{_image.RenderSize.Height:F1} " +
                   $"imgDesired={_image.DesiredSize.Width:F1}x{_image.DesiredSize.Height:F1} " +
                   $"srcDpi={(_source?.DpiX ?? 0):F1} " +
                   $"viewerRender={RenderSize.Width:F1}x{RenderSize.Height:F1} " +
                   $"scale={_scale.ScaleX:F4} off=({_offset.X:F1},{_offset.Y:F1})";
        }
        catch (Exception ex) { return "err:" + ex.Message; }
    }

    public ImageViewer()
    {
        ClipToBounds = true;
        Focusable = true;

        _image.Stretch = Stretch.Fill;
        _image.HorizontalAlignment = HorizontalAlignment.Left;
        _image.VerticalAlignment = VerticalAlignment.Top;
        _image.RenderTransformOrigin = new Point(0, 0);
        _image.SnapsToDevicePixels = true;

        // 硬件加速不可用（驱动问题 / 远程桌面 / 老核显回退）时，
        // HighQuality 的 Fant 重采样会跑到 CPU 上，4K 图每帧上百毫秒
        int tier = RenderCapability.Tier >> 16;
        RenderOptions.SetBitmapScalingMode(_image,
            tier >= 2 ? BitmapScalingMode.HighQuality : BitmapScalingMode.LowQuality);

        var g = new TransformGroup();
        g.Children.Add(_scale);
        g.Children.Add(_offset);
        _image.RenderTransform = g;

        // Canvas 不约束也不裁剪子元素，否则超出容器的部分会在变换前被裁掉
        _canvas.Children.Add(_image);
        Children.Add(_canvas);

        SizeChanged += (_, _) => { if (_fitMode) FitToWindow(); };
        MouseWheel += OnWheel;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => { _dragging = false; Cursor = Cursors.Arrow; };
    }

    public void SetImage(BitmapSource? src)
    {
        _source = src;
        _image.Source = src;

        if (src is null)
        {
            _image.Width = 0;
            _image.Height = 0;
            return;
        }

        _image.Width = src.PixelWidth;
        _image.Height = src.PixelHeight;

        // 换图一律回到「适应窗口」，不继承上一张的缩放和平移
        _fitMode = true;
        FitToWindow();
    }

    /// <summary>换成更高分辨率的同尺寸画面，保持屏幕上的视觉大小不变。</summary>
    public void ReplaceSource(BitmapSource src)
    {
        if (_source is null)
        {
            SetImage(src);
            return;
        }

        double ratio = (double)_source.PixelWidth / src.PixelWidth;
        _source = src;
        _image.Source = src;
        _image.Width = src.PixelWidth;
        _image.Height = src.PixelHeight;
        _scale.ScaleX *= ratio;
        _scale.ScaleY *= ratio;

        ClampOffset();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FitToWindow()
    {
        if (_source is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        double sw = _source.PixelWidth;
        double sh = _source.PixelHeight;
        double s = Math.Min(ActualWidth / sw, ActualHeight / sh);
        if (s > 1.0) s = 1.0;   // 小图不放大，保持清晰

        _fitMode = true;
        _scale.ScaleX = _scale.ScaleY = s;
        _offset.X = (ActualWidth - sw * s) / 2;
        _offset.Y = (ActualHeight - sh * s) / 2;

        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActualSize()
    {
        if (_source is null) return;
        _fitMode = false;
        _scale.ScaleX = _scale.ScaleY = 1.0;
        _offset.X = (ActualWidth - _source.PixelWidth) / 2;
        _offset.Y = (ActualHeight - _source.PixelHeight) / 2;
        ClampOffset();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_source is null) return;
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1.0 / 1.15);
        e.Handled = true;
    }

    private void ZoomAt(Point p, double factor)
    {
        double old = _scale.ScaleX;
        if (old <= 0) return;

        double nw = Math.Clamp(old * factor, 0.02, 32.0);
        if (Math.Abs(nw - old) < 1e-9) return;

        double k = nw / old;
        _offset.X = p.X - (p.X - _offset.X) * k;
        _offset.Y = p.Y - (p.Y - _offset.Y) * k;
        _scale.ScaleX = _scale.ScaleY = nw;
        _fitMode = false;

        ClampOffset();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_source is null) return;

        if (e.ClickCount == 2)
        {
            if (_fitMode) ActualSize();
            else FitToWindow();
            e.Handled = true;
            return;
        }

        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        _offsetOrigin = new Point(_offset.X, _offset.Y);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        _offset.X = _offsetOrigin.X + (p.X - _dragOrigin.X);
        _offset.Y = _offsetOrigin.Y + (p.Y - _dragOrigin.Y);
        ClampOffset();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void ClampOffset()
    {
        if (_source is null) return;

        double sw = _source.PixelWidth * _scale.ScaleX;
        double sh = _source.PixelHeight * _scale.ScaleY;

        if (sw <= ActualWidth) _offset.X = (ActualWidth - sw) / 2;
        else _offset.X = Math.Clamp(_offset.X, ActualWidth - sw, 0);

        if (sh <= ActualHeight) _offset.Y = (ActualHeight - sh) / 2;
        else _offset.Y = Math.Clamp(_offset.Y, ActualHeight - sh, 0);
    }
}
