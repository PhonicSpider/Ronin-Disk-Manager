using System.Windows;
using RoninDiskManager.Engine;
using RoninDiskManager.Services;

namespace RoninDiskManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Apply persisted display preferences before any window binds sizes.
        FileSystemHelpers.UseBinaryUnits = SettingsService.Current.UseBinaryUnits;
    }
}
