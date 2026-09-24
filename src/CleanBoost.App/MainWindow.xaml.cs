using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanBoost.App.Pages;
using CleanBoost.System.Elevation;

namespace CleanBoost.App;

public sealed partial class MainWindow : Window
{
    private readonly AppWindowConfigurer _configurer;

    public MainWindow()
    {
        InitializeComponent();
        Title = "CleanBoost";

        ExtendsContentIntoTitleBar = true;
        _configurer = new AppWindowConfigurer(this);
        _configurer.SetTitleBar(AppTitleBar);

        ConfigureAdminBanner();
        ContentFrame.Navigate(typeof(CleanerPage));
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public string OwnerLine { get; } =
        "CleanBoost " + ProductInfo.Version +
        "  -  by " + ProductInfo.Owner +
        "  -  " + ProductInfo.Company +
        "  -  " + ProductInfo.Copyright;

    private void ConfigureAdminBanner()
    {
        if (ElevationHelper.IsElevated())
        {
            AdminBanner.Severity = InfoBarSeverity.Success;
            AdminBanner.Title = "Running as administrator";
            AdminBanner.Message = "Protected cleanup and service actions are now available.";
            AdminBanner.IsOpen = true;
            return;
        }

        AdminBanner.Severity = InfoBarSeverity.Informational;
        AdminBanner.Title = "Run as administrator";
        AdminBanner.Message = "Some cleanup and boost actions need administrator rights.";
        AdminBanner.ActionButton = new Button
        {
            Content = "Restart as administrator",
        };
        AdminBanner.ActionButton.Click += (_, _) => ElevationHelper.RelaunchElevated();
        AdminBanner.IsOpen = true;
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "About CleanBoost",
            PrimaryButtonText = "OK",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            Content =
                "CleanBoost " + ProductInfo.Version + "\n" +
                "by " + ProductInfo.Owner + "\n" +
                ProductInfo.Company + "\n" +
                ProductInfo.Copyright + "\n\n" +
                "A one-click Windows cleaner and booster.\n" +
                "Deletions go to the Recycle Bin first.\n" +
                "Elevation only happens after your consent.",
        };
        await dialog.ShowAsync();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            var type = tag switch
            {
                "Booster" => typeof(BoosterPage),
                "History" => typeof(HistoryPage),
                "Services" => typeof(ServicesPage),
                _ => typeof(CleanerPage),
            };
            ContentFrame.Navigate(type);
        }
    }
}
