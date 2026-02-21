using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Pages;

public class SimpleInfoPage : Page
{
    protected SimpleInfoPage(string title, string description, string? hint = null)
    {
        Background = Brushes.Transparent;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var root = new StackPanel
        {
            Margin = new Thickness(6, 2, 6, 20)
        };

        var header = new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14)
        };

        var headerPanel = new StackPanel();
        headerPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 15,
            Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"],
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(hint))
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = hint,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = (Brush)Application.Current.Resources["Brush.Accent"],
                FontWeight = FontWeights.SemiBold
            });
        }

        header.Child = headerPanel;
        root.Children.Add(header);
        scroll.Content = root;
        Content = scroll;
    }
}
