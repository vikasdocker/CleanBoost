using Microsoft.UI.Xaml;

namespace CleanBoost.App;

/// <summary>Small helper for WinUI 3 window tricks (title bar drag region).</summary>
internal sealed class AppWindowConfigurer
{
    private readonly Window _window;

    public AppWindowConfigurer(Window window) => _window = window;

    public void SetTitleBar(UIElement element)
        => _window.SetTitleBar(element);
}