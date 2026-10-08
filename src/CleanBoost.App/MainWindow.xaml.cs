using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CleanBoost.App.Pages;
using CleanBoost.System.Elevation;

namespace CleanBoost.App;

public sealed partial class MainWindow : Window
{
    private readonly AppWindowConfigurer _configurer;

    /// <summary>
    /// Pages are constructed once and reused. Without this the frame rebuilds a
    /// page on every tab click, which silently threw away the selection, the scan
    /// results and any in-flight cleanup the user had started.
    /// </summary>
    private readonly Dictionary<Type, Page> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "CleanBoost";

        ExtendsContentIntoTitleBar = true;
        _configurer = new AppWindowConfigurer(this);
        _configurer.SetTitleBar(AppTitleBar);

        ConfigureAdminBanner();
        Nav.SelectedItem = Nav.MenuItems[0];
        ShowPage(typeof(CleanerPage));
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
            ShowPage(tag switch
            {
                "Booster" => typeof(BoosterPage),
                "History" => typeof(HistoryPage),
                "Services" => typeof(ServicesPage),
                _ => typeof(CleanerPage),
            });
        }
    }

    /// <summary>
    /// Swaps in a cached page instance. Assigning <see cref="Frame.Content"/>
    /// directly (rather than calling Navigate) keeps the instance — and therefore
    /// its in-flight work — alive across tab switches.
    /// </summary>
    private void ShowPage(Type type)
    {
        if (ContentFrame.Content is Page current && current.GetType() == type)
            return;

        if (!_pages.TryGetValue(type, out var page))
        {
            page = (Page)Activator.CreateInstance(type)!;
            _pages[type] = page;
        }

        ContentFrame.Content = page;
    }
}
