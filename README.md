<p align="center">
  <img src="RoninDiskManager/Assets/icon.png" width="120" alt="Ronin Disk Manager" />
</p>

<h1 align="center">Ronin Disk Manager</h1>

<p align="center">
  A fast, NTFS-native disk usage analyzer, file search, and bulk file manager for Windows.<br/>
  Built for the <strong>Ronin Empire</strong> community.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-blue"/>
  <img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0-red"/>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-purple"/>
</p>

---

## Overview

Ronin Disk Manager scans your drives, visualizes directory sizes in a tree view and a treemap, searches every fixed drive by filename, and runs bulk Move or Delete operations on selected items. It also bundles a set of Windows maintenance tools (CHKDSK, SFC, DISM, network resets, and more). All file operations and system tools are backed by PowerShell with real-time console output.

On NTFS drives it reads the Master File Table directly via the USN journal (the same technique WizTree uses), giving near-instant enumeration regardless of how many files are on the volume. Non-NTFS volumes (FAT32, exFAT, USB, network shares) fall back to a fast recursive directory walker automatically.

---

## Features

### Scanning and visualization

- MFT-based scanning: USN journal enumeration on NTFS for fast, complete disk analysis.
- Automatic fallback: standard directory walker for FAT32, exFAT, and network drives, used automatically when MFT access is unavailable.
- Tree view: lazy-loading, virtualized, sorted by size, with a proportion bar and percentage of the root total on every row.
- Treemap view: squarified treemap visualization with multi-level drill-down, similar to WinDirStat, with an optional age heatmap that colors file tiles from green (recent) to red (old).
- Largest files view: a flat, sortable list of the biggest files found in the scan, with a running cumulative total and a free-space target so you can see which files together add up to the space you need.
- Scan summary and progress: total size, elapsed time, and file and directory counts after every scan, with a live progress bar during the scan.
- Cancellable: stop any scan or search in progress from the toolbar.

### File search

- System-wide search across all fixed drives at once, with each drive walked in parallel.
- Wildcard patterns (`*` and `?`) or plain substring matching, both case-insensitive.
- Results grid showing name, type, size, date modified, and full path.
- Runs without administrator rights: directories it cannot read are skipped instead of aborting the search.

### File operations

- Move operations: PowerShell `Move-Item` with Force, WhatIf, Verbose, NeverOverwrite, LiteralPath, and Filter, Include, and Exclude patterns.
- Copy operations: PowerShell `Copy-Item` with Recurse, Force, WhatIf, Verbose, LiteralPath, and Filter, Include, and Exclude patterns.
- Delete operations: send items to the Recycle Bin (the recommended default, recoverable), or delete permanently with `Remove-Item` using Force, Recurse, WhatIf, Verbose, LiteralPath, and Filter, Include, and Exclude patterns.
- Dry-run mode: the WhatIf flag on Move, Copy, and permanent Delete lets you preview changes before committing.
- Command preview: the exact PowerShell command for the current action is shown live before you run it.
- Confirmation before every delete, and an abort-if-destination-exists check for moves.
- Operation log: every Move, Copy, Delete, and Recycle is appended to an audit log under %AppData%\RoninDiskManager.
- Real-time console: live PowerShell stdout and stderr streamed to the in-app console, with exit-code reporting.

### Analysis and cleanup

- Extension breakdown: total size and file count per file extension, sorted by size.
- Empty-folders finder: lists empty folders so you can review and recycle them.
- Duplicate finder: finds byte-for-byte duplicate files (grouped by size first, then confirmed by SHA-256 hash) and reports how much space they waste.
- Age-based cleanup rules: remove files by rule (for example, logs older than 30 days under a folder). Always previews the exact file list first, defaults to the Recycle Bin, and confirms before acting.

### Windows tools

- Disk Health (CHKDSK): pick a ready drive, choose Fix errors (`/f`) or Locate bad sectors (`/r`), or run a read-only scan. Checks on the system drive are scheduled for the next restart automatically.
- System Integrity (SFC): `sfc /scannow` to scan and repair, or `sfc /verifyonly` to check without changes.
- Windows Image (DISM): CheckHealth, ScanHealth, RestoreHealth (with an optional local WIM or ISO source), and component cleanup.
- Network: flush DNS, release and renew IP, reset Winsock, and reset the TCP/IP stack.
- Maintenance: launch Windows Disk Cleanup and Windows Memory Diagnostic.
- Every destructive tool shows a confirmation dialog and an inline warning first.

### Interface

- Context menus: right-click any tree node, treemap tile, or grid row to open in Explorer, copy the path, open a terminal there, move, copy, or delete.
- Pinned paths: pin common roots (for example, a game-server folder) for one-click rescans.
- Drag and drop: drop a folder onto the window to scan it.
- Status bar: live display of the selected item path, size, percentage of root, and child count, or the scan summary when nothing is selected.
- Keyboard shortcuts: `Ctrl+E` open in Explorer, `Ctrl+Shift+C` copy the path, `F5` rescan, `Ctrl+F` focus the input box, `Delete` recycle the selected item.
- Size units: choose binary (1 KB = 1024 bytes) or decimal (1 KB = 1000 bytes); the preference is saved.
- Resizable layout: drag the splitters to resize the tree, action panel, and console.
- Ronin theme: dark crimson, orange, cyan, and black color scheme.

