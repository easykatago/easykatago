using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Pages;
using Launcher.App.Services;

namespace Launcher.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Button> _navButtons = new(StringComparer.OrdinalIgnoreCase);
    private InstallPage? _installPage;

    public MainWindow()
    {
        InitializeComponent();
        RegisterNavigationButtons();
        Navigate("Home");
    }

    private void RegisterNavigationButtons()
    {
        _navButtons["Home"] = NavHomeButton;
        _navButtons["Install"] = NavInstallButton;
        _navButtons["Networks"] = NavNetworksButton;
        _navButtons["Profiles"] = NavProfilesButton;
        _navButtons["Logs"] = NavLogsButton;
        _navButtons["Diagnostics"] = NavDiagnosticsButton;
        _navButtons["Settings"] = NavSettingsButton;
        _navButtons["About"] = NavAboutButton;
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string pageKey)
        {
            return;
        }

        Navigate(pageKey);
    }

    private void Navigate(string pageKey)
    {
        AppLogService.Info($"导航到页面: {pageKey}");
        ContentFrame.Content = pageKey switch
        {
            "Install" => _installPage ??= new InstallPage(Navigate),
            "Networks" => new NetworksPage(),
            "Profiles" => new ProfilesPage(),
            "Logs" => new LogsPage(),
            "Diagnostics" => new DiagnosticsPage(),
            "Settings" => new SettingsPage(),
            "About" => new AboutPage(),
            _ => new HomePage(Navigate)
        };

        UpdateNavigationState(pageKey);
    }

    private void UpdateNavigationState(string activeKey)
    {
        var accent = (Brush)Application.Current.Resources["Brush.Accent"];
        var accentSoft = (Brush)Application.Current.Resources["Brush.AccentSoft"];
        var navActiveText = (Brush)Application.Current.Resources["Brush.NavActiveText"];
        var textSecondary = (Brush)Application.Current.Resources["Brush.TextSecondary"];
        var border = (Brush)Application.Current.Resources["Brush.Border"];

        foreach (var (key, button) in _navButtons)
        {
            var isActive = string.Equals(key, activeKey, StringComparison.OrdinalIgnoreCase);
            button.Background = isActive ? accentSoft : Brushes.Transparent;
            button.Foreground = isActive ? navActiveText : textSecondary;
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            button.BorderBrush = isActive ? accent : border;
            button.BorderThickness = isActive ? new Thickness(3, 1, 1, 1) : new Thickness(1);
        }
    }
}
