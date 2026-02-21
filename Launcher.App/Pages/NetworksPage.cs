using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Microsoft.Win32;

namespace Launcher.App.Pages;

public sealed class NetworksPage : Page
{
    private const string KataGoNetworksUrl = "https://katagotraining.org/networks/";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly BootstrapService _bootstrap;
    private readonly SettingsStoreService _settingsStoreService = new();
    private readonly LocalManifestService _manifestService;
    private readonly JsonProfileService _profileService;
    private readonly DefaultCommandBuilder _commandBuilder = new();
    private readonly ListBox _networkList;
    private readonly TextBlock _detailText;
    private readonly TextBlock _statusText;
    private readonly ProgressBar _downloadProgressBar;
    private readonly Grid _downloadMetricsGrid;
    private readonly TextBlock _downloadBytesText;
    private readonly TextBlock _downloadSpeedText;
    private readonly TextBlock _downloadFileSizeText;
    private ManifestModel? _manifest;

    public NetworksPage()
    {
        _bootstrap = new BootstrapService(AppContext.BaseDirectory);
        _manifestService = new LocalManifestService(_bootstrap.ManifestSnapshotPath);
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
                    new TextBlock { Text = "权重管理", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) },
                    new TextBlock
                    {
                        Text = "加载 manifest 预置网络，支持应用到默认 Profile，并可添加本地权重文件。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        main.ColumnDefinitions.Add(new ColumnDefinition());

        var listCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 12, 0)
        };
        var listPanel = new StackPanel();
        listPanel.Children.Add(new TextBlock { Text = "网络列表", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _networkList = new ListBox { MinHeight = 260 };
        _networkList.SelectionChanged += (_, _) => UpdateNetworkDetail();
        listPanel.Children.Add(_networkList);

        var listActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var refreshBtn = new Button { Content = "刷新", Width = 80, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        refreshBtn.Click += async (_, _) => await LoadNetworksAsync();
        var deleteBtn = new Button { Content = "删除", Width = 80, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        deleteBtn.Click += async (_, _) => await DeleteSelectedAsync();
        var addBtn = new Button { Content = "添加", Width = 80, Height = 34 };
        addBtn.Click += async (_, _) => await AddWeightFileAsync();
        listActions.Children.Add(refreshBtn);
        listActions.Children.Add(deleteBtn);
        listActions.Children.Add(addBtn);
        listPanel.Children.Add(listActions);

        listCard.Child = listPanel;
        Grid.SetColumn(listCard, 0);
        main.Children.Add(listCard);

        var detailCard = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
        var detailPanel = new StackPanel();
        detailPanel.Children.Add(new TextBlock { Text = "网络详情", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _detailText = new TextBlock
        {
            Text = "请选择网络。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            Margin = new Thickness(0, 0, 0, 10)
        };
        detailPanel.Children.Add(_detailText);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };

        _downloadProgressBar = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };

        _downloadMetricsGrid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _downloadMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _downloadMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _downloadMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bytesPanel = BuildMetricPanel("已下载", out _downloadBytesText);
        Grid.SetColumn(bytesPanel, 0);
        _downloadMetricsGrid.Children.Add(bytesPanel);

        var speedPanel = BuildMetricPanel("速度", out _downloadSpeedText);
        Grid.SetColumn(speedPanel, 1);
        _downloadMetricsGrid.Children.Add(speedPanel);

        var sizePanel = BuildMetricPanel("文件大小", out _downloadFileSizeText);
        Grid.SetColumn(sizePanel, 2);
        _downloadMetricsGrid.Children.Add(sizePanel);

        var actionGrid = new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var applyBtn = new Button { Content = "应用到默认 Profile", Width = 180, Height = 36, Margin = new Thickness(0, 0, 10, 8) };
        applyBtn.Click += async (_, _) => await ApplySelectedAsync();

        var openSiteBtn = new Button { Content = "打开 KataGo 网络页", Width = 180, Height = 36, Margin = new Thickness(0, 0, 0, 8) };
        openSiteBtn.Click += (_, _) =>
        {
            OpenExternalUrl(KataGoNetworksUrl);
            _statusText.Text = $"已打开：{KataGoNetworksUrl}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"打开网络页: {KataGoNetworksUrl}");
        };

        var downloadLatestBtn = new Button { Content = "一键下载最新权重", Width = 180, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
        downloadLatestBtn.Click += async (_, _) => await DownloadNetworkAsync(preferStrongest: false);

        var downloadStrongestBtn = new Button { Content = "一键下载最强权重", Width = 180, Height = 36 };
        downloadStrongestBtn.Click += async (_, _) => await DownloadNetworkAsync(preferStrongest: true);

        var openLocalFolderBtn = new Button
        {
            Content = "打开权重本地文件夹",
            Width = 370,
            Height = 36,
            Margin = new Thickness(0, 8, 0, 0)
        };
        openLocalFolderBtn.Click += (_, _) => OpenSelectedNetworkFolder();

        Grid.SetRow(applyBtn, 0);
        Grid.SetColumn(applyBtn, 0);
        Grid.SetRow(openSiteBtn, 0);
        Grid.SetColumn(openSiteBtn, 1);
        Grid.SetRow(downloadLatestBtn, 1);
        Grid.SetColumn(downloadLatestBtn, 0);
        Grid.SetRow(downloadStrongestBtn, 1);
        Grid.SetColumn(downloadStrongestBtn, 1);
        Grid.SetRow(openLocalFolderBtn, 2);
        Grid.SetColumn(openLocalFolderBtn, 0);
        Grid.SetColumnSpan(openLocalFolderBtn, 2);

        actionGrid.Children.Add(applyBtn);
        actionGrid.Children.Add(openSiteBtn);
        actionGrid.Children.Add(downloadLatestBtn);
        actionGrid.Children.Add(downloadStrongestBtn);
        actionGrid.Children.Add(openLocalFolderBtn);
        detailPanel.Children.Add(actionGrid);
        detailPanel.Children.Add(_downloadProgressBar);
        detailPanel.Children.Add(_downloadMetricsGrid);
        detailPanel.Children.Add(_statusText);

        detailCard.Child = detailPanel;
        Grid.SetColumn(detailCard, 1);
        main.Children.Add(detailCard);

        root.Children.Add(main);
        scroll.Content = root;
        Content = scroll;
        Loaded += async (_, _) => await LoadNetworksAsync();
    }

    private async Task LoadNetworksAsync()
    {
        try
        {
            await _bootstrap.EnsureDefaultsAsync();
            _manifest = await _manifestService.LoadAsync();
            _networkList.Items.Clear();
            foreach (var network in _manifest.Components.Networks)
            {
                TryMigrateLegacyNetworkLayout(network);
                _networkList.Items.Add(new ListBoxItem
                {
                    Content = network.Name,
                    Tag = network.Id
                });
            }

            if (_networkList.Items.Count > 0)
            {
                _networkList.SelectedIndex = 0;
            }

            _statusText.Text = $"已加载 {_networkList.Items.Count} 个网络。";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"加载网络列表: {_networkList.Items.Count}");
        }
        catch (Exception ex)
        {
            _statusText.Text = $"加载失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
        }
    }

    private void UpdateNetworkDetail()
    {
        var selected = GetSelectedNetwork();
        if (selected is null)
        {
            _detailText.Text = "请选择网络。";
            return;
        }

        var url = selected.Urls.FirstOrDefault() ?? "(本地导入)";
        var modelPath = ResolveNetworkRelativePath(selected);
        var absModelPath = Path.Combine(AppContext.BaseDirectory, modelPath.Replace('/', Path.DirectorySeparatorChar));
        var localSizeText = File.Exists(absModelPath)
            ? FormatBytes(new FileInfo(absModelPath).Length)
            : "未下载";
        _detailText.Text =
            $"ID: {selected.Id}\n" +
            $"名称: {selected.Name}\n" +
            $"版本: {selected.Version}\n" +
            $"发布时间: {FormatPublishedAt(selected.PublishedAt)}\n" +
            $"来源: {selected.Source ?? "manifest"}\n" +
            $"文件: {modelPath}\n" +
            $"本地大小: {localSizeText}\n" +
            $"URL: {url}";
    }

    private ComponentModel? GetSelectedNetwork()
    {
        if (_manifest is null)
        {
            return null;
        }

        if (_networkList.SelectedItem is not ListBoxItem item || item.Tag is not string id)
        {
            return null;
        }

        return _manifest.Components.Networks.FirstOrDefault(n => n.Id == id);
    }

    private async Task ApplySelectedAsync()
    {
        var network = GetSelectedNetwork();
        if (network is null)
        {
            return;
        }

        try
        {
            var profiles = await _profileService.LoadAsync();
            var profile = profiles.Profiles.FirstOrDefault(p => p.ProfileId == profiles.DefaultProfileId) ?? profiles.Profiles.FirstOrDefault();
            if (profile is null)
            {
                _statusText.Text = "未找到默认 Profile。请先在安装向导初始化。";
                _statusText.Foreground = Brushes.IndianRed;
                return;
            }

            var modelPath = ResolveNetworkRelativePath(network);
            var updated = profile with
            {
                Network = profile.Network with
                {
                    Id = network.Id,
                    Name = network.Name,
                    Path = modelPath,
                    Source = network.Source ?? "manifest"
                },
                Katago = profile.Katago with
                {
                    GtpArgs = _commandBuilder.BuildGtpCommand(profile.Katago.Path, modelPath, profile.Config.Path)
                },
                UpdatedAt = DateTimeOffset.Now
            };

            var next = profiles with
            {
                Profiles = profiles.Profiles.Select(p => p.ProfileId == updated.ProfileId ? updated : p).ToList()
            };
            await _profileService.SaveAsync(next);

            _statusText.Text = $"已应用网络到默认 Profile：{network.Name}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"应用网络到默认 Profile: {network.Id}");
        }
        catch (Exception ex)
        {
            _statusText.Text = $"应用失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelectedNetwork();
        if (selected is null || _manifest is null)
        {
            _statusText.Text = "请先选择要删除的网络。";
            _statusText.Foreground = Brushes.IndianRed;
            return;
        }

        var confirm = MessageBox.Show(
            $"确认删除网络“{selected.Name}”？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var nextNetworks = _manifest.Components.Networks
            .Where(n => !string.Equals(n.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (nextNetworks.Count == 0)
        {
            _statusText.Text = "至少需要保留一个网络，无法删除最后一个。";
            _statusText.Foreground = Brushes.IndianRed;
            return;
        }

        var nextDefault = _manifest.Defaults.NetworkId;
        if (string.Equals(nextDefault, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            nextDefault = nextNetworks[0].Id;
        }

        _manifest = _manifest with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Components = _manifest.Components with
            {
                Networks = nextNetworks
            },
            Defaults = _manifest.Defaults with
            {
                NetworkId = nextDefault
            }
        };

        await SaveManifestSnapshotAsync(_manifest);

        var modelPath = ResolveNetworkRelativePath(selected);
        var modelAbsPath = Path.Combine(AppContext.BaseDirectory, modelPath.Replace('/', Path.DirectorySeparatorChar));
        var modelFolder = Path.GetDirectoryName(modelAbsPath);
        if (!string.IsNullOrWhiteSpace(modelFolder))
        {
            TryDeleteDirectory(modelFolder);
        }

        // 清理历史目录结构（components/networks/{id}/{version}）可能残留的分类目录
        var legacyNetworkRoot = Path.Combine(AppContext.BaseDirectory, "components", "networks", selected.Id);
        TryDeleteDirectoryIfEmpty(legacyNetworkRoot);

        await LoadNetworksAsync();
        _statusText.Text = $"已删除网络：{selected.Name}";
        _statusText.Foreground = Brushes.ForestGreen;
        AppLogService.Warn($"删除网络: {selected.Id}");
    }

    private async Task AddWeightFileAsync()
    {
        try
        {
            await _bootstrap.EnsureDefaultsAsync();
            _manifest ??= await _manifestService.LoadAsync();

            var dialog = new OpenFileDialog
            {
                Filter = "KataGo Network (*.bin.gz;*.bin)|*.bin.gz;*.bin|All files (*.*)|*.*",
                Multiselect = false,
                Title = "选择要添加的权重文件"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var sourcePath = dialog.FileName;
            var fileName = Path.GetFileName(sourcePath);
            var displayName = fileName.EndsWith(".bin.gz", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^7]
                : Path.GetFileNameWithoutExtension(fileName);
            var networkId = BuildOfflineNetworkId(displayName);
            var version = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");

            var relFolder = Path.Combine("components", "networks", networkId, version);
            var absFolder = Path.Combine(AppContext.BaseDirectory, relFolder);
            Directory.CreateDirectory(absFolder);

            var targetPath = Path.Combine(absFolder, fileName);
            File.Copy(sourcePath, targetPath, overwrite: true);

            var newNetwork = new ComponentModel
            {
                Id = networkId,
                Name = displayName,
                Version = version,
                Type = fileName.EndsWith(".bin.gz", StringComparison.OrdinalIgnoreCase) ? "bin.gz" : "bin",
                Urls = [],
                Entry = fileName,
                Source = "offline-import",
                SourcePage = null,
                PublishedAt = null,
                Sha256 = string.Empty,
                Size = 0
            };

            var nextNetworks = _manifest.Components.Networks
                .Where(n => !string.Equals(n.Id, newNetwork.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            nextNetworks.Insert(0, newNetwork);

            _manifest = _manifest with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Components = _manifest.Components with
                {
                    Networks = nextNetworks
                }
            };

            await SaveManifestSnapshotAsync(_manifest);
            await LoadNetworksAsync();
            SelectNetworkById(newNetwork.Id);

            _statusText.Text = $"已添加权重文件：{fileName}";
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"添加权重文件: {fileName} -> {BuildNetworkRelativePath(newNetwork)}");
        }
        catch (Exception ex)
        {
            _statusText.Text = $"添加失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
            MessageBox.Show($"添加权重失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DownloadNetworkAsync(bool preferStrongest)
    {
        try
        {
            await _bootstrap.EnsureDefaultsAsync();
            _manifest ??= await _manifestService.LoadAsync();
            var settings = await _settingsStoreService.LoadAsync(_bootstrap.SettingsPath);

            _statusText.Text = preferStrongest ? "正在下载最强权重..." : "正在下载最新权重...";
            _statusText.Foreground = (Brush)Application.Current.Resources["Brush.Accent"];
            ResetDownloadProgressUi();
            SetDownloadProgressVisible(true);

            using var client = CreateHttpClient(settings, includeKataGoTrainingHeaders: true);
            var html = await client.GetStringAsync(KataGoNetworksUrl);
            var picked = preferStrongest
                ? TryPickStrongestNetwork(html)
                : TryPickLatestNetwork(html);
            picked ??= TryPickFirstNetwork(html);
            if (picked is null)
            {
                throw new InvalidOperationException("未能从 KataGoTraining 页面解析权重下载地址。");
            }

            var (networkName, networkUrl) = picked.Value;
            if (!Uri.TryCreate(networkUrl, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"权重地址无效: {networkUrl}");
            }

            var id = preferStrongest ? "kata1-strongest" : "kata1-latest";
            var version = networkName;
            var publishedLookup = BuildPublishedAtLookupFromPage(html);
            var publishedAt = TryGetPublishedAtFromLookup(publishedLookup, version, networkName, networkUrl);
            var downloading = new ComponentModel
            {
                Id = id,
                Name = networkName,
                Version = version,
                Type = "bin.gz",
                Urls = [networkUrl],
                Entry = "model.bin.gz",
                Source = "katagotraining",
                SourcePage = KataGoNetworksUrl,
                PublishedAt = publishedAt,
                Sha256 = string.Empty,
                Size = 0
            };

            var preferredRelPath = BuildNetworkRelativePath(downloading);
            var legacyRelPath = BuildLegacyNetworkRelativePath(downloading);
            var preferredAbsPath = Path.Combine(AppContext.BaseDirectory, preferredRelPath.Replace('/', Path.DirectorySeparatorChar));
            var legacyAbsPath = Path.Combine(AppContext.BaseDirectory, legacyRelPath.Replace('/', Path.DirectorySeparatorChar));

            var existingPath = File.Exists(preferredAbsPath)
                ? preferredAbsPath
                : File.Exists(legacyAbsPath)
                    ? legacyAbsPath
                    : null;
            var targetPath = preferredAbsPath;
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (!string.IsNullOrWhiteSpace(existingPath))
            {
                var existingNetwork = _manifest.Components.Networks.FirstOrDefault(n =>
                    string.Equals(n.Id, downloading.Id, StringComparison.OrdinalIgnoreCase));
                if (existingNetwork is not null &&
                    (!string.Equals(existingNetwork.PublishedAt, downloading.PublishedAt, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(existingNetwork.Name, downloading.Name, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(existingNetwork.Version, downloading.Version, StringComparison.OrdinalIgnoreCase)))
                {
                    var nextNetworksForMetadata = _manifest.Components.Networks
                        .Where(n => !string.Equals(n.Id, downloading.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    nextNetworksForMetadata.Insert(0, downloading);
                    _manifest = _manifest with
                    {
                        UpdatedAt = DateTimeOffset.UtcNow,
                        Components = _manifest.Components with
                        {
                            Networks = nextNetworksForMetadata
                        }
                    };
                    await SaveManifestSnapshotAsync(_manifest);
                    await LoadNetworksAsync();
                    SelectNetworkById(downloading.Id);
                }

                var existedMessage = $"已存在同名权重文件，未重复下载：{existingPath}";
                _statusText.Text = existedMessage;
                _statusText.Foreground = Brushes.DarkGoldenrod;
                SetDownloadProgressVisible(false);
                AppLogService.Info($"跳过权重下载（同名已存在）: {existingPath}");
                MessageBox.Show(
                    $"检测到同名权重文件，已跳过下载。\n\n{existingPath}",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var downloader = new HttpDownloadService(client);
            var watch = Stopwatch.StartNew();
            var lastTick = watch.Elapsed;
            var lastUiTick = watch.Elapsed;
            long lastBytes = 0;
            double smoothedSpeed = 0;
            const double minSampleSeconds = 0.70;
            const double minUiRefreshSeconds = 0.60;
            const double emaAlpha = 0.22;
            var progress = new Progress<DownloadProgress>(p =>
            {
                var nowTick = watch.Elapsed;
                var percent = p.Percentage ?? 0;
                _downloadProgressBar.Value = Math.Clamp(percent, 0, 100);

                var deltaSeconds = (nowTick - lastTick).TotalSeconds;
                var completed = p.TotalBytes.HasValue && p.BytesReceived >= p.TotalBytes.Value;
                var shouldRefreshWindow = deltaSeconds >= minSampleSeconds || completed;
                if (shouldRefreshWindow)
                {
                    var deltaBytes = p.BytesReceived - lastBytes;
                    var instantSpeed = deltaSeconds > 0 ? Math.Max(0, deltaBytes / deltaSeconds) : 0;
                    smoothedSpeed = smoothedSpeed <= 0
                        ? instantSpeed
                        : smoothedSpeed * (1 - emaAlpha) + instantSpeed * emaAlpha;
                    lastTick = nowTick;
                    lastBytes = p.BytesReceived;
                }

                var uiDeltaSeconds = (nowTick - lastUiTick).TotalSeconds;
                var shouldRefreshUi = uiDeltaSeconds >= minUiRefreshSeconds || completed;
                if (shouldRefreshUi)
                {
                    var totalText = p.TotalBytes.HasValue ? FormatBytes(p.TotalBytes.Value) : "未知";
                    _downloadBytesText.Text = $"{FormatBytes(p.BytesReceived)} / {totalText}";
                    _downloadSpeedText.Text = FormatSpeed(smoothedSpeed);
                    _downloadFileSizeText.Text = totalText;
                    lastUiTick = nowTick;
                }
            });
            await downloader.DownloadAsync(uri, targetPath, progress);

            watch.Stop();
            var finalBytes = new FileInfo(targetPath).Length;
            var avgSpeed = watch.Elapsed.TotalSeconds <= 0
                ? 0
                : (long)(finalBytes / watch.Elapsed.TotalSeconds);
            _downloadProgressBar.Value = 100;
            _downloadBytesText.Text = $"{FormatBytes(finalBytes)} / {FormatBytes(finalBytes)}";
            _downloadSpeedText.Text = $"平均 {FormatSpeed(avgSpeed)}";
            _downloadFileSizeText.Text = FormatBytes(finalBytes);

            var downloaded = downloading;

            var nextNetworks = _manifest.Components.Networks
                .Where(n => !string.Equals(n.Id, downloaded.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            nextNetworks.Insert(0, downloaded);

            _manifest = _manifest with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Components = _manifest.Components with
                {
                    Networks = nextNetworks
                }
            };

            await SaveManifestSnapshotAsync(_manifest);
            var createdProfileName = await CreateProfileFromCurrentDefaultAsync(downloaded, preferStrongest);
            await LoadNetworksAsync();
            SelectNetworkById(downloaded.Id);

            var profileSuffix = string.IsNullOrWhiteSpace(createdProfileName) ? string.Empty : $"（已新增档案：{createdProfileName}）";
            _statusText.Text = (preferStrongest
                ? $"最强权重下载完成：{networkName}"
                : $"最新权重下载完成：{networkName}") + profileSuffix;
            _statusText.Foreground = Brushes.ForestGreen;
            AppLogService.Info($"下载权重完成: id={id}, name={networkName}, url={networkUrl}");
        }
        catch (Exception ex)
        {
            var message = $"下载失败：{ex.Message}";
            _statusText.Text = message;
            _statusText.Foreground = Brushes.IndianRed;
            AppLogService.Warn($"下载权重失败: {ex.Message}");
            SetDownloadProgressVisible(false);
        }
    }

    private void SelectNetworkById(string networkId)
    {
        for (var i = 0; i < _networkList.Items.Count; i++)
        {
            if (_networkList.Items[i] is ListBoxItem item &&
                string.Equals(item.Tag as string, networkId, StringComparison.OrdinalIgnoreCase))
            {
                _networkList.SelectedIndex = i;
                break;
            }
        }
    }

    private async Task<string?> CreateProfileFromCurrentDefaultAsync(ComponentModel network, bool preferStrongest)
    {
        try
        {
            var profiles = await _profileService.LoadAsync();
            var baseProfile = _profileService.GetDefault(profiles) ?? profiles.Profiles.FirstOrDefault();
            if (baseProfile is null)
            {
                AppLogService.Warn("下载权重后生成档案失败：未找到可复制的基础档案。");
                return null;
            }

            var now = DateTimeOffset.Now;
            var profileId = BuildNetworkProfileId(network.Id, network.Version);
            var existing = profiles.Profiles.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
            var tag = preferStrongest ? "最强" : "最新";
            var displayName = $"{baseProfile.Katago.Backend}-{tag}-{network.Name}";
            var modelPath = ResolveNetworkRelativePath(network);

            var generated = baseProfile with
            {
                ProfileId = profileId,
                DisplayName = displayName,
                Network = baseProfile.Network with
                {
                    Id = network.Id,
                    Name = network.Name,
                    Path = modelPath,
                    Source = network.Source ?? "katagotraining"
                },
                Katago = baseProfile.Katago with
                {
                    GtpArgs = _commandBuilder.BuildGtpCommand(baseProfile.Katago.Path, modelPath, baseProfile.Config.Path)
                },
                Tuning = baseProfile.Tuning with
                {
                    Status = "unknown",
                    LastBenchmarkAt = null,
                    RecommendedThreads = null
                },
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            var next = _profileService.Upsert(profiles, generated, setAsDefault: false);
            await _profileService.SaveAsync(next);
            AppLogService.Info($"下载权重后新增档案: {generated.ProfileId}");
            return generated.DisplayName;
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"下载权重后新增档案失败: {ex.Message}");
            return null;
        }
    }

    private async Task SaveManifestSnapshotAsync(ManifestModel manifest)
    {
        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
        await File.WriteAllTextAsync(_bootstrap.ManifestSnapshotPath, json);
    }

    private void ResetDownloadProgressUi()
    {
        _downloadProgressBar.Value = 0;
        _downloadBytesText.Text = "0 B / 未知";
        _downloadSpeedText.Text = "0 KB/s";
        _downloadFileSizeText.Text = "未知";
    }

    private void SetDownloadProgressVisible(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        _downloadProgressBar.Visibility = state;
        _downloadMetricsGrid.Visibility = state;
    }

    private static StackPanel BuildMetricPanel(string title, out TextBlock valueText)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 14, 0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 2)
        });

        valueText = new TextBlock
        {
            Text = "-",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None
        };
        panel.Children.Add(valueText);
        return panel;
    }

    private void OpenSelectedNetworkFolder()
    {
        var selected = GetSelectedNetwork();
        if (selected is null)
        {
            _statusText.Text = "请先选择网络。";
            _statusText.Foreground = Brushes.IndianRed;
            return;
        }

        var modelPath = ResolveNetworkRelativePath(selected);
        var absModelPath = Path.Combine(AppContext.BaseDirectory, modelPath.Replace('/', Path.DirectorySeparatorChar));
        var folder = Path.GetDirectoryName(absModelPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            _statusText.Text = "无法定位本地权重目录。";
            _statusText.Foreground = Brushes.IndianRed;
            return;
        }

        if (!Directory.Exists(folder))
        {
            _statusText.Text = $"本地权重目录不存在：{folder}";
            _statusText.Foreground = Brushes.DarkGoldenrod;
            MessageBox.Show($"本地权重目录不存在：\n{folder}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            _statusText.Text = $"已打开本地目录：{folder}";
            _statusText.Foreground = Brushes.ForestGreen;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"打开目录失败：{ex.Message}";
            _statusText.Foreground = Brushes.IndianRed;
        }
    }

    private static void TryMigrateLegacyNetworkLayout(ComponentModel network)
    {
        if (!ShouldUseFlatNetworkLayout(network))
        {
            return;
        }

        var preferred = BuildNetworkRelativePath(network);
        var legacy = BuildLegacyNetworkRelativePath(network);
        if (string.Equals(preferred, legacy, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var preferredAbs = ToAbsolutePath(preferred);
        var legacyAbs = ToAbsolutePath(legacy);
        if (File.Exists(preferredAbs) || !File.Exists(legacyAbs))
        {
            return;
        }

        try
        {
            var preferredDir = Path.GetDirectoryName(preferredAbs);
            if (!string.IsNullOrWhiteSpace(preferredDir))
            {
                Directory.CreateDirectory(preferredDir);
            }

            File.Move(legacyAbs, preferredAbs);
            var legacyVersionDir = Path.GetDirectoryName(legacyAbs);
            if (!string.IsNullOrWhiteSpace(legacyVersionDir))
            {
                TryDeleteDirectory(legacyVersionDir);
            }

            var legacyRootDir = Path.Combine(AppContext.BaseDirectory, "components", "networks", network.Id);
            TryDeleteDirectoryIfEmpty(legacyRootDir);
            AppLogService.Info($"已迁移权重目录结构: {legacy} -> {preferred}");
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"迁移权重目录失败: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string BuildOfflineNetworkId(string value)
    {
        var baseId = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "network";
        }

        return $"offline-{baseId}-{DateTimeOffset.Now:yyyyMMddHHmmss}";
    }

    private static string BuildNetworkProfileId(string networkId, string version)
    {
        var raw = $"p-{networkId}-{version}".ToLowerInvariant();
        var normalized = Regex.Replace(raw, "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = $"p-network-{DateTimeOffset.Now:yyyyMMddHHmmss}";
        }

        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    private string ResolveNetworkRelativePath(ComponentModel network)
    {
        var preferred = BuildNetworkRelativePath(network);
        if (File.Exists(ToAbsolutePath(preferred)))
        {
            return preferred;
        }

        var legacy = BuildLegacyNetworkRelativePath(network);
        if (!string.Equals(legacy, preferred, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(ToAbsolutePath(legacy)))
        {
            return legacy;
        }

        return preferred;
    }

    private static string BuildNetworkRelativePath(ComponentModel network)
    {
        var fileName = string.IsNullOrWhiteSpace(network.Entry) ? "model.bin.gz" : network.Entry;
        if (ShouldUseFlatNetworkLayout(network))
        {
            var folder = SanitizePathSegment(network.Version);
            return Path.Combine("components", "networks", folder, fileName)
                .Replace("\\", "/", StringComparison.Ordinal);
        }

        return BuildLegacyNetworkRelativePath(network);
    }

    private static string BuildLegacyNetworkRelativePath(ComponentModel network)
    {
        var fileName = string.IsNullOrWhiteSpace(network.Entry) ? "model.bin.gz" : network.Entry;
        return Path.Combine("components", "networks", network.Id, network.Version, fileName)
            .Replace("\\", "/", StringComparison.Ordinal);
    }

    private static bool ShouldUseFlatNetworkLayout(ComponentModel network)
    {
        if (!string.Equals(network.Source, "katagotraining", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(network.Id, "kata1-latest", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(network.Id, "kata1-strongest", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizePathSegment(string? value)
    {
        var segment = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "network";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            segment = segment.Replace(c, '-');
        }

        return segment.Trim();
    }

    private static string ToAbsolutePath(string relativePath)
    {
        return Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string BuildSearchablePageText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Regex.Replace(html, "<script[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<style[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text;
    }

    private static IReadOnlyDictionary<string, string> BuildPublishedAtLookupFromPage(string html)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html))
        {
            return lookup;
        }

        const string rowPattern =
            "<tr[^>]*>.*?<a[^>]*>(?<name>kata1-[^<\\s]+)</a>.*?<td[^>]*>\\s*(?<dt>\\d{4}-\\d{2}-\\d{2}\\s+\\d{2}:\\d{2}:\\d{2}\\s+UTC)\\s*</td>.*?</tr>";
        var rowMatches = Regex.Matches(html, rowPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in rowMatches)
        {
            var key = NormalizeNetworkNameToken(match.Groups["name"].Value);
            var normalizedDt = TryNormalizePublishedAt(match.Groups["dt"].Value);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(normalizedDt))
            {
                continue;
            }

            lookup[key] = normalizedDt;
        }

        if (lookup.Count > 0)
        {
            return lookup;
        }

        var pageText = BuildSearchablePageText(html);
        const string linePattern = "(?<name>kata1-[a-z0-9\\-]+)\\s+(?<dt>\\d{4}-\\d{2}-\\d{2}\\s+\\d{2}:\\d{2}:\\d{2}\\s+UTC)";
        var lineMatches = Regex.Matches(pageText, linePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in lineMatches)
        {
            var key = NormalizeNetworkNameToken(match.Groups["name"].Value);
            var normalizedDt = TryNormalizePublishedAt(match.Groups["dt"].Value);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(normalizedDt))
            {
                continue;
            }

            lookup[key] = normalizedDt;
        }

        return lookup;
    }

    private static string? TryGetPublishedAtFromLookup(IReadOnlyDictionary<string, string> lookup, params string?[] tokens)
    {
        if (lookup.Count == 0 || tokens.Length == 0)
        {
            return null;
        }

        foreach (var token in tokens)
        {
            var key = NormalizeNetworkNameToken(token);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (lookup.TryGetValue(key, out var publishedAt))
            {
                return publishedAt;
            }
        }

        return null;
    }

    private static string? NormalizeNetworkNameToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var value = WebUtility.HtmlDecode(token).Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = Path.GetFileName(uri.AbsolutePath);
        }

        value = value.Trim();
        if (value.EndsWith(".bin.gz", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^7];
        }

        var nameMatch = Regex.Match(value, "(?<name>kata1-[a-z0-9\\-]+)", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            return null;
        }

        return nameMatch.Groups["name"].Value.ToLowerInvariant();
    }

    private static string? TryNormalizePublishedAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss 'UTC'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var utcTime))
        {
            return utcTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utcTime))
        {
            return utcTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utcTime))
        {
            return utcTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string FormatPublishedAt(string? raw)
    {
        return TryNormalizePublishedAt(raw) ?? "未知";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var idx = 0;
        while (value >= 1024 && idx < units.Length - 1)
        {
            value /= 1024;
            idx++;
        }

        return idx == 0 ? $"{value:0} {units[idx]}" : $"{value:0.##} {units[idx]}";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 KB/s";
        }

        var kb = bytesPerSecond / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.0} KB/s";
        }

        var mb = kb / 1024.0;
        if (mb < 1024)
        {
            return $"{mb:0.00} MB/s";
        }

        var gb = mb / 1024.0;
        return $"{gb:0.000} GB/s";
    }

    private static HttpClient CreateHttpClient(SettingsModel settings, bool includeKataGoTrainingHeaders)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        var proxyMode = (settings.Proxy.Mode ?? "system").Trim().ToLowerInvariant();
        switch (proxyMode)
        {
            case "none":
                handler.UseProxy = false;
                break;
            case "manual":
                if (string.IsNullOrWhiteSpace(settings.Proxy.Address) ||
                    !Uri.TryCreate(settings.Proxy.Address, UriKind.Absolute, out var proxyUri))
                {
                    throw new InvalidOperationException("手动代理模式下，代理地址无效。请先在设置页修正代理地址。");
                }

                handler.UseProxy = true;
                handler.Proxy = new WebProxy(proxyUri);
                break;
            default:
                handler.UseProxy = true;
                handler.Proxy = WebRequest.DefaultWebProxy;
                break;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyKataGoLauncher/1.0");
        if (includeKataGoTrainingHeaders)
        {
            client.DefaultRequestHeaders.Referrer = new Uri(KataGoNetworksUrl);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://katagotraining.org");
        }

        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    private static (string NetworkName, string Url)? TryPickStrongestNetwork(string html)
    {
        return TryPickNetworkByLabel(
            html,
            "Strongest\\s+confidently(?:\\s|[-‑–—])*rated\\s+network");
    }

    private static (string NetworkName, string Url)? TryPickLatestNetwork(string html)
    {
        return TryPickNetworkByLabel(
            html,
            "Latest\\s+network");
    }

    private static (string NetworkName, string Url)? TryPickFirstNetwork(string html)
    {
        var urlMatch = Regex.Match(
            html,
            "https://media\\.katagotraining\\.org/uploaded/networks/models/kata1/(?<name>[^\"\\s']+)\\.bin\\.gz",
            RegexOptions.IgnoreCase);
        if (!urlMatch.Success)
        {
            return null;
        }

        var name = WebUtility.HtmlDecode(urlMatch.Groups["name"].Value);
        return (name, urlMatch.Value);
    }

    private static (string NetworkName, string Url)? TryPickNetworkByLabel(string html, string labelPattern)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var labelMatch = Regex.Match(
            html,
            $"{labelPattern}\\s*:\\s*(?<tail>.{{0,2500}})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!labelMatch.Success)
        {
            return null;
        }

        var tail = labelMatch.Groups["tail"].Value;
        var anchorMatch = Regex.Match(
            tail,
            "<a[^>]*href\\s*=\\s*\"(?<href>[^\"]+)\"[^>]*>\\s*(?<name>kata1-[^<\\s]+)\\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (anchorMatch.Success)
        {
            var name = WebUtility.HtmlDecode(anchorMatch.Groups["name"].Value);
            var href = WebUtility.HtmlDecode(anchorMatch.Groups["href"].Value);
            var url = NormalizeNetworkUrl(href, name);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return (name, url);
            }
        }

        var urlMatch = Regex.Match(
            tail,
            "https://media\\.katagotraining\\.org/uploaded/networks/models/kata1/(?<name>[^\"\\s'<>]+)\\.bin\\.gz",
            RegexOptions.IgnoreCase);
        if (!urlMatch.Success)
        {
            return null;
        }

        var fallbackName = WebUtility.HtmlDecode(urlMatch.Groups["name"].Value);
        return (fallbackName, urlMatch.Value);
    }

    private static string? NormalizeNetworkUrl(string href, string? fallbackName)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (href.StartsWith("/", StringComparison.Ordinal))
        {
            return "https://katagotraining.org" + href;
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            return $"https://media.katagotraining.org/uploaded/networks/models/kata1/{fallbackName}.bin.gz";
        }

        return null;
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
}

