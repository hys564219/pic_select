using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PicSelect.Core;

namespace PicSelect;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        Loc.Init();

        // 测试用：强制界面语言（不写配置文件）
        string? langArg = e.Args.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase));
        if (langArg is not null)
        {
            Loc.ForceLang(ParseLang(langArg.Substring("--lang=".Length)));
        }

        // 测试用：写入语言偏好后立即退出，用于验证配置持久化
        string? setLangArg = e.Args.FirstOrDefault(a => a.StartsWith("--setlang=", StringComparison.OrdinalIgnoreCase));
        if (setLangArg is not null)
        {
            Loc.Set(ParseLang(setLangArg.Substring("--setlang=".Length)));
            Shutdown();
            return;
        }

        // 模拟低配机：强制软件渲染，用于验证无 GPU 加速时的表现
        if (e.Args.Any(a => a.Equals("--sw", StringComparison.OrdinalIgnoreCase)))
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var win = new MainWindow();
        MainWindow = win;

        if (e.Args.Any(a => a.Equals("--autorestore", StringComparison.OrdinalIgnoreCase)))
            win.AutoRestore = true;

        win.Show();

        string? sizeArg = e.Args.FirstOrDefault(a => a.StartsWith("--size=", StringComparison.OrdinalIgnoreCase));
        if (sizeArg is not null)
        {
            var parts = sizeArg.Substring("--size=".Length).Split('x');
            if (parts.Length == 2 && double.TryParse(parts[0], out var sw) && double.TryParse(parts[1], out var sh))
            {
                win.Width = sw;
                win.Height = sh;
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Left = 10;
                win.Top = 10;
            }
        }

        string? openArg = e.Args.FirstOrDefault(a => a.StartsWith("--open=", StringComparison.OrdinalIgnoreCase));
        if (openArg is not null)
            win.OpenFolderExternal(openArg.Substring("--open=".Length));

        if (e.Args.Any(a => a.Equals("--mark", StringComparison.OrdinalIgnoreCase)))
            win.MarkCurrentForTest();

        string? shotArg = e.Args.FirstOrDefault(a => a.StartsWith("--shot", StringComparison.OrdinalIgnoreCase));
        if (shotArg is not null)
        {
            string file = shotArg.Contains('=') ? shotArg.Split('=', 2)[1] : "shot.png";
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(2200);
                try { win.CaptureTo(Path.Combine(AppContext.BaseDirectory, file)); }
                catch { }
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        string? benchArg = e.Args.FirstOrDefault(a => a.StartsWith("--bench=", StringComparison.OrdinalIgnoreCase));
        if (benchArg is not null)
        {
            string folder = benchArg.Substring("--bench=".Length);
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunBenchAsync(folder, 36);
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        string? switchArg = e.Args.FirstOrDefault(a => a.StartsWith("--langswitch", StringComparison.OrdinalIgnoreCase));
        if (switchArg is not null)
        {
            string folder = switchArg.Contains('=') ? switchArg.Split('=', 2)[1] : "";
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunLangSwitchTestAsync(folder);
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        if (e.Args.Any(a => a.Equals("--clicktest", StringComparison.OrdinalIgnoreCase)))
        {
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(900);
                await win.RunClickTestAsync();
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        if (e.Args.Any(a => a.Equals("--langtest", StringComparison.OrdinalIgnoreCase)))
        {
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunLangTestAsync();
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        string? keepLastArg = e.Args.FirstOrDefault(a => a.StartsWith("--keeplast=", StringComparison.OrdinalIgnoreCase));        if (keepLastArg is not null)
        {
            string folder = keepLastArg.Substring("--keeplast=".Length);
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunKeepLastTestAsync(folder);
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        string? jumpArg = e.Args.FirstOrDefault(a => a.StartsWith("--jumptest=", StringComparison.OrdinalIgnoreCase));        if (jumpArg is not null)
        {
            string folder = jumpArg.Substring("--jumptest=".Length);
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunJumpTestAsync(folder);
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }

        string? delArg = e.Args.FirstOrDefault(a => a.StartsWith("--autodelete=", StringComparison.OrdinalIgnoreCase));        if (delArg is not null)
        {
            string folder = delArg.Substring("--autodelete=".Length);
            win.Dispatcher.BeginInvoke(async () =>
            {
                await Task.Delay(600);
                await win.RunDeleteTestAsync(folder);
                Shutdown();
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static Lang ParseLang(string s) => s.Trim().ToLowerInvariant() switch
    {
        "zh" => Lang.Zh,
        "ja" => Lang.Ja,
        _ => Lang.En,
    };

    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "发生了一个未预期的错误：\n\n" + e.Exception.Message,
            "图片筛选",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
