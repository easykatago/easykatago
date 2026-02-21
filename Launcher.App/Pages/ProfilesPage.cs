using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.Pages;

public sealed class ProfilesPage : Page
{
    private readonly BootstrapService _bootstrap;
    private readonly IProfileService _profileService;
    private readonly ListBox _profilesList;
    private readonly TextBlock _defaultBadge;
    private readonly TextBlock _profileName;
    private readonly TextBlock _profileInfo;
    private readonly TextBlock _commandText;
    private readonly TextBlock _statusText;
    private readonly TextBox _tuningLogBox;
    private readonly TextBlock _recommendedThreadsText;
    private readonly TextBox _manualThreadsInput;
    private ProfilesDocument _profiles = new();
    private int? _recommendedThreads;

    public ProfilesPage()
    {
        _bootstrap = new BootstrapService(AppContext.BaseDirectory);
        _profileService = new JsonProfileService(_bootstrap.ProfilesPath);
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
                    new TextBlock { Text = "配置档案", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock
                    {
                        Text = "管理配置档案：设为默认、复制引擎命令、删除档案与调优线程。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray)
                    }
                }
            }
        });

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        main.ColumnDefinitions.Add(new ColumnDefinition());

        var listCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 12, 0)
        };
        var listPanel = new StackPanel();
        listPanel.Children.Add(new TextBlock { Text = "档案列表", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        _profilesList = new ListBox { MinHeight = 260 };
        _profilesList.SelectionChanged += (_, _) => UpdateSelectedView();
        listPanel.Children.Add(_profilesList);

        var reloadBtn = new Button { Content = "刷新", Width = 100, Height = 34, Margin = new Thickness(0, 10, 0, 0) };
        reloadBtn.Click += async (_, _) => await LoadProfilesAsync();
        listPanel.Children.Add(reloadBtn);

        listCard.Child = listPanel;
        Grid.SetColumn(listCard, 0);
        main.Children.Add(listCard);

        var detailCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"]
        };
        var detailPanel = new StackPanel();

        var titleWrap = new DockPanel();
        _profileName = new TextBlock { Text = "未选择档案", FontSize = 20, FontWeight = FontWeights.SemiBold };
        _defaultBadge = new TextBlock
        {
            Text = string.Empty,
            Foreground = AppBrush("Brush.Accent", Brushes.SteelBlue),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 0, 0)
        };
        titleWrap.Children.Add(_profileName);
        titleWrap.Children.Add(_defaultBadge);
        detailPanel.Children.Add(titleWrap);

        _profileInfo = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray)
        };
        detailPanel.Children.Add(_profileInfo);

        detailPanel.Children.Add(new TextBlock { Text = "引擎命令（绝对路径）", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _commandText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Background = AppBrush("Brush.PanelAlt", Brushes.WhiteSmoke),
            Padding = new Thickness(10),
            MinHeight = 64
        };
        detailPanel.Children.Add(_commandText);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var setDefaultBtn = CreateActionButton("设为默认", new Thickness(0, 0, 8, 8));
        setDefaultBtn.Click += async (_, _) => await SetDefaultAsync();

        var copyBtn = CreateActionButton("复制命令", new Thickness(0, 0, 8, 8));
        copyBtn.Click += (_, _) => CopyCommand();

        var deleteBtn = CreateActionButton("删除", new Thickness(0, 0, 0, 8));
        deleteBtn.Background = AppBrush("Brush.PanelAlt", Brushes.WhiteSmoke);
        deleteBtn.Foreground = AppBrush("Brush.TextPrimary", Brushes.Black);
        deleteBtn.Click += async (_, _) => await DeleteSelectedAsync();

        var benchmarkBtn = CreateActionButton("基准测试", new Thickness(0, 0, 8, 8));
        benchmarkBtn.Click += async (_, _) => await RunBenchmarkAsync();

        var applyThreadsBtn = CreateActionButton("写回推荐线程", new Thickness(0, 0, 8, 8));
        applyThreadsBtn.Click += async (_, _) => await ApplyThreadsAsync();

        var tunerBtn = CreateActionButton("调优", new Thickness(0, 0, 0, 8));
        tunerBtn.Click += async (_, _) => await RunTunerAsync();

        _manualThreadsInput = new TextBox
        {
            Height = 38,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "输入线程数（例如 8 / 12 / 16）"
        };

        var manualThreadsBtn = CreateActionButton("手动写回线程", new Thickness(0, 0, 0, 0));
        manualThreadsBtn.Click += async (_, _) => await ApplyManualThreadsAsync();

        Grid.SetRow(setDefaultBtn, 0);
        Grid.SetColumn(setDefaultBtn, 0);
        Grid.SetRow(copyBtn, 0);
        Grid.SetColumn(copyBtn, 1);
        Grid.SetRow(deleteBtn, 0);
        Grid.SetColumn(deleteBtn, 2);

        Grid.SetRow(benchmarkBtn, 1);
        Grid.SetColumn(benchmarkBtn, 0);
        Grid.SetRow(applyThreadsBtn, 1);
        Grid.SetColumn(applyThreadsBtn, 1);
        Grid.SetRow(tunerBtn, 1);
        Grid.SetColumn(tunerBtn, 2);

        Grid.SetRow(_manualThreadsInput, 2);
        Grid.SetColumn(_manualThreadsInput, 0);
        Grid.SetColumnSpan(_manualThreadsInput, 2);
        Grid.SetRow(manualThreadsBtn, 2);
        Grid.SetColumn(manualThreadsBtn, 2);

        actions.Children.Add(setDefaultBtn);
        actions.Children.Add(copyBtn);
        actions.Children.Add(deleteBtn);
        actions.Children.Add(benchmarkBtn);
        actions.Children.Add(applyThreadsBtn);
        actions.Children.Add(tunerBtn);
        actions.Children.Add(_manualThreadsInput);
        actions.Children.Add(manualThreadsBtn);
        detailPanel.Children.Add(actions);

        _recommendedThreadsText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = AppBrush("Brush.Accent", Brushes.SteelBlue),
            FontWeight = FontWeights.SemiBold,
            Text = "推荐线程：-"
        };
        detailPanel.Children.Add(_recommendedThreadsText);

        detailPanel.Children.Add(new TextBlock { Text = "调优输出", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
        _tuningLogBox = new TextBox
        {
            IsReadOnly = true,
            Height = 180,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        detailPanel.Children.Add(_tuningLogBox);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = AppBrush("Brush.TextSecondary", Brushes.Gray)
        };
        detailPanel.Children.Add(_statusText);

        detailCard.Child = detailPanel;
        Grid.SetColumn(detailCard, 1);
        main.Children.Add(detailCard);

        root.Children.Add(main);
        scroll.Content = root;
        Content = scroll;
        Loaded += async (_, _) => await LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            await _bootstrap.EnsureDefaultsAsync();
            _profiles = await _profileService.LoadAsync();

            _profilesList.Items.Clear();
            for (var i = 0; i < _profiles.Profiles.Count; i++)
            {
                var profile = _profiles.Profiles[i];
                _profilesList.Items.Add(new ListBoxItem
                {
                    Content = BuildProfileListTitle(profile, i),
                    Tag = profile.ProfileId
                });
            }

            if (_profilesList.Items.Count > 0)
            {
                var defaultIndex = 0;
                if (!string.IsNullOrWhiteSpace(_profiles.DefaultProfileId))
                {
                    for (var i = 0; i < _profilesList.Items.Count; i++)
                    {
                        if (_profilesList.Items[i] is ListBoxItem item &&
                            string.Equals(item.Tag as string, _profiles.DefaultProfileId, StringComparison.Ordinal))
                        {
                            defaultIndex = i;
                            break;
                        }
                    }
                }

                _profilesList.SelectedIndex = defaultIndex;
            }
            else
            {
                UpdateSelectedView();
            }

            SetStatus($"已加载 {_profiles.Profiles.Count} 个档案。", Brushes.ForestGreen);
            AppLogService.Info($"加载档案完成: {_profiles.Profiles.Count}");
        }
        catch (Exception ex)
        {
            SetStatus($"加载档案失败：{ex.Message}", Brushes.IndianRed);
            AppLogService.Error($"加载档案失败: {ex}");
        }
    }

    private ProfileModel? GetSelected()
    {
        if (_profilesList.SelectedItem is not ListBoxItem item || item.Tag is not string id)
        {
            return null;
        }

        return _profiles.Profiles.FirstOrDefault(p => p.ProfileId == id);
    }

    private async Task SetDefaultAsync()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        _profiles = _profiles with { DefaultProfileId = selected.ProfileId };
        await _profileService.SaveAsync(_profiles);
        SetStatus($"已设为默认档案：{BuildProfileTitleById(selected.ProfileId)}", Brushes.ForestGreen);
        AppLogService.Info($"设为默认档案: {selected.ProfileId}");
        UpdateSelectedView();
    }

    private void CopyCommand()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var command = BuildAbsoluteGtpCommand(selected);
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        Clipboard.SetText(command);
        SetStatus("命令已复制到剪贴板。", Brushes.ForestGreen);
        AppLogService.Info($"已复制命令: {selected.ProfileId}");
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var result = MessageBox.Show($"确认删除档案“{BuildProfileTitleById(selected.ProfileId)}”？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _profiles = _profileService.Remove(_profiles, selected.ProfileId);
        await _profileService.SaveAsync(_profiles);
        SetStatus($"已删除档案：{BuildProfileTitleById(selected.ProfileId)}", Brushes.ForestGreen);
        AppLogService.Warn($"删除档案: {selected.ProfileId}");
        await LoadProfilesAsync();
    }

    private async Task RunBenchmarkAsync()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var katago = ResolvePath(selected.Katago.Path);
        var model = ResolvePath(selected.Network.Path);
        var cfg = ResolvePath(selected.Config.Path);
        if (!File.Exists(katago) || !File.Exists(model) || !File.Exists(cfg))
        {
            SetStatus("基准测试失败：缺少 katago/model/config 文件。", Brushes.IndianRed);
            return;
        }

        _tuningLogBox.Clear();
        _recommendedThreads = null;
        _recommendedThreadsText.Text = "推荐线程：解析中...";
        _manualThreadsInput.Text = string.Empty;

        var svc = new ProcessTuningService();
        var result = await svc.RunBenchmarkAsync(katago, model, cfg, new Progress<string>(line =>
        {
            _tuningLogBox.AppendText(line + Environment.NewLine);
            _tuningLogBox.ScrollToEnd();
        }));

        _recommendedThreads = result.RecommendedThreads;
        var display = _recommendedThreads.HasValue ? _recommendedThreads.Value.ToString() : "未识别";
        _recommendedThreadsText.Text = $"推荐线程：{display}";
        _manualThreadsInput.Text = _recommendedThreads?.ToString() ?? string.Empty;

        SetStatus(
            result.Success ? "基准测试完成。" : $"基准测试失败：{result.ErrorMessage}",
            result.Success ? Brushes.ForestGreen : Brushes.IndianRed);
        AppLogService.Info($"基准测试结果 success={result.Success}, threads={_recommendedThreads}");

        if (result.RecommendedThreads.HasValue)
        {
            await SaveTuningAsync(selected, selected.Tuning with
            {
                Status = "已基准测试",
                LastBenchmarkAt = DateTimeOffset.Now,
                RecommendedThreads = result.RecommendedThreads
            });
        }
    }

    private async Task ApplyThreadsAsync()
    {
        if (!_recommendedThreads.HasValue)
        {
            SetStatus("请先运行基准测试获取推荐线程。", Brushes.IndianRed);
            return;
        }

        await ApplyThreadsValueAsync(_recommendedThreads.Value, "推荐值");
    }

    private async Task ApplyManualThreadsAsync()
    {
        var input = (_manualThreadsInput.Text ?? string.Empty).Trim();
        if (!int.TryParse(input, out var threads) || threads < 1 || threads > 1024)
        {
            SetStatus("请输入 1 到 1024 之间的线程值。", Brushes.IndianRed);
            return;
        }

        _recommendedThreads = threads;
        _recommendedThreadsText.Text = $"推荐线程：{threads}";
        await ApplyThreadsValueAsync(threads, "手动输入");
    }

    private async Task ApplyThreadsValueAsync(int threads, string source)
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var cfg = ResolvePath(selected.Config.Path);
        if (!File.Exists(cfg))
        {
            SetStatus("写回失败：找不到配置文件。", Brushes.IndianRed);
            return;
        }

        var svc = new ProcessTuningService();
        await svc.ApplyRecommendedThreadsAsync(cfg, threads);
        SetStatus($"已写回 numSearchThreads = {threads}（来源：{source}，已创建备份）。", Brushes.ForestGreen);
        AppLogService.Info($"写回线程: {threads}, source={source}");

        var lizziePath = ResolvePath(selected.Lizzieyzy.Path);
        var prefResult = await new LizzieConfigService().TryWriteKataThreadPreferenceAsync(lizziePath, threads, autoLoad: true);
        if (prefResult.Success)
        {
            AppLogService.Info($"同步 Lizzie 线程偏好成功: {threads}; {prefResult.Message}");
        }
        else
        {
            AppLogService.Warn($"同步 Lizzie 线程偏好失败: {prefResult.Message}");
        }

        await SaveTuningAsync(selected, selected.Tuning with
        {
            Status = "已应用",
            RecommendedThreads = threads
        });
    }

    private async Task RunTunerAsync()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            return;
        }

        var katago = ResolvePath(selected.Katago.Path);
        var model = ResolvePath(selected.Network.Path);
        var cfg = ResolvePath(selected.Config.Path);
        if (!File.Exists(katago) || !File.Exists(model) || !File.Exists(cfg))
        {
            SetStatus("调优失败：缺少 katago/model/config 文件。", Brushes.IndianRed);
            return;
        }

        _tuningLogBox.Clear();
        var svc = new ProcessTuningService();
        var result = await svc.RunTunerAsync(katago, model, cfg, new Progress<string>(line =>
        {
            _tuningLogBox.AppendText(line + Environment.NewLine);
            _tuningLogBox.ScrollToEnd();
        }));

        SetStatus(
            result.Success ? "调优完成。" : $"调优失败：{result.ErrorMessage}",
            result.Success ? Brushes.ForestGreen : Brushes.IndianRed);
        AppLogService.Info($"Tuner result success={result.Success}");
    }

    private void UpdateSelectedView()
    {
        var selected = GetSelected();
        if (selected is null)
        {
            _profileName.Text = "未选择档案";
            _defaultBadge.Text = string.Empty;
            _profileInfo.Text = "请选择一个档案。";
            _commandText.Text = string.Empty;
            _recommendedThreads = null;
            _recommendedThreadsText.Text = "推荐线程：-";
            _manualThreadsInput.Text = string.Empty;
            return;
        }

        _profileName.Text = BuildProfileTitleById(selected.ProfileId);
        _defaultBadge.Text = _profiles.DefaultProfileId == selected.ProfileId ? "默认" : string.Empty;
        _profileInfo.Text = string.Join(
            Environment.NewLine,
            $"ID: {selected.ProfileId}",
            $"原名称: {selected.DisplayName}",
            $"KataGo: {selected.Katago.Version} ({selected.Katago.Backend})",
            $"网络: {selected.Network.Name}",
            $"配置: {selected.Config.Id}",
            $"调优: {selected.Tuning.Status}");
        _commandText.Text = BuildAbsoluteGtpCommand(selected);
        _recommendedThreads = selected.Tuning.RecommendedThreads;
        _recommendedThreadsText.Text = $"推荐线程：{(_recommendedThreads.HasValue ? _recommendedThreads.Value.ToString() : "-")}";
        _manualThreadsInput.Text = _recommendedThreads?.ToString() ?? string.Empty;
    }

    private async Task SaveTuningAsync(ProfileModel selected, TuningProfile tuning)
    {
        try
        {
            var updated = selected with
            {
                Tuning = tuning,
                UpdatedAt = DateTimeOffset.Now
            };
            _profiles = _profileService.Upsert(_profiles, updated, setAsDefault: false);
            await _profileService.SaveAsync(_profiles);
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"保存调优状态失败: {ex.Message}");
        }
    }

    private string BuildAbsoluteGtpCommand(ProfileModel selected)
    {
        var katago = ResolvePath(selected.Katago.Path);
        var model = ResolvePath(selected.Network.Path);
        var cfg = ResolvePath(selected.Config.Path);
        return new DefaultCommandBuilder().BuildGtpCommand(katago, model, cfg);
    }

    private string BuildProfileTitleById(string profileId)
    {
        for (var i = 0; i < _profiles.Profiles.Count; i++)
        {
            if (string.Equals(_profiles.Profiles[i].ProfileId, profileId, StringComparison.Ordinal))
            {
                return BuildProfileListTitle(_profiles.Profiles[i], i);
            }
        }

        return "档案";
    }

    private static string BuildProfileListTitle(ProfileModel profile, int index)
    {
        var seq = index + 1;
        var seqText = seq < 10 ? $"档案 0{seq}" : $"档案 {seq}";
        var backend = (profile.Katago.Backend ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(backend))
        {
            return seqText;
        }

        return $"KataGo({backend}) + {seqText}";
    }

    private void SetStatus(string text, Brush color)
    {
        _statusText.Text = text;
        _statusText.Foreground = color;
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

    private static string ResolvePath(string maybeRelative)
    {
        return Path.IsPathRooted(maybeRelative)
            ? maybeRelative
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, maybeRelative));
    }
}
