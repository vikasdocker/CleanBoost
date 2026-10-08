using System.Collections.ObjectModel;
using CleanBoost.Core.Audit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanBoost.App.Pages;

public sealed class HistoryEntry
{
    public string Title { get; init; } = "";
    public string PathText { get; init; } = "";
    public string TimestampText { get; init; } = "";
    public string Outcome { get; init; } = "";
}

public sealed partial class HistoryPage : Page
{
    private readonly AuditLog _audit = new();
    private readonly ObservableCollection<HistoryEntry> _entries = new();
    private bool _loaded;
    private bool _loading;

    public HistoryPage()
    {
        InitializeComponent();
        JournalPathText.Text = $"Journal: {_audit.FilePath}";
        HistoryList.ItemsSource = _entries;

        // Loaded, not the constructor: pages are cached and swapped in by the shell,
        // and OnNavigatedTo no longer fires. The read also stays off the UI thread.
        Loaded += async (_, _) =>
        {
            if (_loaded)
                return;
            _loaded = true;
            await ReloadAsync();
        };
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_loading)
            return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        HistoryProgress.IsActive = true;
        StatusText.Text = "Loading…";

        try
        {
            var records = await Task.Run(() => _audit.ReadTail(500));

            _entries.Clear();
            foreach (var record in records)
            {
                _entries.Add(new HistoryEntry
                {
                    Title = record.Category,
                    PathText = record.Path,
                    TimestampText = record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                    Outcome = record.Outcome switch
                    {
                        "Deleted" => "Deleted",
                        "SkippedLocked" => "Locked",
                        "SkippedProtected" => "Refused",
                        _ => record.Outcome,
                    },
                });
            }

            StatusText.Text = _entries.Count == 0
                ? "No deletions recorded yet."
                : $"{_entries.Count} entr{(_entries.Count == 1 ? "y" : "ies")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read the journal: {ex.Message}";
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
            HistoryProgress.IsActive = false;
        }
    }
}