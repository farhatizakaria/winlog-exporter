# winlog-exporter

**WinUI 3 desktop app that exports *only* Windows Logs to `.evtx`.**

> Application • Security • Setup • System • ForwardedEvents — nothing else.

Built with WinUI 3 + Windows App SDK 1.8, packaged with MSIX. No `Microsoft-Windows-*` / `Application and Services Logs` noise — just the 5 channels under Event Viewer → **Windows Logs**, ready for archival, analysis or forensics.

![Windows 10+](https://img.shields.io/badge/Windows-10%2B-0078D4) ![WinUI 3](https://img.shields.io/badge/WinUI-3-512BD4) ![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)

### Features
- Enumerates only the 5 Windows Logs (filters `EventLogSession.GlobalSession.GetLogNames()` via `WindowsLogsSet`)
- One-click bulk export to `.evtx` (native `EventLogSession.ExportLog` with `PathType.LogName`, query `*`)
- Destination folder picker (`FolderPicker` + `Desktop\WindowsLogs_yyyyMMdd_HHmmss` default)
- Progress + cancel, admin detection (Security log requires elevation)
- Mica backdrop, dark/light/high-contrast via `ThemeResource`

### Screenshot
> Tip: Run as Administrator to export **Security**.

### Requirements
- Windows 10 1809+ (10.0.17763), Windows 11 recommended
- .NET SDK 10.0
- Developer Mode ON

### Build & Run
```powershell
# fastest (project-mode, with analyzer + stowed-exception triage)
.\BuildAndRun.ps1

# or plain winapp CLI
winapp run . --debug-output

# explicit project
dotnet build LogCollector.csproj -c Release
```

`BuildAndRun.ps1` is the `winui-dev-workflow` wrapper around `winapp run` (WinApp CLI 0.6+).

### How it works
`Services/EventLogService.cs`:
```csharp
public static readonly HashSet<string> WindowsLogsSet = new(StringComparer.OrdinalIgnoreCase)
{
    "Application", "Security", "Setup", "System", "ForwardedEvents"
};
logNames = EventLogSession.GlobalSession.GetLogNames().Where(IsWindowsLog)...
session.ExportLog(logName, PathType.LogName, "*", destFile, false);
```

### Project structure
```
LogCollector.csproj  # net10.0-windows10.0.26100, UseWinUI, Single-project MSIX
MainPage.xaml / ViewModels/MainPageViewModel.cs  # MVVM (CommunityToolkit.Mvvm)
Services/EventLogService.cs
Models/LogChannelInfo.cs
Helpers/AdminHelper.cs, Converters.cs
```

### License
MIT
