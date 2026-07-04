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
- Treemap view: squarified treemap visualization with multi-level drill-down, similar to WinDirStat.
- Scan summary: total size, elapsed time, and file and directory counts after every scan.
- Cancellable: stop any scan or search in progress from the toolbar.

### File search

- System-wide search across all fixed drives at once, with each drive walked in parallel.
- Wildcard patterns (`*` and `?`) or plain substring matching, both case-insensitive.
- Results grid showing name, type, size, date modified, and full path.
- Runs without administrator rights: directories it cannot read are skipped instead of aborting the search.

### File operations

- Move operations: PowerShell `Move-Item` with Force, WhatIf, Verbose, NeverOverwrite, LiteralPath, and Filter, Include, and Exclude patterns.
- Delete operations: PowerShell `Remove-Item` with Force, Recurse, WhatIf, Verbose, LiteralPath, and Filter, Include, and Exclude patterns.
- Dry-run mode: the WhatIf flag on both operations lets you preview changes before committing.
- Confirmation before deletes, and an abort-if-destination-exists check for moves.
- Real-time console: live PowerShell stdout and stderr streamed to the in-app console, with exit-code reporting.

### Windows tools

- Disk Health (CHKDSK): pick a ready drive, choose Fix errors (`/f`) or Locate bad sectors (`/r`), or run a read-only scan. Checks on the system drive are scheduled for the next restart automatically.
- System Integrity (SFC): `sfc /scannow` to scan and repair, or `sfc /verifyonly` to check without changes.
- Windows Image (DISM): CheckHealth, ScanHealth, RestoreHealth (with an optional local WIM or ISO source), and component cleanup.
- Network: flush DNS, release and renew IP, reset Winsock, and reset the TCP/IP stack.
- Maintenance: launch Windows Disk Cleanup and Windows Memory Diagnostic.
- Every destructive tool shows a confirmation dialog and an inline warning first.

### Interface

- Context menus: right-click any tree node, treemap tile, or search result to open in Explorer, copy the path, move, or delete.
- Status bar: live display of the selected item path, size, percentage of root, and child count, or the scan summary when nothing is selected.
- Keyboard shortcuts: `Ctrl+E` to open in Explorer, `Ctrl+Shift+C` to copy the path.
- Resizable layout: drag the splitters to resize the tree, action panel, and console.
- Ronin theme: dark crimson, orange, cyan, and black color scheme.

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
3. Browse: The tree view shows directories sorted by size. Expand any node to see its children, or switch to the treemap for a visual overview.
4. Act: Select a node, choose Move or Delete, configure flags and filters, then click Execute. A confirmation dialog is shown before any delete. Use WhatIf for a dry run first.
5. Maintain: Open the Tools tab to run CHKDSK, SFC, DISM, network resets, and other Windows maintenance commands.
6. Review: All PowerShell output appears in the console panel in real time.

---

## Project Structure

```
RoninDiskManager/
├── Engine/
│   ├── ScanEngine.cs          # Facade that detects NTFS and routes to the right scanner
│   ├── MftScanEngine.cs       # NTFS USN journal scanner (fast path)
│   ├── FallbackScanEngine.cs  # Recursive Win32 directory walker (non-NTFS)
│   ├── SearchEngine.cs        # Parallel all-drives filename search
│   └── NativeMethods.cs       # P/Invoke declarations (CreateFile, DeviceIoControl)
├── Models/
│   ├── DiskNode.cs            # File or directory tree node
│   └── SearchResult.cs        # A single search hit
├── ViewModels/
│   ├── MainViewModel.cs       # All state, commands, PowerShell execution, and tools
│   └── DiskNodeViewModel.cs   # Lazy-loading tree node presentation layer
├── Controls/
│   └── TreemapControl         # Squarified treemap rendering and interaction
├── Converters/                # WPF value converters
├── Assets/                    # App icon
├── MainWindow.xaml            # UI layout (tree, treemap, search, actions, tools, console)
└── App.xaml                   # Theme and global resources
```

---

## License

Copyright (c) 2025 Ronin Empire

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but without any warranty; without even the implied warranty of merchantability or fitness for a particular purpose. See the [GNU Affero General Public License](LICENSE) for more details.
