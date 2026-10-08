using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanBoost.App.Services;
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

public sealed class CategoryModel : INotifyPropertyChanged
{
    public CategoryModel(CleanCategory source) => Source = source;

    public CleanCategory Source { get; }
    public string Key => Source.Key;
    public string Title => Source.Title;
    public string Description => Source.Description;
    public string Icon => Source.Icon;

    public bool IsCommunity { get; init; }

    private long _size;
    private int _items;

    public long Size
    {
        get => _size;
        set { if (_size == value) return; _size = value; Raise(nameof(SizeText)); }
    }

    public int Items
    {
        get => _items;
        set { if (_items == value) return; _items = value; Raise(nameof(ItemsText)); }
    }

    public string SizeText => Formats.Bytes(Size);
    public string ItemsText => Items == 0 ? string.Empty : Formats.Plural(Items, "item");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One row in the live cleanup activity list.</summary>
public sealed class DeleteLogModel
{
    public required string PathText { get; init; }
    public required string CategoryText { get; init; }
    public required string OutcomeText { get; init; }

    public static DeleteLogModel From(DeleteProgress p) => new()
    {
        PathText = p.Path,
        CategoryText = p.CategoryTitle,
        OutcomeText = p.Outcome switch
        {
            DeleteOutcome.Deleted => "Deleted",
            DeleteOutcome.SkippedLocked => "Locked",
            DeleteOutcome.SkippedProtected => "Refused",
            DeleteOutcome.SkippedNotPresent => "Gone",
            _ => "Error",
        },
    };
}

/// <summary>
/// Exact running totals for a cleanup. Fed from the deletion worker thread on every
/// single item, so it must stay lock-free — the live row list may shed rows under
/// load but these numbers never drift.
/// </summary>
internal sealed class DeleteTally
{
    private int _processed;
    private int _deleted;
    private int _locked;
    private int _refused;
    private int _failed;
    private long _freed;

    public int Processed => Volatile.Read(ref _processed);
    public int Deleted => Volatile.Read(ref _deleted);
    public int Locked => Volatile.Read(ref _locked);
    public int Refused => Volatile.Read(ref _refused);
    public int Failed => Volatile.Read(ref _failed);
    public long Freed => Interlocked.Read(ref _freed);

    public void Add(DeleteProgress p)
    {
        Interlocked.Increment(ref _processed);
        switch (p.Outcome)
        {
            case DeleteOutcome.Deleted:
                Interlocked.Increment(ref _deleted);
                Interlocked.Add(ref _freed, p.SizeBytes);
                break;
            case DeleteOutcome.SkippedLocked:
                Interlocked.Increment(ref _locked);
                break;
            case DeleteOutcome.SkippedNotPresent:
                break;
            case DeleteOutcome.SkippedProtected:
                Interlocked.Increment(ref _refused);
                break;
            default:
                Interlocked.Increment(ref _failed);
                break;
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _processed, 0);
        Interlocked.Exchange(ref _deleted, 0);
        Interlocked.Exchange(ref _locked, 0);
        Interlocked.Exchange(ref _refused, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _freed, 0);
    }
}

public sealed partial class CleanerPage : Page
{
    /// <summary>Live activity rows kept on screen. Counters above them stay exact.</summary>
    private const int MaxLiveRows = 300;

    private readonly PathGuard _guard;
    private readonly PathResolver _resolver = new();
    private readonly ObservableCollection<CategoryModel> _categories = new();
    private readonly List<CategoryModel> _community = new();
    private readonly ObservableCollection<DeleteLogModel> _liveRows = new();
    private readonly bool _rulesAvailable;

    private ScanReport? _lastReport;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _cleanCts;
    private DeleteTally _tally = new();
    private int _cleanTotal;
    private bool _busy;
    private bool _cleaning;

    public CleanerPage()
    {
        InitializeComponent();

        _guard = GuardFactory.CreateDefault();
        foreach (var category in BuiltInCatalog.Create())
            _categories.Add(new CategoryModel(category));
        CategoryList.ItemsSource = _categories;
        DeleteLogList.ItemsSource = _liveRows;

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

        UpdateSelectionUi();
        UpdateBusyUi();
    }

    // ─────────────────────────────── community rules ───────────────────────────────

