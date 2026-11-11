using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PicSelect.Core;

namespace PicSelect;

/// <summary>跳转输入框：支持「第几张」或文件名匹配。</summary>
public sealed class JumpDialog : Window
{
    private readonly TextBox _box = new();
    public string Query => _box.Text;

    public JumpDialog(int total, int current)
    {
        Title = Loc.T("jump.title");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x23));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        FontFamily = new FontFamily("Microsoft YaHei UI, Yu Gothic UI, Segoe UI");
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = Loc.T("jump.hint", total),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        _box.FontSize = 15;
        _box.Padding = new Thickness(10, 8, 10, 8);
        _box.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        _box.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        _box.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
        _box.CaretBrush = Brushes.White;
        _box.Text = current.ToString();
        _box.SelectAll();
        root.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(MakeButton(Loc.T("jump.cancel"), false, () => { DialogResult = false; }));
        buttons.Children.Add(MakeButton(Loc.T("jump.go"), true, () => { DialogResult = true; }));
        root.Children.Add(buttons);

        Content = root;

        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };

        Loaded += (_, _) => _box.Focus();
    }

    private static Button MakeButton(string text, bool primary, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Width = 88,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(primary
                ? Color.FromRgb(0x16, 0x30, 0x4D)
                : Color.FromRgb(0x2C, 0x2C, 0x2C)),
            Foreground = new SolidColorBrush(primary
                ? Color.FromRgb(0x9C, 0xC6, 0xF0)
                : Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderBrush = new SolidColorBrush(primary
                ? Color.FromRgb(0x2F, 0x5C, 0x8C)
                : Color.FromRgb(0x3C, 0x3C, 0x3C)),
        };
        b.Click += (_, _) => onClick();
        return b;
    }
}
