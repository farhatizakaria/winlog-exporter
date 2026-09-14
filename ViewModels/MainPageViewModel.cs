using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogCollector.Helpers;
using LogCollector.Models;
using LogCollector.Services;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LogCollector.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly EventLogService _service = new();

    [ObservableProperty]
    public partial string DestinationFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"WindowsLogs_{SanitizeHostName(Environment.MachineName)}_{DateTime.Now:yyyyMMdd_HHmmss}");

    private static string SanitizeHostName(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "HOST";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Join("_", host.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        safe = safe.Replace(' ', '_');
        return string.IsNullOrWhiteSpace(safe) ? "HOST" : safe;
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsCollecting { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    [ObservableProperty]
    public partial string StatusSeverity { get; set; } = "Informational";

    [ObservableProperty]
    public partial bool IsAdmin { get; set; } = AdminHelper.IsRunningAsAdmin();

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial bool HasFailures { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFilteredEmpty { get; set; }

    public ObservableCollection<LogChannelInfo> AllLogs { get; } = [];

    public ObservableCollection<LogChannelInfo> FilteredLogs { get; } = [];

    private CancellationTokenSource? _cts;

    public MainPageViewModel()
    {
        // Watch SearchText changes via property changed
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchText))
                ApplyFilter();
        };
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Enumerating Windows Logs channels…";
        HasStatus = false;
        ProgressText = string.Empty;

        try
        {
            var logs = await _service.GetAllLogsAsync();
            AllLogs.Clear();
            foreach (var log in logs)
            {
                log.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LogChannelInfo.IsSelected))
                        UpdateCounts();
                };
                AllLogs.Add(log);
            }
            TotalCount = AllLogs.Count;
            ApplyFilter();
            UpdateCounts();

            if (AllLogs.Count == 0)
            {
                StatusMessage = "No Windows Logs found or access denied.";
                StatusSeverity = "Warning";
                HasStatus = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to enumerate logs: {ex.Message}";
            StatusSeverity = "Error";
            HasStatus = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = App.WindowHandle;
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                DestinationFolder = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Folder picker failed: {ex.Message}";
            StatusSeverity = "Error";
            HasStatus = true;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var log in FilteredLogs)
            log.IsSelected = true;
        UpdateCounts();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var log in FilteredLogs)
            log.IsSelected = false;
        UpdateCounts();
    }

    [RelayCommand]
    private async Task CollectAsync()
    {
        if (IsCollecting) return;

        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            StatusMessage = "Please choose a destination folder.";
            StatusSeverity = "Warning";
            HasStatus = true;
            return;
        }

        var selected = AllLogs.Count(l => l.IsSelected);
        if (selected == 0)
        {
            StatusMessage = "No Windows Logs selected. Check at least one channel.";
            StatusSeverity = "Warning";
            HasStatus = true;
            return;
        }

        IsCollecting = true;
        HasStatus = false;
        HasFailures = false;
        SummaryText = string.Empty;
        ProgressValue = 0;
        ProgressText = $"Starting export of {selected} Windows Logs…";
        _cts = new CancellationTokenSource();

        // Ensure destination folder name contains hostname
        var host = SanitizeHostName(Environment.MachineName);
        if (!DestinationFolder.Contains(host, StringComparison.OrdinalIgnoreCase))
        {
            // Append hostname subfolder to respect user's picked parent but guarantee hostname in path
            DestinationFolder = Path.Combine(DestinationFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                $"WindowsLogs_{host}_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        try
        {
            Directory.CreateDirectory(DestinationFolder);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cannot create destination folder: {ex.Message}";
            StatusSeverity = "Error";
            HasStatus = true;
            IsCollecting = false;
            return;
        }

        var progress = new Progress<CollectProgress>(p =>
        {
            App.DispatcherQueue.TryEnqueue(() =>
            {
                ProgressValue = p.Total == 0 ? 0 : (double)p.CurrentIndex / p.Total * 100;
                ProgressText = p.Success
                    ? $"Exported {p.LogName} ({p.CurrentIndex}/{p.Total})"
                    : $"Failed {p.LogName}: {p.Error} ({p.CurrentIndex}/{p.Total})";
            });
        });

        try
        {
            var result = await _service.CollectLogsAsync(AllLogs, DestinationFolder, progress, _cts.Token);

            ProgressValue = 100;
            if (result.Failed == 0 && result.Succeeded > 0)
            {
                StatusMessage = $"Export complete — {result.Succeeded} Windows Logs exported to {result.DestinationFolder} in {result.Duration.TotalSeconds:0.0}s.";
                StatusSeverity = "Success";
                SummaryText = $"{result.Succeeded} succeeded • {result.Skipped} skipped • {result.Failed} failed";
            }
            else if (result.Succeeded > 0)
            {
                StatusMessage = $"Export finished with issues — {result.Succeeded} succeeded, {result.Failed} failed. Check {result.DestinationFolder}";
                StatusSeverity = "Warning";
                SummaryText = $"{result.Succeeded} succeeded • {result.Skipped} skipped • {result.Failed} failed — {string.Join("; ", result.Failures.Take(2).Select(f => $"{f.LogName}: {f.Error}"))}";
                HasFailures = result.Failures.Count > 0;
            }
            else
            {
                StatusMessage = $"Export failed — {result.Failed} failures, {result.Skipped} skipped. Try running as Administrator (Security log requires elevation).";
                StatusSeverity = "Error";
                SummaryText = result.Failures.Count > 0 ? $"{result.Failures[0].LogName}: {result.Failures[0].Error}" : "No files exported.";
                HasFailures = true;
            }
            HasStatus = true;
            ProgressText = SummaryText;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export cancelled.";
            StatusSeverity = "Warning";
            HasStatus = true;
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            StatusSeverity = "Error";
            HasStatus = true;
            ProgressText = "Failed";
        }
        finally
        {
            IsCollecting = false;
            _cts?.Dispose();
            _cts = null;
            CollectCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCollect() => !IsCollecting && !IsLoading;

    [RelayCommand]
    private void CancelCollect()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task OpenDestinationAsync()
    {
        try
        {
            if (!Directory.Exists(DestinationFolder))
            {
                StatusMessage = $"Folder does not exist: {DestinationFolder}";
                StatusSeverity = "Warning";
                HasStatus = true;
                return;
            }

            var folder = await StorageFolder.GetFolderFromPathAsync(DestinationFolder);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch
        {
            // Fallback to explorer
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{DestinationFolder}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Cannot open folder: {ex.Message}";
                StatusSeverity = "Error";
                HasStatus = true;
            }
        }
    }

    [RelayCommand]
    private async Task OpenEventViewerAsync()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "eventvwr.msc",
                UseShellExecute = true
            });
            await Task.CompletedTask;
        }
        catch { }
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        FilteredLogs.Clear();
        var source = string.IsNullOrEmpty(query)
            ? AllLogs
            : AllLogs.Where(l => l.LogName.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || l.LogType.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var log in source)
            FilteredLogs.Add(log);
        IsFilteredEmpty = FilteredLogs.Count == 0;
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        SelectedCount = AllLogs.Count(l => l.IsSelected);
        TotalCount = AllLogs.Count;
        IsFilteredEmpty = FilteredLogs.Count == 0;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    public string FilteredCountText => $"{FilteredLogs.Count} of {TotalCount}";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
}
