using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.Core.Models;

namespace Launcher.App.Pages;

public sealed class SettingsPage : Page
{
    private readonly SettingsStoreService _settingsStoreService = new();
    private readonly BootstrapService _bootstrap;
    private readonly TextBox _installRootBox;
    private readonly ComboBox _proxyModeBox;
    private readonly TextBox _proxyAddressBox;
    private readonly TextBox _concurrencyBox;
    private readonly TextBox _retriesBox;
    private readonly TextBox _keepVersionsBox;
    private readonly ComboBox _themeBox;
    private readonly ComboBox _accentBox;
    private readonly TextBlock _statusText;
    private CudaModel _cuda = new();

    public SettingsPage()
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
                    new TextBlock { Text = "设置", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) },
                    new TextBlock
                    {
                        Text = "编辑 settings.json：代理、下载并发、缓存保留版本、界面主题。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        var formCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 8; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _installRootBox = AddTextRow(form, 0, "安装根目录");
        _proxyModeBox = AddComboRow(form, 1, "代理模式", "system", "manual", "none");
        _proxyAddressBox = AddTextRow(form, 2, "代理地址");
        _concurrencyBox = AddTextRow(form, 3, "下载并发");
        _retriesBox = AddTextRow(form, 4, "重试次数");
        _keepVersionsBox = AddTextRow(form, 5, "缓存保留版本");
        _themeBox = AddComboRow(form, 6, "主题", "system", "light", "dark");
        _accentBox = AddComboRow(form, 7, "强调色", "system", "teal", "blue");
        formCard.Child = form;
        root.Children.Add(formCard);

        var actionCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"]
        };
        var actionWrap = new WrapPanel();
        var loadBtn = new Button { Content = "重新加载", Width = 120, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        loadBtn.Click += async (_, _) => await LoadSettingsAsync();
        var saveBtn = new Button { Content = "保存设置", Width = 120, Height = 38, Margin = new Thickness(0, 0, 10, 0) };
        saveBtn.Click += async (_, _) => await SaveSettingsAsync();
        var resetBtn = new Button { Content = "恢复默认", Width = 120, Height = 38 };
        resetBtn.Click += (_, _) => Fill(new SettingsModel());
        actionWrap.Children.Add(loadBtn);
        actionWrap.Children.Add(saveBtn);
        actionWrap.Children.Add(resetBtn);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Text = "待加载"
        };

        actionCard.Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "操作", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                actionWrap,
                _statusText
            }
        };
        root.Children.Add(actionCard);

        scroll.Content = root;
        Content = scroll;
        Loaded += async (_, _) => await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            await _bootstrap.EnsureDefaultsAsync();
            var model = await _settingsStoreService.LoadAsync(_bootstrap.SettingsPath);
            Fill(model);
            _statusText.Text = $"已加载：{_bootstrap.SettingsPath}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"加载设置: {_bootstrap.SettingsPath}");
        }
        catch (Exception ex)
        {
            _statusText.Text = $"加载失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var model = Read();
            await _settingsStoreService.SaveAsync(_bootstrap.SettingsPath, model);
            _statusText.Text = $"保存成功：{_bootstrap.SettingsPath}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"保存设置: {_bootstrap.SettingsPath}");
            MessageBox.Show("设置已保存。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _statusText.Text = $"保存失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Fill(SettingsModel model)
    {
        _installRootBox.Text = model.InstallRoot;
        _proxyModeBox.SelectedItem = model.Proxy.Mode;
        _proxyAddressBox.Text = model.Proxy.Address ?? string.Empty;
        _concurrencyBox.Text = model.Download.Concurrency.ToString();
        _retriesBox.Text = model.Download.Retries.ToString();
        _keepVersionsBox.Text = model.Cache.KeepVersions.ToString();
        _themeBox.SelectedItem = model.Ui.Theme;
        _accentBox.SelectedItem = model.Ui.Accent;
        _cuda = model.Cuda ?? new CudaModel();
    }

    private SettingsModel Read()
    {
        return new SettingsModel
        {
            InstallRoot = string.IsNullOrWhiteSpace(_installRootBox.Text) ? "." : _installRootBox.Text.Trim(),
            Proxy = new ProxyModel
            {
                Mode = (_proxyModeBox.SelectedItem as string) ?? "system",
                Address = string.IsNullOrWhiteSpace(_proxyAddressBox.Text) ? null : _proxyAddressBox.Text.Trim()
            },
            Download = new DownloadModel
            {
                Concurrency = ParsePositive(_concurrencyBox.Text, 3),
                Retries = ParsePositive(_retriesBox.Text, 3)
            },
            Cache = new CacheModel
            {
                KeepVersions = ParsePositive(_keepVersionsBox.Text, 2)
            },
            Cuda = _cuda,
            Ui = new UiModel
            {
                Theme = (_themeBox.SelectedItem as string) ?? "system",
                Accent = (_accentBox.SelectedItem as string) ?? "system"
            }
        };
    }

    private static int ParsePositive(string raw, int fallback)
    {
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static TextBox AddTextRow(Grid grid, int row, string label)
    {
        var labelText = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };
        Grid.SetColumn(labelText, 0);
        Grid.SetRow(labelText, row);
        grid.Children.Add(labelText);

        var textBox = new TextBox { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetColumn(textBox, 1);
        Grid.SetRow(textBox, row);
        grid.Children.Add(textBox);
        return textBox;
    }

    private static ComboBox AddComboRow(Grid grid, int row, string label, params string[] options)
    {
        var labelText = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };
        Grid.SetColumn(labelText, 0);
        Grid.SetRow(labelText, row);
        grid.Children.Add(labelText);

        var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 6), ItemsSource = options };
        Grid.SetColumn(combo, 1);
        Grid.SetRow(combo, row);
        grid.Children.Add(combo);
        return combo;
    }
}
