using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;

namespace Launcher.App.Pages;

public sealed class LogsPage : Page
{
    private readonly ListBox _fileList;
    private readonly TextBox _contentBox;
    private readonly TextBlock _statusText;

    public LogsPage()
    {
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
                    new TextBlock { Text = "日志", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,8) },
                    new TextBlock
                    {
                        Text = "查看应用日志文件，支持快速刷新和尾部预览。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        main.ColumnDefinitions.Add(new ColumnDefinition());

        var listCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 12, 0)
        };
        var listPanel = new StackPanel();
        listPanel.Children.Add(new TextBlock { Text = "日志文件", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _fileList = new ListBox { MinHeight = 260 };
        _fileList.SelectionChanged += (_, _) => LoadSelectedContent();
        listPanel.Children.Add(_fileList);
        var refreshBtn = new Button { Content = "刷新", Width = 100, Height = 34, Margin = new Thickness(0, 10, 0, 0) };
        refreshBtn.Click += (_, _) => LoadFiles();
        listPanel.Children.Add(refreshBtn);
        listCard.Child = listPanel;
        Grid.SetColumn(listCard, 0);
        main.Children.Add(listCard);

        var contentCard = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
        var contentPanel = new StackPanel();
        contentPanel.Children.Add(new TextBlock { Text = "内容预览（最后 200 行）", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _contentBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas,Microsoft YaHei UI")
        };
        contentPanel.Children.Add(_contentBox);
        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
        };
        contentPanel.Children.Add(_statusText);
        contentCard.Child = contentPanel;
        Grid.SetColumn(contentCard, 1);
        main.Children.Add(contentCard);

        root.Children.Add(main);
        scroll.Content = root;
        Content = scroll;
        Loaded += (_, _) => LoadFiles();
    }

    private void LoadFiles()
    {
        _fileList.Items.Clear();
        var files = AppLogService.GetLogFiles();
        foreach (var file in files)
        {
            _fileList.Items.Add(new ListBoxItem { Content = Path.GetFileName(file), Tag = file });
        }

        if (_fileList.Items.Count > 0)
        {
            _fileList.SelectedIndex = 0;
        }

        _statusText.Text = $"日志文件数：{files.Count}";
    }

    private void LoadSelectedContent()
    {
        if (_fileList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            return;
        }

        _contentBox.Text = AppLogService.ReadTail(file, 200);
        _statusText.Text = $"当前文件：{file}";
    }
}
