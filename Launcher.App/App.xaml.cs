using System.IO;
using System.Windows;
using Launcher.App.Services;

namespace Launcher.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var bootstrap = new BootstrapService(AppContext.BaseDirectory);
            await bootstrap.EnsureDefaultsAsync();
            AppLogService.Initialize(bootstrap.LogsRoot);

            DispatcherUnhandledException += (_, args) =>
            {
                AppLogService.Error($"未处理异常: {args.Exception}");
                MessageBox.Show(
                    "程序发生未处理异常，请查看日志后重试。",
                    "EasyKataGo Launcher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }
        catch (Exception ex)
        {
            var fallbackLogDir = Path.Combine(AppContext.BaseDirectory, "data", "logs");
            Directory.CreateDirectory(fallbackLogDir);
            AppLogService.Initialize(fallbackLogDir);
            AppLogService.Error($"启动失败: {ex}");

            MessageBox.Show(
                $"启动失败：{ex.Message}\n请检查程序目录写权限后重试。",
                "EasyKataGo Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
