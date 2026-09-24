using System.Collections.ObjectModel;
using CleanBoost.Core.Audit;
using CleanBoost.Core.Catalog;
using CleanBoost.Core.Deletion;
using CleanBoost.Core.Rules;
using CleanBoost.Core.Safety;
using CleanBoost.Core.Scan;
using CleanBoost.System.Recycle;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanBoost.App.Pages;

public static class Formats
{
    public static string Bytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):F2} GB",
    };

    public static string Plural(long n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
}

public sealed class CategoryModel
{
    public CategoryModel(CleanCategory source) => Source = source;

    public CleanCategory Source { get; }
    public string Key => Source.Key;
    public string Title => Source.Title;
    public string Description => Source.Description;
    public string Icon => Source.Icon;

    public bool IsCommunity { get; init; }

    public long Size { get; set; }
    public long Items { get; set; }

    public string SizeText => Formats.Bytes(Size);
    public string ItemsText => Items == 0 ? string.Empty : Formats.Plural(Items, "item");
}

public sealed partial class CleanerPage : Page
{
    private readonly PathGuard _guard;
    private readonly ObservableCollection<CategoryModel> _categories = new();
    private readonly List<CategoryModel> _community = new();
    private readonly bool _rulesAvailable;
    private ScanReport? _lastReport;
    private CancellationTokenSource? _scanCts;

    public CleanerPage()
    {
        InitializeComponent();
        _guard = GuardFactory.CreateDefault();
        foreach (var category in BuiltInCatalog.Create())
            _categories.Add(new CategoryModel(category));
        CategoryList.ItemsSource = _categories;

        _rulesAvailable = WinappParser.LocateRulesFile() is not null;
        if (!_rulesAvailable)
        {
            CommunityToggle.IsEnabled = false;
            CommunityStatus.Text = "winapp2.ini not found — community rules are unavailable.";
        }
        else
        {
            CommunityStatus.Text = "Optional: add community recipes for the long tail of installed apps.";
        }
    }

    private async void CommunityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!CommunityToggle.IsOn)
        {
            foreach (var model in _community)
                _categories.Remove(model);
            _community.Clear();
            CommunityStatus.Text = "Community rules off.";
            return;
        }

        var path = WinappParser.LocateRulesFile();
        if (path is null)
        {
            CommunityToggle.IsOn = false;
            CommunityStatus.Text = "winapp2.ini not found.";
            return;
        }

        CommunityToggle.IsEnabled = false;
        try
        {
            var categories = await Task.Run(() => WinappParser.ParseFile(path));
            _community.Clear();
            foreach (var category in categories)
                _community.Add(new CategoryModel(category) { IsCommunity = true });
            foreach (var model in _community)
                _categories.Add(model);
            CommunityStatus.Text = $"{categories.Count} community rules loaded.";
        }
        catch (Exception ex)
        {
            CommunityToggle.IsOn = false;
            CommunityStatus.Text = "Could not load community rules.";
            ScanInfo.Severity = InfoBarSeverity.Error;
            ScanInfo.Title = "Rules failed to load";
            ScanInfo.Message = ex.Message;
            ScanInfo.IsOpen = true;
        }
        finally
        {
            CommunityToggle.IsEnabled = true;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        ScanButton.IsEnabled = false;
        CleanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ScanInfo.IsOpen = false;

        foreach (var model in _categories)
        {
            model.Size = 0;
            model.Items = 0;
        }

        var categories = _categories.Select(m => m.Source).ToList();
        var progress = new Progress<ScanProgress>(p =>
            SummaryText.Text = $"Scanning {p.CategoryTitle}… {Formats.Plural(p.FilesScanned, "file")}");

        try
        {
            var engine = new ScanEngine(new PathResolver(), _guard);
            var report = await Task.Run(
                () => engine.Scan(categories, progress, cts.Token),
                cts.Token);

            _lastReport = report;
            var byCategory = report.ByCategory;
            foreach (var model in _categories)
            {
                if (byCategory.TryGetValue(model.Key, out var stat))
                {
                    model.Size = stat.Size;
                    model.Items = stat.Count;
                }
            }

            CategoryList.ItemsSource = new ObservableCollection<CategoryModel>(_categories);
            SummaryText.Text = report.TotalItems == 0
                ? "Nothing found — your system is clean."
                : $"Found {Formats.Plural(report.TotalItems, "item")} · {Formats.Bytes(report.TotalSize)} can be freed.";

            CleanButton.IsEnabled = report.TotalItems > 0;
        }
        catch (OperationCanceledException)
        {
            SummaryText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Scan failed.";
            ScanInfo.Severity = InfoBarSeverity.Error;
            ScanInfo.Title = "Scan failed";
            ScanInfo.Message = ex.Message;
            ScanInfo.IsOpen = true;
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
        }
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
            return;

        var selectedKeys = CategoryList.SelectedItems.OfType<CategoryModel>().Select(m => m.Key).ToHashSet();
        var items = _lastReport.Items
            .Where(i => selectedKeys.Contains(i.CategoryKey))
            .ToList();

        if (items.Count == 0)
        {
            CleanInfo.Severity = InfoBarSeverity.Warning;
            CleanInfo.Title = "Nothing selected";
            CleanInfo.Message = "Select at least one category to clean.";
            CleanInfo.IsOpen = true;
            return;
        }

        if (CategoryList.SelectedItems.OfType<CategoryModel>().Any(m => m.Source.Risk == RiskLevel.ConfirmFirst))
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Confirm sensitive cleanup",
                Content = "One or more selected categories are flagged as sensitive — they can remove data you created yourself, not just regenerable cache. Continue anyway?",
                PrimaryButtonText = "Clean anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        CleanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;

        try
        {
            var deleter = new SafeDeleter(_guard, new AuditLog());
            var attempts = await Task.Run(() =>
                deleter.Delete(items, new RecycleBinDeleter()));

            var deleted = attempts.Count(a => a.Outcome == DeleteOutcome.Deleted);
            var locked = attempts.Count(a => a.Outcome == DeleteOutcome.SkippedLocked);
            var skipped = attempts.Count(a => a.Outcome is DeleteOutcome.SkippedProtected or DeleteOutcome.SkippedError);

            CleanInfo.Severity = deleted > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            CleanInfo.Title = deleted > 0 ? $"Cleaned {Formats.Plural(deleted, "item")}" : "Nothing was cleaned";
            CleanInfo.Message = $"To Recycle Bin: {deleted}. Locked/in use: {locked}. Skipped: {skipped}.";
            CleanInfo.IsOpen = true;
        }
        catch (Exception ex)
        {
            CleanInfo.Severity = InfoBarSeverity.Error;
            CleanInfo.Title = "Clean failed";
            CleanInfo.Message = ex.Message;
            CleanInfo.IsOpen = true;
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            CleanButton.IsEnabled = true;
        }

        ScanButton_Click(sender, new RoutedEventArgs());
    }
}