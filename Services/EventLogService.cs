using LogCollector.Models;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;

namespace LogCollector.Services;

public sealed class EventLogService
{
    /// <summary>
    /// The 5 channels under Event Viewer → Windows Logs.
    /// This app intentionally exports ONLY these — not the hundreds of
    /// Application and Services Logs (Microsoft-Windows-*, etc.).
    /// </summary>
    public static readonly HashSet<string> WindowsLogsSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application",
        "Security",
        "Setup",
        "System",
        "ForwardedEvents"
    };

    public static bool IsWindowsLog(string logName) => WindowsLogsSet.Contains(logName);

    public Task<List<LogChannelInfo>> GetAllLogsAsync()
    {
        return Task.Run(() =>
        {
            var result = new List<LogChannelInfo>();
            string[] logNames;
            try
            {
                logNames = EventLogSession.GlobalSession.GetLogNames()
                    .Where(IsWindowsLog)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetLogNames failed: {ex}");
                // Fallback to classic logs (already Windows Logs) — still filter to the 5.
                try
                {
                    logNames = System.Diagnostics.EventLog.GetEventLogs()
                        .Select(l => l.Log)
                        .Where(IsWindowsLog)
                        .ToArray();
                }
                catch
                {
                    return result;
                }
            }

            Array.Sort(logNames, StringComparer.OrdinalIgnoreCase);

            foreach (var name in logNames)
            {
                try
                {
                    var config = new EventLogConfiguration(name);
                    result.Add(new LogChannelInfo
                    {
                        LogName = name,
                        LogType = config.LogType.ToString(),
                        IsEnabled = config.IsEnabled,
                        IsClassicLog = config.IsClassicLog,
                        ProviderName = config.OwningProviderName ?? string.Empty,
                        RecordCount = TryGetRecordCount(config),
                        FileSizeBytes = TryGetFileSize(config),
                        IsSelected = IsDefaultSelected(name, config)
                    });
                }
                catch (EventLogNotFoundException)
                {
                    // Skip missing
                }
                catch (UnauthorizedAccessException)
                {
                    result.Add(new LogChannelInfo
                    {
                        LogName = name,
                        LogType = "Unknown",
                        IsEnabled = false,
                        IsClassicLog = false,
                        IsSelected = false
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to read config for {name}: {ex.Message}");
                    result.Add(new LogChannelInfo
                    {
                        LogName = name,
                        LogType = "Unknown",
                        IsEnabled = true,
                        IsSelected = false
                    });
                }
            }

            return result;
        });
    }

    public async Task<CollectResult> CollectLogsAsync(
        IEnumerable<LogChannelInfo> logs,
        string destinationFolder,
        IProgress<CollectProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool sanitizeFileNames = true)
    {
        var sw = Stopwatch.StartNew();
        var selected = logs.Where(l => l.IsSelected).ToList();
        var result = new CollectResult
        {
            TotalRequested = selected.Count,
            DestinationFolder = destinationFolder
        };

        Directory.CreateDirectory(destinationFolder);

        var session = EventLogSession.GlobalSession;
        var total = selected.Count;
        var index = 0;

        foreach (var log in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            var safeName = sanitizeFileNames ? MakeSafeFileName(log.LogName) : log.LogName;
            var destFile = Path.Combine(destinationFolder, safeName + ".evtx");

            // Handle duplicate names after sanitizing (e.g., slashes become _)
            destFile = GetUniquePath(destFile);

            try
            {
                // ExportLog with query "*": export all events
                // Use PathType.LogName so first param is interpreted as log name
                session.ExportLog(log.LogName, PathType.LogName, "*", destFile, false);
                // Some providers require tolerant query; if empty, file still created (0 events but header present)
                if (File.Exists(destFile))
                {
                    result.Succeeded++;
                    result.ExportedFiles.Add(destFile);
                    progress?.Report(new CollectProgress
                    {
                        CurrentIndex = index,
                        Total = total,
                        LogName = log.LogName,
                        Success = true
                    });
                }
                else
                {
                    // ExportLog may succeed but create 0-length? Treat as skipped
                    result.Skipped++;
                    progress?.Report(new CollectProgress
                    {
                        CurrentIndex = index,
                        Total = total,
                        LogName = log.LogName,
                        Success = false,
                        Error = "No file created (log empty or not exportable)"
                    });
                }
            }
            catch (EventLogException ex) when (ex.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped++;
                progress?.Report(new CollectProgress
                {
                    CurrentIndex = index,
                    Total = total,
                    LogName = log.LogName,
                    Success = false,
                    Error = "Log is empty"
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Failed++;
                result.Failures.Add((log.LogName, "Access denied — run as Administrator. " + ex.Message));
                progress?.Report(new CollectProgress
                {
                    CurrentIndex = index,
                    Total = total,
                    LogName = log.LogName,
                    Success = false,
                    Error = "Access denied"
                });
            }
            catch (EventLogException ex)
            {
                result.Failed++;
                result.Failures.Add((log.LogName, ex.Message));
                progress?.Report(new CollectProgress
                {
                    CurrentIndex = index,
                    Total = total,
                    LogName = log.LogName,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Failures.Add((log.LogName, ex.Message));
                progress?.Report(new CollectProgress
                {
                    CurrentIndex = index,
                    Total = total,
                    LogName = log.LogName,
                    Success = false,
                    Error = ex.Message
                });
            }

            // Small delay to keep UI responsive and allow cancellation
            await Task.Delay(10, cancellationToken);
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static long? TryGetRecordCount(EventLogConfiguration config)
    {
        try
        {
            var logInfo = EventLogSession.GlobalSession.GetLogInformation(config.LogName, PathType.LogName);
            return logInfo.RecordCount;
        }
        catch
        {
            return null;
        }
    }

    private static long? TryGetFileSize(EventLogConfiguration config)
    {
        try
        {
            var info = EventLogSession.GlobalSession.GetLogInformation(config.LogName, PathType.LogName);
            return info.FileSize;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDefaultSelected(string name, EventLogConfiguration config)
    {
        // Windows Logs only — select all classic Windows Logs by default.
        // ForwardedEvents is often disabled/empty but still selected so user sees it.
        // No Analytical/Debug filtering needed since we already limit to the 5.
        return true;
    }

    private static string MakeSafeFileName(string logName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Join("_", logName.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        // Replace path separators and other problematic chars
        safe = safe.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        if (string.IsNullOrWhiteSpace(safe)) safe = "_log";
        return safe;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            i++;
        } while (File.Exists(candidate));
        return candidate;
    }
}
