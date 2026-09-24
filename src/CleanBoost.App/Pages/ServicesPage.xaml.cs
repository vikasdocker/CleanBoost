using System.Collections.ObjectModel;
using CleanBoost.System.Elevation;
using CleanBoost.System.Startup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CleanBoost.App.Pages;

public sealed class ServiceModel
{
    public ServiceModel(string name, string? displayName, int start)
    {
        Name = name;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName!;
        IsMutable = start is 2 or 3 or 4;
        StateIndex = IsMutable ? start - 2 : -1;
        ModeText = start switch
        {
            0 => "Boot driver",
            1 => "System driver",
            2 => "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => $"Start={start}",
        };
    }

    public string Name { get; }
    public string DisplayName { get; }
    public bool IsMutable { get; }
    public int StateIndex { get; private set; }
    public string ModeText { get; private set; }

    public void Apply(int start)
    {
        StateIndex = start - 2;
        ModeText = start switch { 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => $"Start={start}" };
    }
}

public sealed partial class ServicesPage : Page
{
    public bool IsElevated { get; private set; }
    private bool IsInitialized;
    private readonly ObservableCollection<ServiceModel> _all = new();

    public ServicesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (IsInitialized)
            return;
        IsInitialized = true;
        _ = LoadServicesAsync(showBusy: true);
    }

    private async Task LoadServicesAsync(bool showBusy)
    {
        if (showBusy)
        {
            ServicesInfo.Severity = InfoBarSeverity.Informational;
            ServicesInfo.Title = "Loading services soon...";
            ServicesInfo.IsOpen = true;
        }

        try
        {
            var services = await Task.Run(() => ServiceTuner.GetServices());
            _all.Clear();
            foreach (var service in services)
                _all.Add(new ServiceModel(service.Name, service.DisplayName, service.Start));
            RefreshList();
            ServicesInfo.IsOpen = false;
        }
        catch (Exception ex)
        {
            ServicesInfo.Severity = InfoBarSeverity.Error;
            ServicesInfo.Title = "Could not load services";
            ServicesInfo.Message = ex.Message;
            ServicesInfo.IsOpen = true;
        }
    }

    private void RefreshList()
    {
        var filter = ServiceFilter?.Text?.Trim() ?? string.Empty;
        ServiceList.ItemsSource = filter.Length == 0
            ? _all
            : _all.Where(s => s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                              s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ServiceFilter_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ServiceStartMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.DataContext is not ServiceModel model || combo.SelectedIndex < 0)
            return;

        var start = combo.SelectedIndex + 2;
        if (ServiceTuner.SetAutostart(model.Name, start))
        {
            model.Apply(start);
            ServicesInfo.IsOpen = false;
            return;
        }

        combo.SelectedIndex = model.StateIndex;
        ServicesInfo.Severity = InfoBarSeverity.Warning;
        ServicesInfo.Title = "Could not update service";
        ServicesInfo.Message = ElevationHelper.IsElevated()
            ? "The change was rejected (start mode of boot/system drivers is locked)."
            : "This needs administrator mode.";
        ServicesInfo.IsOpen = true;
    }
}