    private async void CommunityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

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
            CommunityToggle.IsEnabled = _rulesAvailable;
        }
    }

    // ───────────────────────────────────── scan ─────────────────────────────────────

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync()
    {
        if (_busy)
            return;

        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        ScanInfo.IsOpen = false;
        CleanProgress.IsIndeterminate = true;
        CleanProgress.Value = 0;

        foreach (var model in _categories)
        {
            model.Size = 0;
            model.Items = 0;
        }

        var categories = _categories.Select(m => m.Source).ToList();
        var progress = new Progress<ScanProgress>(p =>
            SummaryText.Text = $"Scanning {p.CategoryTitle}… {Formats.Plural(p.FilesScanned, "file")}");

        SetBusy(scanning: true);

        try
        {
            var engine = new ScanEngine(_resolver, _guard);
            var report = await Task.Run(() => engine.Scan(categories, progress, cts.Token), cts.Token);

            _lastReport = report;
            foreach (var model in _categories)
                if (report.ByCategory.TryGetValue(model.Key, out var stat))
                {
                    model.Size = stat.Size;
                    model.Items = stat.Count;
                }

            SummaryText.Text = report.TotalItems == 0
                ? "Nothing found — your system is clean."
                : $"Found {Formats.Plural(report.TotalItems, "item")} · {Formats.Bytes(report.TotalSize)} can be freed.";
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
            cts.Dispose();
            if (ReferenceEquals(_scanCts, cts))
                _scanCts = null;
            SetBusy(scanning: false);
        }
    }

    // ──────────────────────────────────── clean ────────────────────────────────────

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
        => await RunCleanAsync(SelectedKeys());

    private async void CleanAllButton_Click(object sender, RoutedEventArgs e)
        => await RunCleanAsync(null);

    private HashSet<string> SelectedKeys()
        => CategoryList.SelectedItems.OfType<CategoryModel>().Select(m => m.Key).ToHashSet();

    private async Task RunCleanAsync(HashSet<string>? keys)
    {
        if (_busy || _lastReport is null)
            return;

        // Null keys means "everything the scan found".
        var wanted = keys is null
            ? _categories.Where(c => c.Items > 0).Select(c => c.Key).ToHashSet()
            : keys;

        var models = _categories.Where(c => wanted.Contains(c.Key)).ToList();
        var items = _lastReport.Items.Where(i => wanted.Contains(i.CategoryKey)).ToList();

        if (items.Count == 0)
        {
            var nothingPicked = models.Count == 0;
            CleanInfo.Severity = InfoBarSeverity.Warning;
            CleanInfo.Title = keys is null
                ? "Nothing to clean"
                : nothingPicked ? "Nothing selected" : "Nothing to clean";
            CleanInfo.Message = keys is null
                ? "The scan found nothing to remove."
                : nothingPicked
                    ? "Select at least one category to clean."
                    : "The selected categories have nothing to remove.";
            CleanInfo.IsOpen = true;
            return;
        }

        if (models.Any(m => m.Source.Risk == RiskLevel.ConfirmFirst))
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

        // Bound pruning: only folders that a chosen category explicitly marked
        // RemoveSelf may be emptied out, and never anything above them.
        var pruneRoots = BuildPruneRoots(models);

        var cts = new CancellationTokenSource();
        _cleanCts = cts;

        _tally = new DeleteTally();
        _cleanTotal = items.Count;
        _liveRows.Clear();
        CleanLivePanel.Visibility = Visibility.Visible;
        CleanLiveSummary.Text = $"Cleaning {Formats.Plural(items.Count, "item")}…";
        CleanInfo.IsOpen = false;

        CleanProgress.IsIndeterminate = false;
        CleanProgress.Minimum = 0;
        CleanProgress.Maximum = items.Count;
        CleanProgress.Value = 0;

        var tally = _tally;
        var total = _cleanTotal;
        var live = new ThrottledProgress<DeleteProgress>(DispatcherQueue, batch => OnLiveBatch(batch, tally, total), tally.Add);

        SetBusy(cleaning: true);
        UpdateLiveSummary(tally, total, running: true);

        var rescan = false;
        try
        {
            var deleter = new SafeDeleter(_guard, new AuditLog());
            var attempts = await Task.Run(
                () => deleter.Delete(items, new RecycleBinDeleter(), pruneRoots, cts.Token, live));

            var deleted = attempts.Count(a => a.Outcome == DeleteOutcome.Deleted);
            var locked = attempts.Count(a => a.Outcome == DeleteOutcome.SkippedLocked);
            var refused = attempts.Count(a => a.Outcome == DeleteOutcome.SkippedProtected);
            var skipped = attempts.Count(a => a.Outcome is DeleteOutcome.SkippedError or DeleteOutcome.SkippedNotPresent);

            CleanInfo.Severity = deleted > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            CleanInfo.Title = deleted > 0 ? $"Cleaned {Formats.Plural(deleted, "item")}" : "Nothing was cleaned";
            CleanInfo.Message = $"To Recycle Bin: {deleted}. Locked/in use: {locked}. Refused: {refused}. Skipped: {skipped}.";
            CleanInfo.IsOpen = true;

            CleanProgress.Value = attempts.Count;
            rescan = true;
        }
        catch (OperationCanceledException)
        {
            CleanInfo.Severity = InfoBarSeverity.Warning;
            CleanInfo.Title = "Cleanup cancelled";
            CleanInfo.Message = $"Stopped after {tally.Processed} of {total} items.";
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
            live.Dispose();
            cts.Dispose();
            if (ReferenceEquals(_cleanCts, cts))
                _cleanCts = null;

            UpdateLiveSummary(tally, total, running: false);
            SetBusy(cleaning: false);
        }

        // Re-scan so the sizes on screen match what is left on disk. Awaited — firing
        // it off unobserved used to race the button state and swallow its exceptions.
        // Must run after SetBusy, because RunScanAsync refuses to start while busy.
        if (rescan)
            await RunScanAsync();
    }

    private void OnLiveBatch(IReadOnlyList<DeleteProgress> batch, DeleteTally tally, int total)
    {
        foreach (var p in batch)
        {
            _liveRows.Insert(0, DeleteLogModel.From(p));
            if (_liveRows.Count > MaxLiveRows)
                _liveRows.RemoveAt(_liveRows.Count - 1);
        }

        CleanProgress.Value = Math.Min(tally.Processed, total);
        UpdateLiveSummary(tally, total, running: true);
    }

    private void UpdateLiveSummary(DeleteTally tally, int total, bool running)
    {
        var percent = total == 0 ? 100 : (int)(tally.Processed * 100L / total);
        CleanLiveSummary.Text =
            $"{(running ? "Deleting" : "Finished")} — {tally.Processed} of {total} ({percent}%)  ·  " +
            $"Freed {Formats.Bytes(tally.Freed)}  ·  Deleted {tally.Deleted}  ·  " +
            $"Locked {tally.Locked}  ·  Refused {tally.Refused}  ·  Failed {tally.Failed}";
    }

    /// <summary>
    /// Collects the target folders that opted into REMOVESELF semantics. Pruning is
    /// bounded to these — previously the deleter walked all the way up the tree,
    /// which could empty unrelated folders above the target.
    /// </summary>
    private HashSet<string> BuildPruneRoots(IReadOnlyCollection<CategoryModel> models)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        foreach (var target in model.Source.Targets)
        {
            if (!target.RemoveSelf)
                continue;

            var expanded = _resolver.Expand(target.Path);
            if (string.IsNullOrWhiteSpace(expanded) ||
                expanded.Contains('*') || expanded.Contains('?') ||
                !Path.IsPathRooted(expanded))
                continue;

            try
            {
                roots.Add(Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch (Exception)
            {
                // an unusable target simply contributes no prune root
            }
        }
        return roots;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cleaning)
        {
            _cleanCts?.Cancel();
            CancelButton.IsEnabled = false;
            CleanLiveSummary.Text = "Stopping after the current batch…";
        }
        else
        {
            _scanCts?.Cancel();
            CancelButton.IsEnabled = false;
        }
    }

    // ────────────────────────────── selection actions ──────────────────────────────

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Select every category the scan actually found something in.
        foreach (var model in _categories)
            if (model.Items > 0)
                CategoryList.SelectedItems.Add(model);
        UpdateSelectionUi();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        CategoryList.SelectedItems.Clear();
        UpdateSelectionUi();
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateSelectionUi();

    private void UpdateSelectionUi()
    {
        var selected = CategoryList.SelectedItems.Count;
        SelectionSummary.Text = selected == 0
            ? "No categories selected"
            : $"{selected} of {_categories.Count} selected";

        UpdateBusyUi();
    }

    // ──────────────────────────────────── state ─────────────────────────────────────

    private void SetBusy(bool scanning = false, bool cleaning = false)
    {
        _busy = scanning || cleaning;
        _cleaning = cleaning;

        UpdateBusyUi();

        CleanProgress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = _busy;
        if (!_busy)
            CleanProgress.IsIndeterminate = false;
    }

    private void UpdateBusyUi()
    {
        var hasFindings = _categories.Any(c => c.Items > 0);
        var selection = CategoryList.SelectedItems.OfType<CategoryModel>().ToList();

        ScanButton.IsEnabled = !_busy;
        CategoryList.IsEnabled = !_busy;
        CommunityToggle.IsEnabled = !_busy && _rulesAvailable;

        // "Clean selected" only lights up when the selection can actually clean
        // something — otherwise the button invites a pointless "nothing to clean".
        CleanButton.IsEnabled = !_busy && selection.Count > 0 && selection.Any(m => m.Items > 0);
        CleanAllButton.IsEnabled = !_busy && hasFindings;
        SelectAllButton.IsEnabled = !_busy && hasFindings;
        ClearSelectionButton.IsEnabled = !_busy && selection.Count > 0;
    }
}