using System.Collections.ObjectModel;
using CleanBoost.Core.Audit;
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
    public HistoryPage()
    {
        InitializeComponent();
        var audit = new AuditLog();
        JournalPathText.Text = $"Journal: {audit.FilePath}";

        var entries = new ObservableCollection<HistoryEntry>();
        foreach (var record in audit.ReadAll().Reverse().Take(500))
        {
            entries.Add(new HistoryEntry
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
        HistoryList.ItemsSource = entries;
    }
}