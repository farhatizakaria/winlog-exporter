using CommunityToolkit.Mvvm.ComponentModel;

namespace LogCollector.Models;

public sealed partial class LogChannelInfo : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    public string LogName { get; set; } = string.Empty;

    public string LogType { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsClassicLog { get; set; }

    public long? RecordCount { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? ProviderName { get; set; }

    public string FileSizeDisplay => FileSizeBytes is null ? "—" : FormatBytes(FileSizeBytes.Value);

    public string RecordCountDisplay => RecordCount is null ? "—" : RecordCount.Value.ToString("N0");

    public string StatusDisplay => IsEnabled ? "Enabled" : "Disabled";

    public string CategoryDisplay
    {
        get
        {
            if (IsClassicLog) return "Classic";
            return LogType switch
            {
                "Administrative" => "Admin",
                "Operational" => "Operational",
                "Analytical" => "Analytical",
                "Debug" => "Debug",
                _ => LogType
            };
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public sealed class CollectProgress
{
    public int CurrentIndex { get; set; }
    public int Total { get; set; }
    public string LogName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class CollectResult
{
    public int TotalRequested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<string> ExportedFiles { get; set; } = [];
    public List<(string LogName, string Error)> Failures { get; set; } = [];
    public string DestinationFolder { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}
