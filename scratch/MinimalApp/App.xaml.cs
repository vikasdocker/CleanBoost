using Microsoft.UI.Xaml;

namespace MinimalApp;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window { Title = "Minimal" };
        window.Activate();
    }
}