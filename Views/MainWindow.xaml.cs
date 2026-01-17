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

        // Icon Loading - Robust Logic
        try
        {
            // 1. Try generic application icon first as safe default
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;

            // 2. Try loading custom icon from execution directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = System.IO.Path.Combine(baseDir, "icon.png");

            if (System.IO.File.Exists(iconPath))
            {
                using var bitmap = new System.Drawing.Bitmap(iconPath);
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
            }
        }
        catch (Exception ex)
        {
            // Fallback is already set to SystemIcons.Application
            System.Diagnostics.Debug.WriteLine($"Icon load failed: {ex.Message}");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeNotifyIcon();

        if (DataContext is MainViewModel vm)
        {
            // Safety Check: If icon failed to initialize properly (null icon handle), don't hide window
            if (vm.IsStartInTrayEnabled && _notifyIcon.Icon != null)
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
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(3000, "Miner Control", "Running in background.", WinForms.ToolTipIcon.Info);
        }
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