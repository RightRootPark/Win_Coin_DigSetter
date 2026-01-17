using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using WinForms = System.Windows.Forms;
using EncryptionMinerControl.ViewModels;

namespace EncryptionMinerControl.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private WinForms.NotifyIcon _notifyIcon;

    public MainWindow()
    {
        InitializeComponent();
        InitializeNotifyIcon();
        Loaded += MainWindow_Loaded;
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new WinForms.NotifyIcon();
        _notifyIcon.Text = "Encryption Miner Control";
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick += (s, e) => ShowFromTray();

        // Context Menu
        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (s, e) => ShowFromTray());
        contextMenu.Items.Add("Exit", null, (s, e) => 
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        _notifyIcon.ContextMenuStrip = contextMenu;

        // Icon Loading
        try
        {
            if (System.IO.File.Exists("icon.png"))
            {
                using var bitmap = new System.Drawing.Bitmap("icon.png");
                // Note: The handle created here should be managed properly in a real production scenario
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
            }
            else
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (vm.IsStartInTrayEnabled)
            {
                HideToTray();
            }
        }
    }

    private void BtnHideToTray_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(3000, "Miner Control", "Running in background.", WinForms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _notifyIcon.Visible = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnClosed(e);
    }
}