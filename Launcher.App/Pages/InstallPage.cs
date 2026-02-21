using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.Win32;

namespace Launcher.App.Pages;

public sealed class InstallPage : Page
{
    private readonly Action<string> _navigate;
    private readonly SettingsStoreService _settingsStoreService = new();
    private readonly BootstrapService _bootstrapService;
    private readonly TextBox _logBox;
    private readonly Button _btnInstall;
    private readonly Button _btnDownloadInstall;
    private readonly Dictionary<string, BackendCardUi> _backendCards = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedBackend = "opencl";
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _stepOneStatus;
    private readonly TextBlock _stepTwoStatus;
    private readonly TextBlock _resultStatus;
    private Border? _cudaRuntimePanel;
    private TextBox? _cudaDirectoryBox;
    private TextBox? _cudnnDirectoryBox;
    private TextBlock? _cudaRuntimeStatus;
    private string _installRoot = AppContext.BaseDirectory;

    public InstallPage(Action<string> navigate)
    {
        _navigate = navigate;
        _bootstrapService = new BootstrapService(AppContext.BaseDirectory);
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
                    new TextBlock { Text = "安装向导", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) },
                    new TextBlock
                    {
                        Text = "执行默认初始化，或按所选后端下载并安装 KataGo 组件。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        var progressCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };
        var progressPanel = new StackPanel();
        progressPanel.Children.Add(new TextBlock { Text = "执行进度", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _progressBar = new ProgressBar
        {
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        progressPanel.Children.Add(_progressBar);
        _resultStatus = new TextBlock
        {
            Text = "待执行",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };
        progressPanel.Children.Add(_resultStatus);
        progressCard.Child = progressPanel;
        root.Children.Add(progressCard);

        var stepsGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        stepsGrid.Children.Add(BuildStepCard("01 准备基础目录", "创建 data/logs/cache 并写入默认 json 文件。", out _stepOneStatus, 0));
        stepsGrid.Children.Add(BuildStepCard("02 生成默认 Profile", "基于 manifest defaults 写入 p_default。", out _stepTwoStatus, 1));
        root.Children.Add(stepsGrid);

        var actionCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };

        var actionPanel = new StackPanel();
        actionPanel.Children.Add(new TextBlock { Text = "执行动作", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        actionPanel.Children.Add(BuildBackendSelectorCard());

        var actionWrap = new WrapPanel();
        _btnInstall = new Button
        {
            Content = "开始初始化",
            Width = 150,
            Height = 38,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _btnInstall.Click += InstallButton_OnClick;
        _btnDownloadInstall = new Button
        {
            Content = "下载并安装默认组件",
            Width = 180,
            Height = 38,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _btnDownloadInstall.Click += DownloadInstallButton_OnClick;
        var homeBtn = new Button
        {
            Content = "返回首页",
            Width = 130,
            Height = 38
        };
        homeBtn.Click += (_, _) => _navigate("Home");
        actionWrap.Children.Add(_btnInstall);
        actionWrap.Children.Add(_btnDownloadInstall);
        actionWrap.Children.Add(homeBtn);
        actionPanel.Children.Add(actionWrap);
        actionCard.Child = actionPanel;
        root.Children.Add(actionCard);

        root.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "实时日志", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) },
                    (_logBox = new TextBox
                    {
                        IsReadOnly = true,
                        Height = 250,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    })
                }
            }
        });

        scroll.Content = root;
        Content = scroll;
        ResetSteps();
        Loaded += InstallPage_Loaded;
    }

