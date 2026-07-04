using MahApps.Metro.Controls;
using RoninDiskManager.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RoninDiskManager;

public partial class MainWindow : MetroWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Global keyboard shortcuts
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => Vm?.OpenInExplorerCommand.Execute(null)),
            new KeyGesture(Key.E, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => Vm?.CopyPathCommand.Execute(null)),
            new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift)));
        // F5 rescans; Ctrl+F focuses the path/search box.
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => { if (Vm?.ScanCommand.CanExecute(null) == true) Vm.ScanCommand.Execute(null); }),
            new KeyGesture(Key.F5)));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => { InputBox.Focus(); InputBox.SelectAll(); }),
            new KeyGesture(Key.F, ModifierKeys.Control)));
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void DiskTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedNode = e.NewValue as DiskNodeViewModel;
    }

    // ── Drag and drop a folder onto the window to scan it ─────────────────────
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (Vm == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;

        // Use the dropped item's folder (its own path if it is a directory).
        string first = paths[0];
        string target = Directory.Exists(first) ? first : (Path.GetDirectoryName(first) ?? first);

        Vm.InputQuery = target;
        if (Vm.ScanCommand.CanExecute(null)) Vm.ScanCommand.Execute(null);
    }

    private void ConsoleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ScrollToEnd();
    }

    // Minimal ICommand wrapper for InputBinding — no need for a full RelayCommand here
    private sealed class RelayCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067  // event is required by ICommand but never raised (always executable)
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? _) => true;
        public void Execute(object? _) => execute();
    }
}
