using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;

namespace Launcher.App.Pages;

public sealed class HomePage : Page
{
    private readonly Action<string> _navigate;
    private readonly LauncherStateService _stateService = new();
    private readonly LaunchWorkflowService _launchWorkflowService = new();
    private readonly TextBlock _statusBadge;
    private readonly TextBlock _profileText;
    private readonly TextBlock _backendText;
    private readonly TextBlock _networkText;
    private readonly TextBlock _tuningText;
    private readonly TextBlock _healthText;
    private readonly TextBlock _dataRootText;
    private readonly TextBlock _launchStatusText;

    public HomePage(Action<string> navigate)
    {
        _navigate = navigate;
        Background = Brushes.Transparent;

        var scroll = new ScrollViewer();
        var root = new StackPanel { Margin = new Thickness(6, 2, 6, 20) };

        var headerCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "首页",
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "检查当前安装状态、默认 Profile 和调优状态。",
            FontSize = 15,
            Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray)
        });

        _statusBadge = new TextBlock
        {
            Text = "检查中",
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("Brush.Accent", Brushes.SteelBlue),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_statusBadge, 1);

        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(_statusBadge);
        headerCard.Child = headerGrid;
        root.Children.Add(headerCard);

        var quickActionsCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };
        var actions = new StackPanel();
        actions.Children.Add(new TextBlock
        {
            Text = "快速操作",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var actionsGrid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        actionsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var refreshButton = CreateActionButton("刷新概览", new Thickness(0, 0, 10, 0));
        refreshButton.Click += (_, _) => RefreshSnapshot();

        var installButton = CreateActionButton("前往安装向导", new Thickness(0, 0, 10, 0));
        installButton.Click += (_, _) => _navigate("Install");

        var launchButton = CreateActionButton("一键启动 LizzieYzy", new Thickness(0, 0, 0, 0));
        launchButton.Click += async (_, _) => await LaunchDefaultAsync();

        Grid.SetColumn(refreshButton, 0);
        Grid.SetColumn(installButton, 1);
        Grid.SetColumn(launchButton, 2);

        actionsGrid.Children.Add(refreshButton);
        actionsGrid.Children.Add(installButton);
        actionsGrid.Children.Add(launchButton);
        actions.Children.Add(actionsGrid);

        _launchStatusText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray),
            Text = "启动器待命。"
        };
        actions.Children.Add(_launchStatusText);
        quickActionsCard.Child = actions;
        root.Children.Add(quickActionsCard);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var profileCard = BuildInfoCard("默认 Profile");
        _profileText = AddInfoLine(profileCard, "名称：");
        _backendText = AddInfoLine(profileCard, "后端：");
        _networkText = AddInfoLine(profileCard, "权重：");
        Grid.SetColumn(profileCard, 0);
        Grid.SetRow(profileCard, 0);
        grid.Children.Add(profileCard);

        var tuningCard = BuildInfoCard("调优与健康");
        _tuningText = AddInfoLine(tuningCard, "调优状态：");
        _healthText = AddInfoLine(tuningCard, "配置完整性：");
        _dataRootText = AddInfoLine(tuningCard, "数据目录：");
        Grid.SetColumn(tuningCard, 1);
        Grid.SetRow(tuningCard, 0);
        grid.Children.Add(tuningCard);

        root.Children.Add(grid);

        scroll.Content = root;
        Content = scroll;
        Loaded += (_, _) => RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var snapshot = _stateService.GetSnapshot(AppContext.BaseDirectory);
        _statusBadge.Text = snapshot.IsInitialized ? "已初始化" : "未初始化";
        _profileText.Text = snapshot.DefaultProfileName ?? "未生成";
        _backendText.Text = snapshot.EngineBackend ?? "未知";
        _networkText.Text = snapshot.NetworkName ?? "未知";
        _tuningText.Text = string.IsNullOrWhiteSpace(snapshot.TuningStatus) ? "unknown" : snapshot.TuningStatus;
        _healthText.Text = snapshot.HasManifest && snapshot.HasProfiles && snapshot.HasSettings
            ? $"完整（日志文件 {snapshot.LogFileCount} 个）"
            : "不完整";
        _dataRootText.Text = snapshot.DataRoot;
        AppLogService.Info("刷新首页状态");
    }

    private async Task LaunchDefaultAsync()
    {
        try
        {
            _launchStatusText.Text = "正在启动...";
            _launchStatusText.Foreground = AppBrush("Brush.Accent", Brushes.SteelBlue);

            var result = await _launchWorkflowService.LaunchDefaultAsync(AppContext.BaseDirectory);
            AppLogService.Info($"执行一键启动: success={result.Success}");
            _launchStatusText.Text = result.Message;
            _launchStatusText.Foreground = result.Success ? Brushes.ForestGreen : Brushes.IndianRed;

            if (!result.Success)
            {
                MessageBox.Show(result.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);

                if (!string.IsNullOrWhiteSpace(result.CopyableCommand))
                {
                    var copy = MessageBox.Show(
                        "是否复制引擎命令到剪贴板以便手动配置？",
                        "手动兜底",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (copy == MessageBoxResult.Yes)
                    {
                        Clipboard.SetText(result.CopyableCommand);
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.ManualGuide))
                {
                    MessageBox.Show(result.ManualGuide, "手动配置指引", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(result.CopyableCommand))
            {
                var prompt = string.IsNullOrWhiteSpace(result.ManualGuide)
                    ? "自动写入引擎配置失败，是否复制命令用于手动配置？"
                    : $"{result.ManualGuide}\n\n是否复制命令用于手动配置？";
                var need = MessageBox.Show(
                    prompt,
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (need == MessageBoxResult.Yes)
                {
                    Clipboard.SetText(result.CopyableCommand);
                }
            }
        }
        catch (Exception ex)
        {
            _launchStatusText.Text = $"启动异常：{ex.Message}";
            _launchStatusText.Foreground = Brushes.IndianRed;
            MessageBox.Show($"启动异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Border BuildInfoCard(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 12, 12),
            Child = panel
        };
    }

    private static TextBlock AddInfoLine(Border card, string label)
    {
        var panel = (StackPanel)card.Child;
        var wrap = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2)
        };
        wrap.Children.Add(new TextBlock
        {
            Text = label,
            Width = 90,
            Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray)
        });

        var value = new TextBlock
        {
            Text = "-",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        wrap.Children.Add(value);
        panel.Children.Add(wrap);
        return value;
    }

    private static Button CreateActionButton(string text, Thickness margin)
    {
        return new Button
        {
            Content = text,
            Height = 38,
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static Brush AppBrush(string key, Brush fallback)
    {
        return Application.Current.Resources[key] as Brush ?? fallback;
    }
}
