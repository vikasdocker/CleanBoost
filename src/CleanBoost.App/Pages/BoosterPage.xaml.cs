using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanBoost.System.Boost;
using CleanBoost.System.Elevation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanBoost.App.Pages;

public sealed class BoostStepModel : INotifyPropertyChanged
{
    public BoostStepModel(BoostStep step) => Source = step;

    public BoostStep Source { get; }
    public BoostStepKind Kind => Source.Kind;
    public string Title => Source.Title;
    public string Description => Source.Description;
    public bool IsTurboOnly => Source.IsTurboOnly;
    public bool RequiresElevation => Source.RequiresElevation;

    private string _status = "Pending";

    public string StatusText
    {
        get => _status;
        private set { if (_status == value) return; _status = value; Raise(); }
    }

    internal void SetStatus(string status) => StatusText = status;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class BoosterPage : Page
{
    private readonly ObservableCollection<BoostStepModel> _steps = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    public BoosterPage()
    {
        InitializeComponent();
        StepsList.ItemsSource = _steps;
        TurboConsent.Checked += TurboConsent_Changed;
        TurboConsent.Unchecked += TurboConsent_Changed;
        ReloadPlan();
    }

    /// <summary>
    /// Turbo only counts as selected once the consent box is ticked. The preview and
    /// the executor used to disagree here, so Turbo looked like it was queued and
    /// then silently did nothing.
    /// </summary>
    private bool TurboSelected => TurboSwitch.IsOn && TurboConsent.IsChecked == true;

    private List<BoostStep> Plan()
    {
        var steps = BoostCatalog.Light().ToList();
        if (TurboSelected)
            steps.AddRange(BoostCatalog.Turbo());
        return steps;
    }

    private void ReloadPlan()
    {
        TurboWarningPanel.Visibility = TurboSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;

        var plan = Plan();
        var kinds = plan.Select(s => s.Kind).ToHashSet();

        // Rebuild only when the set of steps actually changed, so live status
        // updates from a running boost are not wiped mid-run.
        var same = _steps.Count == plan.Count && _steps.All(m => kinds.Contains(m.Kind));
        if (!same)
        {
            _steps.Clear();
            foreach (var step in plan)
                _steps.Add(new BoostStepModel(step));
        }

        TurboConsent.IsEnabled = !_running;
        RunBoostButton.Content = TurboSwitch.IsOn ? "Apply Turbo" : "Apply boost";
        PlanSummaryText.Text = TurboSwitch.IsOn && !TurboSelected
            ? "Turbo is on but not consented — tick the box to include its 3 steps."
            : $"{plan.Count} step{(plan.Count == 1 ? "" : "s")} will run.";
    }

    private void TurboSwitch_Toggled(object sender, RoutedEventArgs e) => ReloadPlan();

    private void TurboConsent_Changed(object sender, RoutedEventArgs e) => ReloadPlan();

    private async void RunBoostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        var steps = Plan();
        if (steps.Count == 0)
        {
            SummaryText.Text = "No steps selected.";
            return;
        }

        if (!await ConfirmAsync())
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;

        SetRunning(true);
        foreach (var model in _steps)
            model.SetStatus("Pending");

        SummaryText.Text = "Applying…";
        BoostInfo.IsOpen = false;

        var models = _steps.ToDictionary(m => m.Kind);
        var progress = new Progress<BoostStepStep>(p =>
        {
            if (!models.TryGetValue(p.Kind, out var model))
                return;
            model.SetStatus(p.Result switch
            {
                "running" => "Running…",
                "done" => "Done",
                "skipped" => "Failed",
                _ => "Pending",
            });
        });

        try
        {
            var executor = new BoostExecutor();
            var results = await executor.RunAsync(steps, progress, cts.Token);

            var succeeded = results.Count(r => r.Succeeded);
            SummaryText.Text = string.Join(Environment.NewLine,
                results.Select(r => $"{(r.Succeeded ? "✓" : "✗")} {r.Step.Title}: {r.Message}"));

            BoostInfo.Severity = succeeded == results.Count ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            BoostInfo.Title = $"Boost finished — {succeeded} of {results.Count} steps succeeded.";
            BoostInfo.Message = "See the step list and the summary below for detail.";
            BoostInfo.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            SummaryText.Text = "Boost cancelled.";
            BoostInfo.Severity = InfoBarSeverity.Warning;
            BoostInfo.Title = "Boost cancelled";
            BoostInfo.Message = "Stopped before every step finished. Steps already applied are not undone.";
            BoostInfo.IsOpen = true;
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"Boost failed: {ex.Message}";
            BoostInfo.Severity = InfoBarSeverity.Error;
            BoostInfo.Title = "Boost failed";
            BoostInfo.Message = ex.Message;
            BoostInfo.IsOpen = true;
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_cts, cts))
                _cts = null;
            SetRunning(false);
        }
    }

    private void CancelBoostButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        SummaryText.Text = "Stopping after the current step…";
        _cts?.Cancel();
    }

    private async Task<bool> ConfirmAsync()
    {
        var steps = Plan();
        var needsAdmin = steps.Any(s => s.RequiresElevation) && !ElevationHelper.IsElevated();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = TurboSelected ? "Apply Turbo" : "Apply boost",
            Content = BuildConsentText(needsAdmin),
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private string BuildConsentText(bool needsAdmin)
    {
        var turboWarning = "Turbo steps include permanently emptying the Recycle Bin. Continue?";
        var lightText = "Apply the fully safe steps now?";

        var text = TurboSelected ? turboWarning : lightText;
        if (needsAdmin)
            text += " One step needs administrator mode and will be skipped — use the banner to restart elevated.";
        return text;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        RunBoostButton.IsEnabled = !running;
        TurboSwitch.IsEnabled = !running;
        BoostProgress.IsActive = running;
        CancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = running;
        ReloadPlan();
    }
}