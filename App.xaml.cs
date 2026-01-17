using System.Windows;
using EncryptionMinerControl.Views;
using EncryptionMinerControl.ViewModels;

namespace EncryptionMinerControl;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        var mainWindow = new MainWindow();
        _mainViewModel = new MainViewModel();
        mainWindow.DataContext = _mainViewModel;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Cleanup();
        base.OnExit(e);
    }
}