### Reliability

- Junction and symlink safe: scanning and search never follow reparse points, so they cannot loop forever or double-count storage that physically lives elsewhere.
- Long-path aware: the app opts in to Windows long-path support so scanning and enumeration handle paths beyond the legacy 260-character limit.
- Unit tested: the command builders, file-system helpers, analysis, and cleanup logic are covered by an xUnit test project (`RoninDiskManager.Tests`).

---

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 / 11 |
| Runtime | [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Privileges | Administrator (required for MFT access, file operations, and most Windows tools) |
| Filesystem | NTFS for MFT scanning; FAT32/exFAT/network supported via fallback |

---

## Getting Started

### Download and run (easiest)

1. Go to the [Releases](https://github.com/PhonicSpider/Ronin-Disk-Manager/releases/latest) page.
2. Download `RoninDiskManager.exe` under the latest release.
3. Right-click and choose Run as Administrator.

No installation or .NET runtime needed. The exe is self-contained.

---

### Run from source (for local changes)

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone the repository
git clone https://github.com/PhonicSpider/Ronin-Disk-Manager.git
cd RoninDiskManager

# Run (builds automatically)
dotnet run --project RoninDiskManager/RoninDiskManager.csproj
```

> Run your terminal as Administrator, or Windows will prompt for elevation on launch.

### Build your own standalone executable

```powershell
dotnet publish RoninDiskManager/RoninDiskManager.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Output: `RoninDiskManager/bin/Release/net8.0-windows/win-x64/publish/RoninDiskManager.exe`

---

## How It Works

1. Scan: Enter a path and click Scan. On NTFS, the engine opens the raw volume (`\\.\C:`), streams all MFT records via `FSCTL_ENUM_USN_DATA`, resolves full paths, and populates file sizes in a parallel directory-batched pass. On other filesystems it falls back to a recursive `EnumerateFileSystemInfos` walk.
2. Search: Enter a name or pattern and click Search to scan every fixed drive in parallel. Results appear in a sortable grid.
3. Browse: The tree view shows directories sorted by size. Expand any node to see its children, switch to the treemap for a visual overview, or open the Largest Files tab to jump straight to the biggest items.
4. Act: Select a node, choose Move, Copy, or Delete, configure flags and filters, then click Execute. Deletes go to the Recycle Bin by default and always ask for confirmation. Use WhatIf for a dry run first.
5. Maintain: Open the Tools tab to run CHKDSK, SFC, DISM, network resets, and other Windows maintenance commands.
6. Review: All PowerShell output appears in the console panel in real time.

---

## Project Structure

```
RoninDiskManager/
├── Engine/
│   ├── ScanEngine.cs           # Facade that detects NTFS and routes to the right scanner
│   ├── MftScanEngine.cs        # NTFS USN journal scanner (fast path)
│   ├── FallbackScanEngine.cs   # Recursive Win32 directory walker (non-NTFS)
│   ├── SearchEngine.cs         # Parallel all-drives filename search
│   ├── DiskAnalysis.cs         # Extension breakdown and empty-folder analysis
│   ├── DuplicateFinder.cs      # Size-group then SHA-256 duplicate detection
│   ├── CleanupScanner.cs       # Age-based cleanup rule matching
│   ├── ShellCommandBuilder.cs  # Pure builders for the Move / Copy / Delete PowerShell commands
│   ├── FileSystemHelpers.cs    # Reparse-point, filename match, long-path, and size-format helpers
│   ├── RecycleBin.cs           # Send to Recycle Bin via SHFileOperation
│   └── NativeMethods.cs        # P/Invoke declarations (CreateFile, DeviceIoControl, SHFileOperation)
├── Services/
│   ├── AppSettings.cs          # Persisted settings (units, pinned paths, targets)
│   └── OperationLog.cs         # Audit log for file operations
├── Models/
│   ├── DiskNode.cs             # File or directory tree node
│   └── SearchResult.cs         # A single search / largest-file / cleanup row
├── ViewModels/
│   ├── MainViewModel.cs        # All state, commands, PowerShell execution, and tools
│   └── DiskNodeViewModel.cs    # Lazy-loading tree node presentation layer
├── Controls/
│   └── TreemapControl          # Squarified treemap rendering, drill-down, age heatmap
├── Converters/                 # WPF value converters
├── Assets/                     # App icon
├── MainWindow.xaml             # UI layout (tree, treemap, largest files, extensions, empty folders, duplicates, actions, tools, cleanup, console)
└── App.xaml                    # Theme and global resources

RoninDiskManager.Tests/         # xUnit tests for the builders, helpers, analysis, and cleanup logic
```

---

## License

Copyright (c) 2025 Ronin Empire

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but without any warranty; without even the implied warranty of merchantability or fitness for a particular purpose. See the [GNU Affero General Public License](LICENSE) for more details.
