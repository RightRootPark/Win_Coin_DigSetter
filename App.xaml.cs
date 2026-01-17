using System.Windows;
using EncryptionMinerControl.Views;
using EncryptionMinerControl.ViewModels;

namespace EncryptionMinerControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        var mainWindow = new MainWindow();
        mainWindow.DataContext = new MainViewModel();
        mainWindow.Show();
    }
}