    private async void InstallPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCudaRuntimeHintsAsync();
    }

    private Border BuildBackendSelectorCard()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "KataGo 后端",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Brush.TextPrimary"]
        });
        root.Children.Add(new TextBlock
        {
            Text = "请选择适合你机器的后端。建议先用 OpenCL，再按显卡能力切换到 CUDA / TensorRT。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Margin = new Thickness(0, 4, 0, 10)
        });

        var optionsGrid = new Grid();
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        optionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        optionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddBackendCard(optionsGrid, 0, 0, "opencl", "OpenCL", "兼容性最好，推荐先用", "推荐");
        AddBackendCard(optionsGrid, 0, 1, "cuda", "CUDA", "NVIDIA 显卡优先", "GPU");
        AddBackendCard(optionsGrid, 1, 0, "tensorrt", "TensorRT", "NVIDIA 推理加速，部署略复杂", "GPU");
        AddBackendCard(optionsGrid, 1, 1, "eigen", "Eigen", "纯 CPU 后端，稳定但速度较慢", "CPU");
        root.Children.Add(optionsGrid);
        root.Children.Add(BuildCudaRuntimePanel());
        ApplyBackendCardStyles();

        return new Border
        {
            Background = (Brush)Application.Current.Resources["Brush.PanelAlt"],
            BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = root
        };
    }

    private Border BuildCudaRuntimePanel()
    {
        _cudaDirectoryBox = new TextBox
        {
            MinWidth = 400,
            Margin = new Thickness(0, 6, 8, 0),
            ToolTip = "可填写 CUDA 安装目录或 bin 目录。"
        };
        _cudnnDirectoryBox = new TextBox
        {
            MinWidth = 400,
            Margin = new Thickness(0, 6, 8, 0),
            ToolTip = "可填写 cuDNN 安装目录或 bin 目录。"
        };
        _cudaRuntimeStatus = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = "可选：手动指定目录可提高 CUDA 后端命中率。",
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };

        var browseCudaButton = new Button
        {
            Content = "选择 CUDA 目录",
            Width = 130,
            Height = 32,
            Margin = new Thickness(0, 6, 0, 0)
        };
        browseCudaButton.Click += async (_, _) =>
        {
            if (_cudaDirectoryBox is null)
            {
                return;
            }

            var selected = PickDirectory(_cudaDirectoryBox.Text, "选择 CUDA 安装目录或 bin 目录");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _cudaDirectoryBox.Text = selected;
            await SaveCudaRuntimeHintsAsync("已保存 CUDA 手动目录。");
        };

        var browseCudnnButton = new Button
        {
            Content = "选择 cuDNN 目录",
            Width = 130,
            Height = 32,
            Margin = new Thickness(0, 6, 0, 0)
        };
        browseCudnnButton.Click += async (_, _) =>
        {
            if (_cudnnDirectoryBox is null)
            {
                return;
            }

            var selected = PickDirectory(_cudnnDirectoryBox.Text, "选择 cuDNN 安装目录或 bin 目录");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _cudnnDirectoryBox.Text = selected;
            await SaveCudaRuntimeHintsAsync("已保存 cuDNN 手动目录。");
        };

        var detectButton = new Button
        {
            Content = "自动检测本机环境",
            Width = 150,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0)
        };
        detectButton.Click += async (_, _) => await AutoDetectCudaRuntimeAsync();

        var saveButton = new Button
        {
            Content = "保存目录",
            Width = 110,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0)
        };
        saveButton.Click += async (_, _) => await SaveCudaRuntimeHintsAsync("已保存 CUDA/cuDNN 手动目录。");

        var clearButton = new Button
        {
            Content = "清空目录",
            Width = 100,
            Height = 34
        };
        clearButton.Click += async (_, _) =>
        {
            if (_cudaDirectoryBox is not null)
            {
                _cudaDirectoryBox.Text = string.Empty;
            }

            if (_cudnnDirectoryBox is not null)
            {
                _cudnnDirectoryBox.Text = string.Empty;
            }

            await SaveCudaRuntimeHintsAsync("已清空 CUDA/cuDNN 手动目录。");
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "CUDA / cuDNN 运行库目录（可选）",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Brush.TextPrimary"],
            Margin = new Thickness(0, 14, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "用于辅助一键启动时的运行库定位。如果系统 PATH 不完整，建议在这里指定目录。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Margin = new Thickness(0, 4, 0, 0)
        });
        panel.Children.Add(BuildRuntimePathRow("CUDA 目录", _cudaDirectoryBox, browseCudaButton));
        panel.Children.Add(BuildRuntimePathRow("cuDNN 目录", _cudnnDirectoryBox, browseCudnnButton));
        panel.Children.Add(new WrapPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                detectButton,
                saveButton,
                clearButton
            }
        });
        panel.Children.Add(_cudaRuntimeStatus);

        _cudaRuntimePanel = new Border
        {
            Background = (Brush)Application.Current.Resources["Brush.Panel"],
            BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 10, 0, 0),
            Child = panel
        };
        return _cudaRuntimePanel;
    }

    private static Grid BuildRuntimePathRow(string label, TextBox valueTextBox, Button browseButton)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Margin = new Thickness(0, 2, 0, 0);

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Margin = new Thickness(0, 6, 8, 0)
        };
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        Grid.SetColumn(valueTextBox, 1);
        row.Children.Add(valueTextBox);

        Grid.SetColumn(browseButton, 2);
        row.Children.Add(browseButton);
        return row;
    }

    private static string? PickDirectory(string? initialPath, string description)
    {
        var dialog = new OpenFileDialog
        {
            Title = description,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "选择此文件夹"
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            try
            {
                var full = Path.GetFullPath(initialPath);
                dialog.InitialDirectory = Directory.Exists(full)
                    ? full
                    : Path.GetDirectoryName(full);
            }
            catch
            {
                // ignore invalid user input
            }
        }

        var result = dialog.ShowDialog();
        if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return null;
        }

        var directory = Directory.Exists(dialog.FileName)
            ? dialog.FileName
            : Path.GetDirectoryName(dialog.FileName);
        return string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
    }

    private async Task LoadCudaRuntimeHintsAsync()
    {
        if (_cudaDirectoryBox is null || _cudnnDirectoryBox is null)
        {
            return;
        }

        try
        {
            await _bootstrapService.EnsureDefaultsAsync();
            var settings = await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath);
            _installRoot = ResolveInstallRoot(settings.InstallRoot);
            _cudaDirectoryBox.Text = settings.Cuda?.ManualCudaDirectory ?? string.Empty;
            _cudnnDirectoryBox.Text = settings.Cuda?.ManualCudnnDirectory ?? string.Empty;
            SetCudaRuntimeStatus("可选：手动指定目录可提高 CUDA 后端命中率。", (Brush)Application.Current.Resources["Brush.TextSecondary"]);
            RefreshCudaRuntimePanelVisibility();
        }
        catch (Exception ex)
        {
            SetCudaRuntimeStatus($"加载 CUDA 目录设置失败：{ex.Message}", Brushes.IndianRed);
            AppLogService.Warn($"加载 CUDA 目录设置失败: {ex.Message}");
        }
    }

    private async Task SaveCudaRuntimeHintsAsync(string successMessage)
    {
        if (_cudaDirectoryBox is null || _cudnnDirectoryBox is null)
        {
            return;
        }

        try
        {
            await _bootstrapService.EnsureDefaultsAsync();
            var settings = await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath);
            var manualCudaDir = NormalizeDirectoryInput(_cudaDirectoryBox.Text);
            var manualCudnnDir = NormalizeDirectoryInput(_cudnnDirectoryBox.Text);

            var cudaOk = ValidateDirectoryInput(manualCudaDir, out var cudaError);
            var cudnnOk = ValidateDirectoryInput(manualCudnnDir, out var cudnnError);
            if (!cudaOk || !cudnnOk)
            {
                SetCudaRuntimeStatus(cudaError ?? cudnnError ?? "目录校验失败。", Brushes.IndianRed);
                return;
            }

            settings = settings with
            {
                Cuda = (settings.Cuda ?? new CudaModel()) with
                {
                    ManualCudaDirectory = manualCudaDir,
                    ManualCudnnDirectory = manualCudnnDir
                }
            };
            await _settingsStoreService.SaveAsync(_bootstrapService.SettingsPath, settings);
            _installRoot = ResolveInstallRoot(settings.InstallRoot);
            SetCudaRuntimeStatus(successMessage, Brushes.ForestGreen);
            AppLogService.Info($"保存 CUDA 手动目录: cuda={manualCudaDir ?? "(empty)"}, cudnn={manualCudnnDir ?? "(empty)"}");
        }
        catch (Exception ex)
        {
            SetCudaRuntimeStatus($"保存 CUDA 目录设置失败：{ex.Message}", Brushes.IndianRed);
            AppLogService.Warn($"保存 CUDA 目录设置失败: {ex.Message}");
        }
    }

    private async Task AutoDetectCudaRuntimeAsync()
    {
        try
        {
            await _bootstrapService.EnsureDefaultsAsync();
            var settings = await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath);
            _installRoot = ResolveInstallRoot(settings.InstallRoot);

            var probe = CudaRuntimeService.Probe(BuildCudaProbeKatagoPath(), _installRoot, AppContext.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(probe.CudaDirectory) && _cudaDirectoryBox is not null)
            {
                _cudaDirectoryBox.Text = probe.CudaDirectory;
            }

            if (!string.IsNullOrWhiteSpace(probe.CudnnDirectory) && _cudnnDirectoryBox is not null)
            {
                _cudnnDirectoryBox.Text = probe.CudnnDirectory;
            }

            await SaveCudaRuntimeHintsAsync(
                probe.IsReady
                    ? "自动检测成功，已回填并保存 CUDA/cuDNN 目录。"
                    : "自动检测完成，未完全命中运行库，请手动补充目录。");

            if (!probe.IsReady)
            {
                SetCudaRuntimeStatus($"自动检测完成：{probe.Summary}", Brushes.DarkGoldenrod);
            }

            Log($"CUDA 运行库检测: ready={probe.IsReady}; cuda={probe.CudaDirectory ?? "(none)"}; cudnn={probe.CudnnDirectory ?? "(none)"}");
        }
        catch (Exception ex)
        {
            SetCudaRuntimeStatus($"自动检测失败：{ex.Message}", Brushes.IndianRed);
            AppLogService.Warn($"自动检测 CUDA 运行库失败: {ex.Message}");
        }
    }

    private string BuildCudaProbeKatagoPath()
    {
        return Path.Combine(_installRoot, "components", "katago", "placeholder", "cuda", "katago.exe");
    }

    private static string ResolveInstallRoot(string? installRootValue)
    {
        var target = string.IsNullOrWhiteSpace(installRootValue) ? "." : installRootValue.Trim();
        return Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, target));
    }

    private static bool ValidateDirectoryInput(string? path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = null;
            return true;
        }

        if (!Directory.Exists(path))
        {
            error = $"目录不存在：{path}";
            return false;
        }

        error = null;
        return true;
    }

    private static string? NormalizeDirectoryInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(raw.Trim());
        }
        catch
        {
            return raw.Trim();
        }
    }

    private void SetCudaRuntimeStatus(string text, Brush color)
    {
        if (_cudaRuntimeStatus is null)
        {
            return;
        }

        _cudaRuntimeStatus.Text = text;
        _cudaRuntimeStatus.Foreground = color;
    }

    private void RefreshCudaRuntimePanelVisibility()
    {
        if (_cudaRuntimePanel is null)
        {
            return;
        }

        _cudaRuntimePanel.Visibility = IsCudaBackend(_selectedBackend)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddBackendCard(
        Grid host,
        int row,
        int col,
        string key,
        string title,
        string description,
        string tag)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        };
        var descText = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12
        };
        var tagText = new TextBlock
        {
            Text = tag,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        var tagBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2, 8, 2),
            Child = tagText
        };

        var top = new DockPanel();
        DockPanel.SetDock(tagBorder, Dock.Right);
        top.Children.Add(tagBorder);
        top.Children.Add(titleText);

        var cardBody = new StackPanel();
        cardBody.Children.Add(top);
        cardBody.Children.Add(descText);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 6, col == 0 ? 6 : 0, row == 0 ? 6 : 0),
            Cursor = Cursors.Hand,
            Child = cardBody,
            Tag = key
        };
        card.MouseLeftButtonUp += (_, _) => SelectBackend(key);
        card.MouseEnter += (_, _) =>
        {
            if (!string.Equals(_selectedBackend, key, StringComparison.OrdinalIgnoreCase))
            {
                card.Background = (Brush)Application.Current.Resources["Brush.PanelAlt"];
            }
        };
        card.MouseLeave += (_, _) =>
        {
            if (!string.Equals(_selectedBackend, key, StringComparison.OrdinalIgnoreCase))
            {
                card.Background = (Brush)Application.Current.Resources["Brush.Panel"];
            }
        };

        Grid.SetRow(card, row);
        Grid.SetColumn(card, col);
        host.Children.Add(card);

        _backendCards[key] = new BackendCardUi(card, titleText, descText, tagBorder, tagText, tag);
    }

    private void SelectBackend(string key)
    {
        if (!_backendCards.ContainsKey(key))
        {
            return;
        }

        _selectedBackend = key;
        ApplyBackendCardStyles();
    }

    private void ApplyBackendCardStyles()
    {
        var accent = (Brush)Application.Current.Resources["Brush.Accent"];
        var accentSoft = (Brush)Application.Current.Resources["Brush.AccentSoft"];
        var panel = (Brush)Application.Current.Resources["Brush.Panel"];
        var border = (Brush)Application.Current.Resources["Brush.Border"];
        var textPrimary = (Brush)Application.Current.Resources["Brush.TextPrimary"];
        var textSecondary = (Brush)Application.Current.Resources["Brush.TextSecondary"];

        foreach (var (key, ui) in _backendCards)
        {
            var selected = string.Equals(key, _selectedBackend, StringComparison.OrdinalIgnoreCase);
            ui.Card.Background = selected ? accentSoft : panel;
            ui.Card.BorderBrush = selected ? accent : border;
            ui.Card.BorderThickness = selected ? new Thickness(1.5) : new Thickness(1);
            ui.Title.Foreground = selected ? accent : textPrimary;
            ui.Description.Foreground = selected ? accent : textSecondary;
            ui.TagBorder.Background = selected ? accent : accentSoft;
            ui.TagText.Foreground = selected ? Brushes.White : accent;
            ui.TagText.Text = selected ? "已选" : ui.DefaultTag;
        }

        RefreshCudaRuntimePanelVisibility();
    }

    private async void InstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        _btnInstall.IsEnabled = false;
        _btnDownloadInstall.IsEnabled = false;
        ResetSteps();
        _progressBar.Value = 10;
        _resultStatus.Text = "执行中...";

        try
        {
            Log("开始初始化默认安装...");
            _stepOneStatus.Text = "进行中";
            _stepOneStatus.Foreground = (Brush)Application.Current.Resources["Brush.Accent"];

            var appRoot = AppContext.BaseDirectory;
            var bootstrap = new BootstrapService(appRoot);
            var workflow = new InstallWorkflowService(bootstrap);
            _progressBar.Value = 45;

            var result = await workflow.InitializeDefaultsAsync();
            _stepOneStatus.Text = "已完成";
            _stepTwoStatus.Text = result.Success ? "已完成" : "失败";
            _stepTwoStatus.Foreground = result.Success ? Brushes.ForestGreen : Brushes.IndianRed;
            _progressBar.Value = result.Success ? 100 : 70;

            Log($"数据目录: {result.DataRoot}");
            Log($"安装根目录: {result.InstallRoot}");
            Log($"profiles: {result.ProfilesPath}");
            Log($"settings: {result.SettingsPath}");
            Log($"manifest 快照: {result.ManifestSnapshotPath}");
            Log(result.Message);

            _resultStatus.Text = result.Success ? "初始化完成。可返回首页查看状态。" : "初始化未完成，请检查日志。";
            _resultStatus.Foreground = result.Success ? Brushes.ForestGreen : Brushes.IndianRed;

            if (result.Success)
            {
                MessageBox.Show("初始化成功，已生成默认 Profile。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("初始化失败，请查看日志信息。", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _stepOneStatus.Text = "失败";
            _stepOneStatus.Foreground = Brushes.IndianRed;
            _stepTwoStatus.Text = "未执行";
            _progressBar.Value = 40;
            _resultStatus.Text = "初始化失败。";
            _resultStatus.Foreground = Brushes.IndianRed;
            Log($"初始化失败: {ex.Message}");
            MessageBox.Show($"初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _btnInstall.IsEnabled = true;
            _btnDownloadInstall.IsEnabled = true;
        }
    }

    private async void DownloadInstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        var backend = GetSelectedBackendKey();
        if (IsCudaBackend(backend))
        {
            await SaveCudaRuntimeHintsAsync("已保存 CUDA/cuDNN 手动目录。");

            var cudaArchiveUrl = await ResolveCudaArchiveUrlAsync();
            if (!ConfirmCudaInstallNotice(cudaArchiveUrl))
            {
                _resultStatus.Text = "已取消 CUDA 安装任务。";
                _resultStatus.Foreground = Brushes.IndianRed;
                Log("用户关闭了 CUDA 安装告知弹窗，任务已中止。");
                return;
            }
        }

        if (IsTensorRtBackend(backend) && !ConfirmTensorRtInstallNotice())
        {
            _resultStatus.Text = "已取消 TensorRT 安装任务。";
            _resultStatus.Foreground = Brushes.IndianRed;
            Log("用户关闭了 TensorRT 安装告知弹窗，任务已中止。");
            return;
        }

        _btnInstall.IsEnabled = false;
        _btnDownloadInstall.IsEnabled = false;
        ResetSteps();
        _progressBar.Value = 5;
        _resultStatus.Text = "执行中...";
        _resultStatus.Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"];

        try
        {
            var bootstrap = new BootstrapService(AppContext.BaseDirectory);
            var workflow = new InstallWorkflowService(bootstrap);
            Log($"开始下载并安装默认组件... 后端: {backend}");
            _stepOneStatus.Text = "进行中";
            _stepOneStatus.Foreground = (Brush)Application.Current.Resources["Brush.Accent"];

            var progress = new Progress<InstallProgress>(p =>
            {
                var overall = ((p.Current - 1) + p.PercentInStage / 100.0) / p.Total * 100.0;
                _progressBar.Value = Math.Clamp(overall, 0, 100);
                _resultStatus.Text = $"阶段 {p.Current}/{p.Total}: {p.Stage} ({p.PercentInStage:0}%)";
            });

            var result = await workflow.InstallDefaultsAsync(
                msg => Log(msg),
                progress,
                backend);

            _stepOneStatus.Text = "已完成";
            _stepTwoStatus.Text = result.Success ? "已完成" : "失败";
            _stepTwoStatus.Foreground = result.Success ? Brushes.ForestGreen : Brushes.IndianRed;
            _progressBar.Value = result.Success ? 100 : Math.Max(_progressBar.Value, 60);
            _resultStatus.Text = result.Success ? "默认组件安装完成。" : "默认组件安装失败。";
            _resultStatus.Foreground = result.Success ? Brushes.ForestGreen : Brushes.IndianRed;

            Log($"安装根目录: {result.InstallRoot}");
            Log(result.Message);
            if (result.Success)
            {
                MessageBox.Show("默认组件安装完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"安装失败：{result.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _stepOneStatus.Text = "失败";
            _stepOneStatus.Foreground = Brushes.IndianRed;
            _stepTwoStatus.Text = "未执行";
            _progressBar.Value = Math.Max(_progressBar.Value, 40);
            _resultStatus.Text = "安装异常。";
            _resultStatus.Foreground = Brushes.IndianRed;
            Log($"安装异常: {ex.Message}");
            MessageBox.Show($"安装异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _btnInstall.IsEnabled = true;
            _btnDownloadInstall.IsEnabled = true;
        }
    }

    private bool ConfirmTensorRtInstallNotice()
    {
        var owner = Window.GetWindow(this);
        var accent = Application.Current.Resources["Brush.Accent"] as Brush ?? Brushes.SteelBlue;
        var textPrimary = Application.Current.Resources["Brush.TextPrimary"] as Brush ?? Brushes.Black;
        var textSecondary = Application.Current.Resources["Brush.TextSecondary"] as Brush ?? Brushes.DimGray;

        var dialog = new Window
        {
            Title = "TensorRT 安装告知",
            Width = 760,
            Height = 460,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = Brushes.White,
            ShowInTaskbar = false
        };

        var bodyText = string.Join(
            Environment.NewLine,
            "你当前选择了 TensorRT 后端。继续前请确认以下事项：",
            "1) 启动器将自动下载 KataGo TensorRT 构建（例如 trt10.9.0-cuda12.8）。",
            "2) 启动器会尝试自动下载 NVIDIA TensorRT Windows 包并解压到本地 components/tensorrt。",
            "3) 启动器会自动把 TensorRT 目录写入当前进程 PATH，并尝试写入“用户 PATH”。",
            "4) 若 NVIDIA 下载受限（网络/地区/账号授权），任务会报错并停止，请手动下载后重试。",
            string.Empty,
            "建议：",
            "- 显卡驱动与 CUDA 主版本需匹配（示例：CUDA 12.8）。",
            "- 首次安装体积较大，耗时会明显长于 OpenCL/CUDA 包。");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "TensorRT 运行环境下载与配置告知",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = bodyText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = textSecondary,
                FontSize = 14,
                LineHeight = 23
            }
        };
        Grid.SetRow(contentScroll, 1);
        root.Children.Add(contentScroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var acknowledge = new Button
        {
            Content = "我已知悉",
            Width = 120,
            Height = 38,
            Background = accent,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true
        };
        acknowledge.Click += (_, _) => dialog.DialogResult = true;

        var close = new Button
        {
            Content = "关闭",
            Width = 100,
            Height = 38,
            IsCancel = true
        };
        close.Click += (_, _) => dialog.DialogResult = false;

        buttons.Children.Add(acknowledge);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        var accepted = dialog.ShowDialog();
        return accepted == true;
    }

    private bool ConfirmCudaInstallNotice(string cudaArchiveUrl)
    {
        var owner = Window.GetWindow(this);
        var accent = Application.Current.Resources["Brush.Accent"] as Brush ?? Brushes.SteelBlue;
        var textPrimary = Application.Current.Resources["Brush.TextPrimary"] as Brush ?? Brushes.Black;
        var textSecondary = Application.Current.Resources["Brush.TextSecondary"] as Brush ?? Brushes.DimGray;
        var archiveHint = TryExtractCudaArchiveVersionHint(cudaArchiveUrl);
        const string cudnnUrl = "https://developer.nvidia.com/cudnn-downloads";

        var dialog = new Window
        {
            Title = "CUDA 安装告知",
            Width = 920,
            Height = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = Brushes.White,
            ShowInTaskbar = false
        };

        var bodyText = string.Join(
            Environment.NewLine,
            $"你当前选择了 CUDA 后端。预计将使用 CUDA {archiveHint} 对应构建。",
            "继续前请确认以下事项：",
            "1) 启动器会下载 KataGo CUDA 版。",
            "2) CUDA 运行库（cublas/cudnn）通常来自本机 CUDA + cuDNN 安装，不一定在 KataGo 压缩包内。",
            "3) 若缺少 cublas64_12.dll / cudnn64_9.dll，请先安装匹配版本后再启动。",
            string.Empty,
            "你可以点击下方“打开 CUDA 官网”或“打开 cuDNN 官网”完成依赖安装。");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "CUDA 运行环境准备提示",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = bodyText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = textSecondary,
                FontSize = 14,
                LineHeight = 23
            }
        };
        Grid.SetRow(contentScroll, 1);
        root.Children.Add(contentScroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var acknowledge = new Button
        {
            Content = "我已知悉",
            Width = 120,
            Height = 38,
            Background = accent,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true
        };
        acknowledge.Click += (_, _) => dialog.DialogResult = true;

        var openCudaSite = new Button
        {
            Content = "打开 CUDA 官网",
            Width = 150,
            Height = 38,
            Margin = new Thickness(0, 0, 10, 0)
        };
        openCudaSite.Click += (_, _) =>
        {
            OpenExternalUrl(cudaArchiveUrl);
            AppLogService.Info($"打开 CUDA 下载链接: {cudaArchiveUrl}");
        };

        var openCudnnSite = new Button
        {
            Content = "打开 cuDNN 官网",
            Width = 160,
            Height = 38,
            Margin = new Thickness(0, 0, 10, 0)
        };
        openCudnnSite.Click += (_, _) =>
        {
            OpenExternalUrl(cudnnUrl);
            AppLogService.Info($"打开 cuDNN 下载链接: {cudnnUrl}");
        };

        var close = new Button
        {
            Content = "关闭",
            Width = 100,
            Height = 38,
            IsCancel = true
        };
        close.Click += (_, _) => dialog.DialogResult = false;

        buttons.Children.Add(acknowledge);
        buttons.Children.Add(openCudaSite);
        buttons.Children.Add(openCudnnSite);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        var accepted = dialog.ShowDialog();
        return accepted == true;
    }

    private async Task<string> ResolveCudaArchiveUrlAsync()
    {
        try
        {
            var bootstrap = new BootstrapService(AppContext.BaseDirectory);
            var workflow = new InstallWorkflowService(bootstrap);
            var cudaVersion = await workflow.TryResolveCudaVersionAsync();
            if (string.IsNullOrWhiteSpace(cudaVersion))
            {
                return "https://developer.nvidia.com/cuda-downloads";
            }

            var parts = cudaVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return "https://developer.nvidia.com/cuda-downloads";
            }

            return $"https://developer.nvidia.com/cuda-{parts[0]}-{parts[1]}-0-download-archive";
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"解析 CUDA 官网链接失败: {ex.Message}");
            return "https://developer.nvidia.com/cuda-downloads";
        }
    }

    private static string TryExtractCudaArchiveVersionHint(string cudaArchiveUrl)
    {
        var m = Regex.Match(cudaArchiveUrl ?? string.Empty, "cuda-(?<major>\\d+)-(?<minor>\\d+)-0", RegexOptions.IgnoreCase);
        return m.Success ? $"{m.Groups["major"].Value}.{m.Groups["minor"].Value}" : "对应版本";
    }

    private static void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // best effort
        }
    }

    private static bool IsCudaBackend(string backend)
    {
        return string.Equals(backend, "cuda", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTensorRtBackend(string backend)
    {
        return string.Equals(backend, "tensorrt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "trt", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetSteps()
    {
        _stepOneStatus.Text = "待执行";
        _stepOneStatus.Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"];
        _stepTwoStatus.Text = "待执行";
        _stepTwoStatus.Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"];
    }

    private void Log(string line)
    {
        AppLogService.Info(line);
        _logBox.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        _logBox.ScrollToEnd();
    }

    private string GetSelectedBackendKey()
    {
        return _selectedBackend;
    }

    private static Border BuildStepCard(string title, string description, out TextBlock status, int column)
    {
        status = new TextBlock
        {
            Text = "待执行",
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Margin = new Thickness(0, 8, 0, 0)
        };

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,6) },
                    new TextBlock
                    {
                        Text = description,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    },
                    status
                }
            }
        };
        Grid.SetColumn(card, column);
        return card;
    }

    private sealed record BackendCardUi(
        Border Card,
        TextBlock Title,
        TextBlock Description,
        Border TagBorder,
        TextBlock TagText,
        string DefaultTag);
}
