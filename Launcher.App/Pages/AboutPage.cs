using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Pages;

public sealed class AboutPage : Page
{
    public AboutPage()
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
                    new TextBlock { Text = "关于 EasyKataGo", FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                    new TextBlock
                    {
                        Text = "EasyKataGo 是面向 KataGo + LizzieYzy 的一键启动与管理器。",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        });

        root.Children.Add(BuildInfoCard(
            "本项目可实现的功能",
            [
                "1. 安装向导：初始化默认档案、设置与清单快照。",
                "2. 权重管理：下载最新/最强权重，离线导入本地权重，应用到默认档案。",
                "3. 档案管理：维护多档案，切换默认档案，自动同步关键参数。",
                "4. 启动联动：校验并修正关键路径后启动 KataGo + LizzieYzy。",
                "5. 性能调优：基准测试、线程推荐、配置写回。",
                "6. 诊断与日志：运行自检、导出诊断包、查看运行日志。"
            ]));

        root.Children.Add(BuildInfoCard(
            "项目网址与鸣谢",
            [
                "KataGo: https://github.com/lightvector/KataGo",
                "LizzieYzy: https://github.com/yzyray/lizzieyzy",
                "KataGo Networks: https://katagotraining.org/networks/",
                "",
                "感谢 KataGo、LizzieYzy、KataGo Training 及其社区贡献者对开源围棋生态的持续投入与支持。"
            ]));

        scroll.Content = root;
        Content = scroll;
    }

    private static Border BuildInfoCard(string title, IReadOnlyList<string> lines)
    {
        var body = string.Join(Environment.NewLine, lines);
        return new Border
        {
            Style = (Style)Application.Current.Resources["CardBorderStyle"],
            Margin = new Thickness(0, 0, 0, 14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new TextBlock
                    {
                        Text = body,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["Brush.TextSecondary"]
                    }
                }
            }
        };
    }
}
