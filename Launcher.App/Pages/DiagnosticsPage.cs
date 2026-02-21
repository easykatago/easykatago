using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.Core.Services;

namespace Launcher.App.Pages;

public sealed class DiagnosticsPage : Page
{
    private readonly BootstrapService _bootstrap;
    private readonly ListBox _checksList;
    private readonly TextBlock _statusText;
    private readonly Button _runBtn;
    private readonly Button _exportBtn;
    private bool _isBusy;

    public DiagnosticsPage()
    {
        _bootstrap = new BootstrapService(AppContext.BaseDirectory);
        Background = Brushes.Transparent;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(6, 2, 6, 20) };

        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "诊断", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock
                    {
                        Text = "执行健康自检，并支持导出诊断包（logs / profiles / manifest 快照）。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        var checksCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };
        var checksPanel = new StackPanel();
        checksPanel.Children.Add(new TextBlock { Text = "健康检查", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _checksList = new ListBox { MinHeight = 180 };
        checksPanel.Children.Add(_checksList);
        checksCard.Child = checksPanel;
        root.Children.Add(checksCard);

        var actionCard = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
        var actionPanel = new StackPanel();
        var actionWrap = new WrapPanel();

        _runBtn = new Button { Content = "运行自检", Width = 120, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
        _runBtn.Click += async (_, _) => await RunChecksAsync();

        _exportBtn = new Button { Content = "导出诊断包", Width = 130, Height = 36 };
        _exportBtn.Click += async (_, _) => await ExportAsync();

        actionWrap.Children.Add(_runBtn);
        actionWrap.Children.Add(_exportBtn);
        actionPanel.Children.Add(actionWrap);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Text = "待执行。"
        };
        actionPanel.Children.Add(_statusText);
        actionCard.Child = actionPanel;
        root.Children.Add(actionCard);

        scroll.Content = root;
        Content = scroll;
        Loaded += async (_, _) => await RunChecksAsync();
    }

    private BasicDiagnosticsService CreateDiagnosticsService()
    {
        return new BasicDiagnosticsService(
            AppContext.BaseDirectory,
            _bootstrap.LogsRoot,
            _bootstrap.ProfilesPath,
            _bootstrap.ManifestSnapshotPath);
    }

    private async Task RunChecksAsync()
    {
        if (_isBusy)
        {
            return;
        }

        await ExecuteBusyAsync("正在运行自检...", async () =>
        {
            await _bootstrap.EnsureDefaultsAsync();
            var checks = await CreateDiagnosticsService().RunHealthChecksAsync();

            _checksList.Items.Clear();
            foreach (var check in checks)
            {
                _checksList.Items.Add($"{(check.IsSuccess ? "✓" : "✗")} {check.Name} - {check.Message}");
            }

            var failCount = checks.Count(x => !x.IsSuccess);
            _statusText.Text = failCount == 0 ? "自检通过。" : $"自检完成：{failCount} 项异常。";
            _statusText.Foreground = failCount == 0 ? Brushes.ForestGreen : Brushes.IndianRed;
            AppLogService.Info($"运行自检完成: fail={failCount}");
        });
    }

    private async Task ExportAsync()
    {
        if (_isBusy)
        {
            return;
        }

        await ExecuteBusyAsync("正在导出诊断包...", async () =>
        {
            await _bootstrap.EnsureDefaultsAsync();
            var diagnosticsDir = Path.Combine(_bootstrap.DataRoot, "diagnostics");
            var zipPath = await CreateDiagnosticsService().ExportZipAsync(diagnosticsDir);

            _statusText.Text = $"导出成功：{zipPath}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"导出诊断包: {zipPath}");
            MessageBox.Show($"诊断包已导出：\n{zipPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async Task ExecuteBusyAsync(string busyText, Func<Task> action)
    {
        SetBusy(true, busyText);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"操作失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
            AppLogService.Warn($"诊断操作失败: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? statusText = null)
    {
        _isBusy = busy;
        _runBtn.IsEnabled = !busy;
        _exportBtn.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            _statusText.Text = statusText;
            _statusText.Foreground = (Brush)Application.Current.Resources["Brush.Accent"];
        }
    }
}
