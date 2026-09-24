using System.Collections.ObjectModel;
using CleanBoost.System.Boost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanBoost.App.Pages;

public sealed class BoostStepModel
{
    public BoostStepModel(BoostStep step) => Source = step;

    public BoostStep Source { get; }
    public string Title => Source.Title;
    public string Description => Source.Description;
    public bool IsTurboOnly => Source.IsTurboOnly;
}

public sealed partial class BoosterPage : Page
{
    private readonly ObservableCollection<BoostStepModel> _steps = new();

    public BoosterPage()
    {
        InitializeComponent();
        ReloadSteps();
        StepsList.ItemsSource = _steps;
    }

    private void ReloadSteps()
    {
        _steps.Clear();
        foreach (var step in BoostCatalog.Light())
            _steps.Add(new BoostStepModel(step));
        if (TurboSwitch.IsOn)
            foreach (var step in BoostCatalog.Turbo())
                _steps.Add(new BoostStepModel(step));
    }

    private void TurboSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        TurboWarningPanel.Visibility = TurboSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
        ReloadSteps();
    }

    private async void RunBoostButton_Click(object sender, RoutedEventArgs e)
    {
        var includeRam = TurboSwitch.IsOn && TurboConsent.IsChecked == true;
        var steps = BoostCatalog.Light().ToList();
        if (TurboSwitch.IsOn && includeRam)
            steps.AddRange(BoostCatalog.Turbo());

        if (steps.Count == 0)
        {
            SummaryText.Text = "No steps selected — enable Turbo retail consent or use Light mode.";
            return;
        }

        RunBoostButton.IsEnabled = false;
        BoostProgress.IsActive = true;
        SummaryText.Text = "Applying boost…";

        try
        {
            var executor = new BoostExecutor();
            var results = await executor.RunAsync(steps);

            SummaryText.Text = string.Join(Environment.NewLine,
                results.Select(r => $"{(r.Succeeded ? "\u2713" : "\u2717")} {r.Step.Title}: {r.Message}"));
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"Boost failed: {ex.Message}";
        }
        finally
        {
            RunBoostButton.IsEnabled = true;
            BoostProgress.IsActive = false;
        }
    }
}