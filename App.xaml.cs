using System.Windows;
using EncryptionMinerControl.Views;
using EncryptionMinerControl.ViewModels;

namespace EncryptionMinerControl;

public partial class App : System.Windows.Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // [Korea] CP949 인코딩 지원 (배치 파일 읽기용)
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        
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

