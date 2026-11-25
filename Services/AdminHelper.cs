using System.Diagnostics;
using System.Security.Principal;

namespace EncryptionMinerControl.Services;

/// <summary>
/// Provides Windows administrator privilege checks and elevated Task Scheduler registration.
/// 관리자 권한 확인 및 UAC 없는 시작 프로그램(작업 스케줄러) 등록을 담당함.
/// </summary>
public static class AdminHelper
{
    public const string StartupTaskName = "EncryptionMinerControl_AutoStart";

    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartAsAdmin()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsStartupTaskRegistered()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{StartupTaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc == null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) SetStartupTask(bool enable)
    {
        string? exePath = Environment.ProcessPath;
        if (enable && string.IsNullOrEmpty(exePath))
        {
            return (false, "Executable path could not be resolved.");
        }

        // Highest run level prevents UAC prompt at user logon / 최고 권한 실행 등록으로 로그인 시 UAC 팝업 생략
        string args = enable
            ? $"/Create /F /RL HIGHEST /SC ONLOGON /TN \"{StartupTaskName}\" /TR \"\\\"{exePath}\\\"\""
            : $"/Delete /F /TN \"{StartupTaskName}\"";

        try
        {
            bool isAdmin = IsRunningAsAdmin();
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = !isAdmin,
                CreateNoWindow = isAdmin,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (isAdmin)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }
            else
            {
                startInfo.Verb = "runas";
            }

            using var proc = Process.Start(startInfo);
            if (proc == null) return (false, "Failed to launch schtasks.exe.");

            proc.WaitForExit(10000);
            if (proc.ExitCode == 0)
            {
                return (true, enable ? "Startup task registered." : "Startup task removed.");
            }

            string err = isAdmin ? proc.StandardError.ReadToEnd().Trim() : $"ExitCode {proc.ExitCode}";
            return (false, string.IsNullOrEmpty(err) ? $"schtasks failed (ExitCode {proc.ExitCode})" : err);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
