using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using RoninDiskManager.Engine;
using RoninDiskManager.Models;
using RoninDiskManager.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;

namespace RoninDiskManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScanEngine   _engine       = new();
    private readonly SearchEngine _searchEngine = new();

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;

    // ── Scan ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _scanPath = @"C:\";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "Enter a path and click Scan.";
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private bool _scanIndeterminate = true;
    [ObservableProperty] private ObservableCollection<DiskNodeViewModel> _treeRoots = [];
    [ObservableProperty] private DiskNodeViewModel? _selectedNode;
    [ObservableProperty] private string _consoleText = string.Empty;

    // ── Scan stats (populated after each scan) ────────────────────────────────
    private int _totalScannedFiles;
    private int _totalScannedDirs;

    // ── Treemap ───────────────────────────────────────────────────────────────
    /// <summary>Raw scan root forwarded to the TreemapControl for rendering.</summary>
    [ObservableProperty] private DiskNode? _treemapRoot;

    /// <summary>
    /// Written by TreemapControl via OneWayToSource when the user clicks a tile;
    /// bridges into the existing <see cref="SelectedNode"/> flow.
    /// </summary>
    [ObservableProperty] private DiskNode? _treemapSelectedNode;

    partial void OnTreemapSelectedNodeChanged(DiskNode? value)
    {
        if (value == null) return;
        long rootSize = TreemapRoot?.SizeBytes ?? 1;
        SelectedNode = new DiskNodeViewModel(value, Math.Max(1, rootSize));
    }

    // ── Search ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchResults = [];
    [ObservableProperty] private SearchResult? _selectedSearchResult;

    // ── Largest files (populated after each scan) ──────────────────────────────
    /// <summary>Number of biggest files surfaced in the Largest Files tab.</summary>
    private static int LargestFilesCount => SettingsService.Current.LargestFilesCount;
    [ObservableProperty] private ObservableCollection<SearchResult> _largestFiles = [];
    [ObservableProperty] private SearchResult? _selectedLargestFile;

    /// <summary>Free-space target (GB) used to mark cumulative rows in Largest Files.</summary>
    [ObservableProperty] private double _freeSpaceTargetGb = SettingsService.Current.FreeSpaceTargetGb;

    partial void OnFreeSpaceTargetGbChanged(double value)
    {
        SettingsService.Current.FreeSpaceTargetGb = value;
        SettingsService.Save();
        if (TreemapRoot != null) PopulateLargestFiles(TreemapRoot);
    }

    // ── Extension breakdown ─────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DiskAnalysis.ExtensionStat> _extensionStats = [];

    // ── Empty folders ───────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<SearchResult> _emptyFolders = [];
    [ObservableProperty] private SearchResult? _selectedEmptyFolder;

    // ── Duplicates ──────────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<SearchResult> _duplicateFiles = [];
    [ObservableProperty] private SearchResult? _selectedDuplicate;
    [ObservableProperty] private bool _isFindingDuplicates;
    [ObservableProperty] private string _duplicateStatus = "Scan a folder, then find duplicate files.";
    private CancellationTokenSource? _dupCts;

    // ── Age-based cleanup rules ─────────────────────────────────────────────────
    [ObservableProperty] private string _cleanupFolder = @"C:\";
    [ObservableProperty] private string _cleanupPattern = "*.log";
    [ObservableProperty] private int _cleanupOlderThanDays = 30;
    [ObservableProperty] private bool _cleanupRecurse = true;
    [ObservableProperty] private bool _cleanupRecycle = true;
    [ObservableProperty] private ObservableCollection<SearchResult> _cleanupResults = [];
    [ObservableProperty] private string _cleanupStatus = "Set a folder, pattern, and age, then Preview.";
    [ObservableProperty] private bool _isCleaning;
    private CancellationTokenSource? _cleanupCts;

    // ── Unified input bar ─────────────────────────────────────────────────────
    [ObservableProperty] private string _inputQuery = @"C:\";
    [ObservableProperty] private bool _isShowingSearchResults;

    // ── Pinned paths (persisted) ───────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _pinnedPaths = [];

    public MainViewModel()
    {
        var s = SettingsService.Current;
        foreach (var p in s.PinnedPaths)
            PinnedPaths.Add(p);
        CleanupFolder = ScanPath;
    }

    [RelayCommand]
    private void PinCurrentPath()
    {
        var path = InputQuery?.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (PinnedPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return;
        PinnedPaths.Add(path);
        PersistPinnedPaths();
    }

    [RelayCommand]
    private void UnpinPath(string? path)
    {
        if (path == null) return;
        PinnedPaths.Remove(path);
        PersistPinnedPaths();
    }

    [RelayCommand]
    private async Task ScanPinned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        InputQuery = path;
        await ScanAsync();
    }

    private void PersistPinnedPaths()
    {
        SettingsService.Current.PinnedPaths = [.. PinnedPaths];
        SettingsService.Save();
    }

    /// <summary>True while either a scan or search is in progress — drives Cancel button.</summary>
    public bool IsOperationRunning => IsScanning || IsSearching;

    // ── Status text (shown in top bar row 1) ──────────────────────────────────
    private string _lastStatus = "Enter a path and click Scan or Search.";
    public string StatusText => IsScanning ? ScanStatus : _lastStatus;

    // ── Status bar (bottom strip — selected item or scan summary) ─────────────
    public string StatusBarText
    {
        get
        {
            if (SelectedNode != null)
            {
                var n = SelectedNode;
                string sizeRaw = n.SizeBytes switch
                {
                    >= 1_073_741_824 => $"{n.SizeBytes / 1_073_741_824.0:F1} GB",
                    >= 1_048_576     => $"{n.SizeBytes / 1_048_576.0:F0} MB",
                    _                => $"{n.SizeBytes / 1024.0:F0} KB"
                };
                string pct    = TreemapRoot != null
                    ? $"  ·  {n.SizeBytes / (double)Math.Max(1, TreemapRoot.SizeBytes) * 100:F1}% of root"
                    : string.Empty;
                string kids   = n.IsDirectory ? $"  ·  {n.ChildCountText}" : string.Empty;
                return $"{n.Icon}  {n.FullPath}   ·   {sizeRaw}{pct}{kids}";
            }

            if (TreemapRoot != null)
            {
                string total = FormatBytes(TreemapRoot.SizeBytes);
                string stats = _totalScannedFiles > 0
                    ? $"  ·  {_totalScannedFiles:N0} files  ·  {_totalScannedDirs:N0} dirs"
                    : string.Empty;
                return $"📂  {TreemapRoot.FullPath}   ·   {total}{stats}";
            }

            return "Ready. Enter a path and click Scan.";
        }
    }

    // ── Action mode ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isMoveMode = true;
    [ObservableProperty] private bool _isCopyMode;
    [ObservableProperty] private bool _isDeleteMode;

    public string ExecuteButtonText =>
        IsMoveMode ? "▶   Execute Move"
      : IsCopyMode ? "▶   Execute Copy"
      : DeleteToRecycleBin ? "▶   Send to Recycle Bin"
      : "▶   Delete Permanently";

    /// <summary>
    /// Live preview of exactly what the current action will run, so the user can
    /// see the command before committing. Empty when nothing is selected.
    /// </summary>
    public string CommandPreview
    {
        get
        {
            if (SelectedNode == null) return string.Empty;
            if (IsMoveMode) return BuildMoveCommand();
            if (IsCopyMode) return BuildCopyCommand();
            if (IsDeleteMode)
                return DeleteToRecycleBin
                    ? $"Send to Recycle Bin:  {SelectedNode.FullPath}"
                    : BuildDeleteCommand();
            return string.Empty;
        }
    }

    // Recompute the command preview whenever any relevant property changes.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(CommandPreview) or nameof(ConsoleText) or nameof(StatusBarText)))
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CommandPreview)));
    }

    // ── Move flags ────────────────────────────────────────────────────────────
    // Destination is shared by both Move and Copy (only one mode is active).
    [ObservableProperty] private string _destination = string.Empty;
    [ObservableProperty] private bool _moveForce = true;
    [ObservableProperty] private bool _moveWhatIf;
    [ObservableProperty] private bool _moveVerbose = true;
    [ObservableProperty] private bool _moveNeverOverwrite;
    [ObservableProperty] private bool _moveLiteralPath;
    [ObservableProperty] private string _moveFilter = string.Empty;
    [ObservableProperty] private string _moveInclude = string.Empty;
    [ObservableProperty] private string _moveExclude = string.Empty;

    // ── Copy flags ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _copyForce = true;
    [ObservableProperty] private bool _copyRecurse = true;   // needed to copy folder trees
    [ObservableProperty] private bool _copyWhatIf;
    [ObservableProperty] private bool _copyVerbose = true;
    [ObservableProperty] private bool _copyLiteralPath;
    [ObservableProperty] private string _copyFilter = string.Empty;
    [ObservableProperty] private string _copyInclude = string.Empty;
    [ObservableProperty] private string _copyExclude = string.Empty;

    // ── Delete flags ──────────────────────────────────────────────────────────
    /// <summary>When true (default), Delete sends items to the Recycle Bin instead of erasing them.</summary>
    [ObservableProperty] private bool _deleteToRecycleBin = true;
    [ObservableProperty] private bool _deleteForce = true;
    [ObservableProperty] private bool _deleteRecurse = true;
    [ObservableProperty] private bool _deleteWhatIf;
    [ObservableProperty] private bool _deleteVerbose = true;
    [ObservableProperty] private bool _deleteLiteralPath;
    [ObservableProperty] private string _deleteFilter = string.Empty;
    [ObservableProperty] private string _deleteInclude = string.Empty;
    [ObservableProperty] private string _deleteExclude = string.Empty;

    partial void OnDeleteToRecycleBinChanged(bool value) => OnPropertyChanged(nameof(ExecuteButtonText));

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        // Sync unified bar → ScanPath, clear previous search state
        ScanPath = InputQuery;
        IsShowingSearchResults = false;
        SearchResults.Clear();

        IsScanning = true;
        ScanIndeterminate = true;
        ScanProgress = 0;
        OnPropertyChanged(nameof(IsOperationRunning));
        TreeRoots.Clear();
        LargestFiles.Clear();
        ExtensionStats.Clear();
        EmptyFolders.Clear();
        DuplicateFiles.Clear();
        DuplicateStatus = "Scan complete. Click Find Duplicates to scan for duplicate files.";
        SelectedNode = null;

        var progress = new Progress<string>(msg =>
        {
            ScanStatus = $"Scanning  {Path.GetFileName(msg) ?? msg}";
            OnPropertyChanged(nameof(StatusText));
        });

        AppendConsole($"▶  Scanning: {ScanPath}");
        var sw = Stopwatch.StartNew();

        var pct = new Progress<double>(v =>
        {
            ScanIndeterminate = false;
            ScanProgress = v * 100;
        });

        try
        {
            var root = await _engine.ScanAsync(ScanPath, AppendConsole, progress, _scanCts.Token, pct);
            sw.Stop();

            // Count files and dirs for status bar
            CountNodes(root, out _totalScannedFiles, out _totalScannedDirs);

            TreeRoots.Add(new DiskNodeViewModel(root, root.SizeBytes));
            TreemapRoot = root;
            PopulateLargestFiles(root);
            PopulateExtensionStats(root);
            PopulateEmptyFolders(root);
            ScanStatus = $"Done. {FormatBytes(root.SizeBytes)} in {sw.Elapsed.TotalSeconds:F1}s  ·  {_totalScannedFiles:N0} files  ·  {_totalScannedDirs:N0} dirs";
            AppendConsole($"✔  Scan complete in {sw.Elapsed.TotalSeconds:F1}s  {FormatBytes(root.SizeBytes)} total\n");
            _lastStatus = ScanStatus;
            OnPropertyChanged(nameof(StatusBarText));
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
            _lastStatus = ScanStatus;
            AppendConsole("✖  Scan cancelled.\n");
        }
        catch (Exception ex)
        {
            ScanStatus = $"Error: {ex.Message}";
            _lastStatus = ScanStatus;
            AppendConsole($"✖  {ex.Message}\n");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        _searchCts?.Cancel();
    }

    [RelayCommand]
    private void BrowseScanPath()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            ScanPath = dlg.FolderName;
            InputQuery = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            Destination = dlg.FolderName;
    }

    [RelayCommand]
    private void SetMoveMode()
    {
        IsMoveMode = true;
        IsCopyMode = false;
        IsDeleteMode = false;
        OnPropertyChanged(nameof(ExecuteButtonText));
    }

    [RelayCommand]
    private void SetCopyMode()
    {
        IsCopyMode = true;
        IsMoveMode = false;
        IsDeleteMode = false;
        OnPropertyChanged(nameof(ExecuteButtonText));
    }

    [RelayCommand]
    private void SetDeleteMode()
    {
        IsDeleteMode = true;
        IsMoveMode = false;
        IsCopyMode = false;
        OnPropertyChanged(nameof(ExecuteButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task ExecuteActionAsync()
    {
        if (SelectedNode == null) return;

        if (IsMoveMode)
        {
            if (string.IsNullOrWhiteSpace(Destination))
            {
                AppendConsole("✖  Destination is required for Move.\n");
                return;
            }

            if (MoveNeverOverwrite)
            {
                var destPath = Path.Combine(Destination, Path.GetFileName(SelectedNode.FullPath));
                if (Path.Exists(destPath))
                {
                    AppendConsole($"✖  Aborted: '{destPath}' already exists and NeverOverwrite is enabled.\n");
                    return;
                }
            }

            await RunPowerShellAsync(BuildMoveCommand(), auditAction: "MOVE");
        }
        else if (IsCopyMode)
        {
            if (string.IsNullOrWhiteSpace(Destination))
            {
                AppendConsole("✖  Destination is required for Copy.\n");
                return;
            }

            await RunPowerShellAsync(BuildCopyCommand(), auditAction: "COPY");
        }
        else
        {
            await ExecuteDeleteAsync();
        }
    }

    private async Task ExecuteDeleteAsync()
    {
        if (SelectedNode == null) return;

        bool recycle = DeleteToRecycleBin;

        // The classic Recycle Bin API can't address extended-length paths.
        if (recycle && FileSystemHelpers.ExceedsLegacyMaxPath(SelectedNode.FullPath))
        {
            AppendConsole("⚠  Path exceeds 260 characters; Recycle Bin is unavailable. Use permanent delete for this item.\n");
            return;
        }

        string prompt = recycle
            ? $"Send to Recycle Bin:\n\n{SelectedNode.FullPath}\n\nYou can restore it from the Recycle Bin later."
            : $"Permanently delete:\n\n{SelectedNode.FullPath}\n\nThis cannot be undone.";

        var confirm = MessageBox.Show(
            prompt,
            recycle ? "Confirm Recycle" : "Confirm Permanent Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return;

        if (recycle)
            await RecycleSelectedAsync();
        else
            await RunPowerShellAsync(BuildDeleteCommand(), auditAction: "DELETE");
    }

    private async Task RecycleSelectedAsync()
    {
        var path = SelectedNode!.FullPath;
        AppendConsole($"> Recycle '{path}'\n");
        var (ok, error) = await Task.Run(() =>
        {
            bool success = RecycleBin.Send(path, out var err);
            return (success, err);
        });
        AppendConsole(ok
            ? "\n✔  Sent to Recycle Bin.\n"
            : $"\n✖  {error}\n");
        OperationLog.Record("RECYCLE", path, ok ? "OK" : error);
    }

    private bool CanExecuteAction() => SelectedNode != null;

    partial void OnSelectedNodeChanged(DiskNodeViewModel? value)
    {
        ExecuteActionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusBarText));
    }

    partial void OnSelectedSearchResultChanged(SearchResult? value) => SelectFromResult(value);

    partial void OnSelectedLargestFileChanged(SearchResult? value) => SelectFromResult(value);

    partial void OnSelectedEmptyFolderChanged(SearchResult? value) => SelectFromResult(value);

    partial void OnSelectedDuplicateChanged(SearchResult? value) => SelectFromResult(value);

    /// <summary>Bridges a grid-row selection (search or largest-files) into the action panel.</summary>
    private void SelectFromResult(SearchResult? value)
    {
        if (value == null) return;
        var node = new DiskNode
        {
            Name        = value.Name,
            FullPath    = value.FullPath,
            IsDirectory = value.IsDirectory,
            SizeBytes   = value.SizeBytes
        };
        SelectedNode = new DiskNodeViewModel(node, value.SizeBytes > 0 ? value.SizeBytes : 1);
    }

    // ── Search commands ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchAllDrivesAsync()
    {
        // Cancel any in-flight search first
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        var query = InputQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            _lastStatus = "Please enter a search term.";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        IsSearching = true;
        IsShowingSearchResults = true;
        OnPropertyChanged(nameof(IsOperationRunning));
        SearchResults.Clear();
        SelectedNode = null;
        _lastStatus = $"Searching all drives for  \"{query}\"…";
        OnPropertyChanged(nameof(StatusText));
        AppendConsole($"🔍  Search started: \"{query}\"\n");

        var progress = new Progress<string>(msg =>
        {
            _lastStatus = msg;
            OnPropertyChanged(nameof(StatusText));
            AppendConsole($"    {msg}\n");
        });

        var sw = Stopwatch.StartNew();

        try
        {
            var results = await _searchEngine.SearchAsync(query, progress, ct);
            sw.Stop();

            // Populate observable collection on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var r in results)
                    SearchResults.Add(r);
            });

            _lastStatus = results.Count == 0
                ? $"No results found for  \"{query}\"."
                : $"{results.Count:N0} result{(results.Count == 1 ? "" : "s")} for  \"{query}\"  ({sw.Elapsed.TotalSeconds:F1}s)";
            AppendConsole($"✔  {_lastStatus}\n");
        }
        catch (OperationCanceledException)
        {
            _lastStatus = "Search cancelled.";
            AppendConsole("✖  Search cancelled.\n");
        }
        catch (Exception ex)
        {
            _lastStatus = $"Search error: {ex.Message}";
            AppendConsole($"✖  Search error: {ex.Message}\n");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void CancelSearch() => _searchCts?.Cancel();

    // ── Command builders ──────────────────────────────────────────────────────

    private string BuildMoveCommand() => ShellCommandBuilder.BuildMove(new ShellCommandBuilder.FileOpOptions
    {
        SourcePath  = SelectedNode!.FullPath,
        Destination = Destination,
        Force       = MoveForce,
        Verbose     = MoveVerbose,
        WhatIf      = MoveWhatIf,
        LiteralPath = MoveLiteralPath,
        Filter      = MoveFilter,
        Include     = MoveInclude,
        Exclude     = MoveExclude,
    });

    private string BuildCopyCommand() => ShellCommandBuilder.BuildCopy(new ShellCommandBuilder.FileOpOptions
    {
        SourcePath  = SelectedNode!.FullPath,
        Destination = Destination,
        Force       = CopyForce,
        Recurse     = CopyRecurse,
        Verbose     = CopyVerbose,
        WhatIf      = CopyWhatIf,
        LiteralPath = CopyLiteralPath,
        Filter      = CopyFilter,
        Include     = CopyInclude,
        Exclude     = CopyExclude,
    });

    private string BuildDeleteCommand() => ShellCommandBuilder.BuildDelete(new ShellCommandBuilder.FileOpOptions
    {
        SourcePath  = SelectedNode!.FullPath,
        Force       = DeleteForce,
        Recurse     = DeleteRecurse,
        Verbose     = DeleteVerbose,
        WhatIf      = DeleteWhatIf,
        LiteralPath = DeleteLiteralPath,
        Filter      = DeleteFilter,
        Include     = DeleteInclude,
        Exclude     = DeleteExclude,
    });

    // ── Execution ─────────────────────────────────────────────────────────────

    private async Task RunPowerShellAsync(string command, string? displayName = null, string? auditAction = null)
    {
        AppendConsole($"> {displayName ?? command}\n");

        // Use EncodedCommand to avoid quoting issues
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)!;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendConsole(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendConsole(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            AppendConsole(proc.ExitCode == 0
                ? "\n✔  Operation completed successfully.\n"
                : $"\n✖  Exited with code {proc.ExitCode}.\n");
            if (auditAction != null)
                OperationLog.Record(auditAction, command, proc.ExitCode == 0 ? "OK" : $"exit {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            AppendConsole($"\n✖  Failed to start PowerShell: {ex.Message}\n");
            if (auditAction != null)
                OperationLog.Record(auditAction, command, $"error: {ex.Message}");
        }
    }

    // ── Context-menu commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void OpenInExplorer()
    {
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        // For files, open the containing folder and select the file
        string args = SelectedNode!.IsDirectory
            ? $"\"{path}\""
            : $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPath()
    {
        var path = SelectedNode?.FullPath;
        if (!string.IsNullOrEmpty(path))
            Clipboard.SetText(path);
    }

    [RelayCommand]
    private void OpenTerminal()
    {
        var path = SelectedNode?.FullPath;
        if (string.IsNullOrEmpty(path)) return;
        string dir = SelectedNode!.IsDirectory ? path : (Path.GetDirectoryName(path) ?? path);
        try
        {
            // Prefer Windows Terminal; fall back to PowerShell if it isn't installed.
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = dir, UseShellExecute = true }); }
            catch (Exception ex) { AppendConsole($"✖  Could not open a terminal: {ex.Message}\n"); }
        }
    }

    /// <summary>Delete-key shortcut: switch to Delete mode and run it (with confirmation).</summary>
    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (SelectedNode == null) return;
        SetDeleteMode();
        await ExecuteDeleteAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AppendConsole(string text)
        => Application.Current.Dispatcher.BeginInvoke(() => ConsoleText += text + "\n");

    /// <summary>Counts files and directories in the scan tree iteratively.</summary>
    private static void CountNodes(DiskNode root, out int files, out int dirs)
    {
        files = 0; dirs = 0;
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsDirectory) dirs++; else files++;
            foreach (var c in n.Children) stack.Push(c);
        }
    }

    private static string FormatBytes(long bytes) => FileSystemHelpers.FormatBytes(bytes);

    /// <summary>
    /// Collects the biggest files in the scan tree and fills the Largest Files
    /// tab. Timestamps are fetched only for the top N (a few hundred cheap stat
    /// calls), not for the whole tree.
    /// </summary>
    private void PopulateLargestFiles(DiskNode root)
    {
        var top = new List<DiskNode>();
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsDirectory && n.SizeBytes > 0) top.Add(n);
            foreach (var c in n.Children) stack.Push(c);
        }

        var biggest = top.OrderByDescending(n => n.SizeBytes).Take(LargestFilesCount).ToList();

        long targetBytes = (long)(FreeSpaceTargetGb * 1024 * 1024 * 1024);
        long cumulative = 0;

        LargestFiles.Clear();
        foreach (var n in biggest)
        {
            DateTime modified = DateTime.MinValue;
            try { modified = new FileInfo(n.FullPath).LastWriteTime; } catch { /* metadata unavailable */ }

            cumulative += n.SizeBytes;

            var ext = Path.GetExtension(n.Name);
            LargestFiles.Add(new SearchResult
            {
                Name         = n.Name,
                FullPath     = n.FullPath,
                IsDirectory  = false,
                SizeBytes    = n.SizeBytes,
                DateModified = modified,
                FileType     = string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " File",
                CumulativeBytes = cumulative,
                // Rows up to and including the one that first reaches the target are "within target".
                WithinFreeSpaceTarget = targetBytes > 0 && cumulative - n.SizeBytes < targetBytes
            });
        }
    }

    private void PopulateExtensionStats(DiskNode root)
    {
        ExtensionStats.Clear();
        foreach (var s in DiskAnalysis.ExtensionBreakdown(root))
            ExtensionStats.Add(s);
    }

    private void PopulateEmptyFolders(DiskNode root)
    {
        EmptyFolders.Clear();
        foreach (var n in DiskAnalysis.FindEmptyFolders(root))
            EmptyFolders.Add(MakeResult(n));
    }

    /// <summary>Builds a display row from a scan node (used for empty folders and duplicates).</summary>
    private static SearchResult MakeResult(DiskNode n)
    {
        var ext = Path.GetExtension(n.Name);
        return new SearchResult
        {
            Name        = n.Name,
            FullPath    = n.FullPath,
            IsDirectory = n.IsDirectory,
            SizeBytes   = n.SizeBytes,
            FileType    = n.IsDirectory
                ? "File Folder"
                : (string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " File")
        };
    }

    // ── Duplicate finder ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task FindDuplicates()
    {
        var root = TreemapRoot;
        if (root == null) { DuplicateStatus = "Scan a folder first."; return; }
        if (IsFindingDuplicates) return;

        _dupCts?.Cancel();
        _dupCts = new CancellationTokenSource();

        IsFindingDuplicates = true;
        DuplicateFiles.Clear();
        DuplicateStatus = "Scanning for duplicate files...";
        var progress = new Progress<string>(m => DuplicateStatus = m);

        try
        {
            var groups = await DuplicateFinder.FindDuplicatesAsync(root, progress, _dupCts.Token);
            long wasted = groups.Sum(g => g.WastedBytes);
            int fileCount = 0;
            foreach (var g in groups)
                foreach (var f in g.Files)
                {
                    DuplicateFiles.Add(MakeResult(f));
                    fileCount++;
                }

            DuplicateStatus = groups.Count == 0
                ? "No duplicate files found."
                : $"{groups.Count:N0} duplicate set{(groups.Count == 1 ? "" : "s")}  ·  {fileCount:N0} files  ·  {FileSystemHelpers.FormatBytes(wasted)} reclaimable";
        }
        catch (OperationCanceledException)
        {
            DuplicateStatus = "Duplicate scan cancelled.";
        }
        catch (Exception ex)
        {
            DuplicateStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsFindingDuplicates = false;
        }
    }

    [RelayCommand]
    private void CancelDuplicates() => _dupCts?.Cancel();

    // ── Age-based cleanup rules ─────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseCleanupFolder()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            CleanupFolder = dlg.FolderName;
    }

    /// <summary>Dry run: list the files the rule would remove, without touching anything.</summary>
    [RelayCommand]
    private async Task PreviewCleanup()
    {
        if (IsCleaning) return;
        if (string.IsNullOrWhiteSpace(CleanupFolder) || !Directory.Exists(CleanupFolder))
        {
            CleanupStatus = "Folder not found.";
            return;
        }

        _cleanupCts?.Cancel();
        _cleanupCts = new CancellationTokenSource();

        IsCleaning = true;
        CleanupResults.Clear();
        CleanupStatus = "Scanning...";
        var progress = new Progress<string>(m => CleanupStatus = m);

        try
        {
            var matches = await CleanupScanner.FindAsync(
                CleanupFolder, CleanupPattern, CleanupOlderThanDays, CleanupRecurse, progress, _cleanupCts.Token);

            long total = matches.Sum(m => m.SizeBytes);
            foreach (var m in matches) CleanupResults.Add(m);

            CleanupStatus = matches.Count == 0
                ? "No matching files. Nothing to clean."
                : $"{matches.Count:N0} file{(matches.Count == 1 ? "" : "s")}  ·  {FileSystemHelpers.FormatBytes(total)}  ·  review, then Apply.";
        }
        catch (OperationCanceledException) { CleanupStatus = "Cleanup preview cancelled."; }
        catch (Exception ex)               { CleanupStatus = $"Error: {ex.Message}"; }
        finally { IsCleaning = false; }
    }

    /// <summary>Acts on exactly the previewed files (recycle by default), after confirmation.</summary>
    [RelayCommand]
    private async Task ApplyCleanup()
    {
        if (IsCleaning) return;
        if (CleanupResults.Count == 0)
        {
            CleanupStatus = "Nothing to clean. Run Preview first.";
            return;
        }

        var files = CleanupResults.ToList();
        long total = files.Sum(f => f.SizeBytes);
        bool recycle = CleanupRecycle;

        var confirm = MessageBox.Show(
            (recycle
                ? $"Send {files.Count:N0} file(s) ({FileSystemHelpers.FormatBytes(total)}) to the Recycle Bin?\n\nThey can be restored later."
                : $"Permanently delete {files.Count:N0} file(s) ({FileSystemHelpers.FormatBytes(total)})?\n\nThis cannot be undone.")
            + $"\n\nRule: {CleanupPattern} older than {CleanupOlderThanDays} days in\n{CleanupFolder}",
            recycle ? "Confirm Cleanup (Recycle)" : "Confirm Cleanup (Permanent)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        IsCleaning = true;
        try
        {
            if (recycle)
            {
                var progress = new Progress<string>(m => CleanupStatus = m);
                int ok = await Task.Run(() =>
                {
                    int success = 0;
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (RecycleBin.Send(files[i].FullPath, out var err))
                        {
                            success++;
                            OperationLog.Record("CLEANUP-RECYCLE", files[i].FullPath, "OK");
                        }
                        else
                        {
                            OperationLog.Record("CLEANUP-RECYCLE", files[i].FullPath, err);
                        }
                        ((IProgress<string>)progress).Report($"Recycling {i + 1:N0} / {files.Count:N0}...");
                    }
                    return success;
                });
                AppendConsole($"✔  Cleanup: sent {ok:N0} / {files.Count:N0} file(s) to the Recycle Bin.\n");
                CleanupStatus = $"Done. {ok:N0} / {files.Count:N0} file(s) recycled.";
            }
            else
            {
                var quoted = string.Join(",", files.Select(f => $"'{ShellCommandBuilder.EscapePs(f.FullPath)}'"));
                await RunPowerShellAsync($"Remove-Item -LiteralPath {quoted} -Force", "Cleanup: permanent delete", "CLEANUP-DELETE");
                CleanupStatus = $"Done. Deleted {files.Count:N0} file(s).";
            }

            CleanupResults.Clear();
        }
        finally { IsCleaning = false; }
    }

    [RelayCommand]
    private void CancelCleanup() => _cleanupCts?.Cancel();

    // ── System Tools ──────────────────────────────────────────────────────────

    /// <summary>Ready drives available for CHKDSK selection.</summary>
    public IReadOnlyList<string> AvailableDrives { get; } =
        DriveInfo.GetDrives()
                 .Where(d => { try { return d.IsReady; } catch { return false; } })
                 .Select(d => d.Name.TrimEnd('\\'))
                 .ToList();

    [ObservableProperty] private string _chkdskDrive =
        DriveInfo.GetDrives()
                 .Where(d => { try { return d.IsReady; } catch { return false; } })
                 .Select(d => d.Name.TrimEnd('\\'))
                 .FirstOrDefault() ?? "C:";

    [ObservableProperty] private bool _chkdskFixErrors   = true;
    [ObservableProperty] private bool _chkdskBadSectors;
    [ObservableProperty] private string _dismSource = string.Empty;

    // ── CHKDSK ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunChkdskAsync()
    {
        if (string.IsNullOrWhiteSpace(ChkdskDrive)) return;
        string drive = ChkdskDrive.TrimEnd('\\');

        // Build flags — /r implies /f, so prefer /r if both ticked
        string flags = ChkdskBadSectors ? " /r /f" : ChkdskFixErrors ? " /f" : string.Empty;

        if (!string.IsNullOrEmpty(flags))
        {
            string? sysRoot = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.System));
            bool isSystemDrive = sysRoot != null &&
                drive.Equals(sysRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

            if (isSystemDrive)
            {
                var confirm = MessageBox.Show(
                    $"Drive {drive} is the system drive and is currently in use by Windows.\n\n" +
                    "CHKDSK will be scheduled to run automatically before the next startup. " +
                    "No data will be affected. Windows handles this safely.\n\nContinue?",
                    "Schedule Disk Check on Restart",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information,
                    MessageBoxResult.Yes);
                if (confirm != MessageBoxResult.Yes) return;
            }
        }

        // 'Y' | auto-confirms the "schedule on next boot?" prompt for system drives
        string cmd = string.IsNullOrEmpty(flags)
            ? $"chkdsk {drive}"
            : $"'Y' | chkdsk {drive}{flags}";

        await RunSystemToolAsync(cmd, $"CHKDSK {drive}{flags.Trim()}");
    }

    [RelayCommand]
    private Task RunChkdskReadOnlyAsync()
        => RunSystemToolAsync($"chkdsk {ChkdskDrive.TrimEnd('\\')}", $"CHKDSK {ChkdskDrive.TrimEnd('\\')} (read-only)");

    // ── SFC ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RunSfcScanAsync()
        => RunSystemToolAsync("sfc /scannow", "SFC: Scan & Repair System Files");

    [RelayCommand]
    private Task RunSfcVerifyAsync()
        => RunSystemToolAsync("sfc /verifyonly", "SFC: Verify Only (no repairs)");

    // ── DISM ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RunDismCheckAsync()
        => RunSystemToolAsync("DISM /Online /Cleanup-Image /CheckHealth", "DISM: CheckHealth");

    [RelayCommand]
    private Task RunDismScanAsync()
        => RunSystemToolAsync("DISM /Online /Cleanup-Image /ScanHealth", "DISM: ScanHealth");

    [RelayCommand]
    private async Task RunDismRestoreAsync()
    {
        string sourceArg = string.IsNullOrWhiteSpace(DismSource)
            ? string.Empty
            : $" /Source:\"{DismSource.Trim()}\" /LimitAccess";

        if (string.IsNullOrWhiteSpace(DismSource))
        {
            var confirm = MessageBox.Show(
                "No source path specified.\n\n" +
                "DISM will contact Windows Update to download repair files. " +
                "This may use several hundred MB of data and can take 10–20 minutes.\n\n" +
                "Tip: You can specify a local source (e.g. mounted ISO or WIM) to avoid the download.\n\nContinue?",
                "DISM: Restore Health",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);
            if (confirm != MessageBoxResult.Yes) return;
        }

        await RunSystemToolAsync(
            $"DISM /Online /Cleanup-Image /RestoreHealth{sourceArg}",
            "DISM: RestoreHealth");
    }

    [RelayCommand]
    private async Task RunDismCleanupAsync()
    {
        var confirm = MessageBox.Show(
            "Component cleanup will remove superseded Windows updates and reduce the size of the WinSxS folder.\n\n" +
            "⚠  The /ResetBase flag permanently removes old update backups. You will not be able to uninstall " +
            "those updates afterward.\n\nContinue?",
            "DISM: Component Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        await RunSystemToolAsync(
            "DISM /Online /Cleanup-Image /StartComponentCleanup /ResetBase",
            "DISM: Component Cleanup");
    }

    [RelayCommand]
    private void BrowseDismSource()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            DismSource = dlg.FolderName;
    }

    // ── Network ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RunFlushDnsAsync()
        => RunSystemToolAsync("ipconfig /flushdns", "Flush DNS Cache");

    [RelayCommand]
    private async Task RunRenewIpAsync()
    {
        await RunSystemToolAsync("ipconfig /release", "Release IP Address");
        await RunSystemToolAsync("ipconfig /renew",   "Renew IP Address");
    }

    [RelayCommand]
    private async Task RunWinsockResetAsync()
    {
        var confirm = MessageBox.Show(
            "Winsock reset restores Windows networking to its default state. " +
            "This can fix connectivity issues caused by corrupted network settings.\n\n" +
            "⚠  A system reboot is required for the reset to take effect.\n\nContinue?",
            "Reset Winsock",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        await RunSystemToolAsync("netsh winsock reset", "Winsock Reset");
    }

    [RelayCommand]
    private async Task RunTcpIpResetAsync()
    {
        var confirm = MessageBox.Show(
            "TCP/IP reset removes all manual IP configuration and restores defaults. " +
            "Use this if you have persistent connection issues after other fixes have failed.\n\n" +
            "⚠  A system reboot is required.\n\nContinue?",
            "Reset TCP/IP Stack",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        await RunSystemToolAsync("netsh int ip reset", "TCP/IP Stack Reset");
    }

    // ── Maintenance ───────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RunDiskCleanupAsync()
    {
        AppendConsole("▶  Launching Disk Cleanup…\n");
        try
        {
            Process.Start(new ProcessStartInfo("cleanmgr.exe") { UseShellExecute = true });
            AppendConsole("✔  Disk Cleanup launched.\n");
        }
        catch (Exception ex) { AppendConsole($"✖  {ex.Message}\n"); }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RunMemoryDiagAsync()
    {
        var confirm = MessageBox.Show(
            "Windows Memory Diagnostic will test your RAM for errors.\n\n" +
            "⚠  Your computer will restart immediately to run the test. " +
            "Save all open work before continuing.\n\nRestart and run test now?",
            "Windows Memory Diagnostic",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        AppendConsole("▶  Scheduling Windows Memory Diagnostic…\n");
        try
        {
            Process.Start(new ProcessStartInfo("mdsched.exe") { UseShellExecute = true });
            AppendConsole("✔  Memory Diagnostic scheduled, restarting.\n");
        }
        catch (Exception ex) { AppendConsole($"✖  {ex.Message}\n"); }
        await Task.CompletedTask;
    }

    // ── System tool runner ────────────────────────────────────────────────────

    /// <summary>
    /// Runs a command through PowerShell (with UTF-8 output encoding enforced so
    /// system tools like SFC and DISM don't produce garbled console output).
    /// </summary>
    private Task RunSystemToolAsync(string command, string displayName)
    {
        AppendConsole($"\n── {displayName} ─────────────────────────\n");
        // chcp 65001 switches the console code page to UTF-8 so SFC/DISM output renders cleanly
        return RunPowerShellAsync($"chcp 65001 | Out-Null; {command}", displayName);
    }
}
