using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace EncryptionMinerControl.Services;

public class ProcessManager
{
    private Process? _process;
    private readonly Action<string> _logCallback;

    public bool IsRunning => _process != null && !_process.HasExited;

    public ProcessManager(Action<string> logCallback)
    {
        _logCallback = logCallback;
    }

    public void Start(string executablePath, string arguments)
    {
        if (IsRunning) return;

        if (!File.Exists(executablePath))
        {
            _logCallback?.Invoke($"[Error] Executable not found: {executablePath}");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
            };

            _process = new Process { StartInfo = startInfo };
            
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) _logCallback?.Invoke(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) _logCallback?.Invoke($"[Error] {e.Data}"); };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logCallback?.Invoke($"[System] Process started: {executablePath} {arguments}");
        }
        catch (Exception ex)
        {
            _logCallback?.Invoke($"[Exception] Failed to start process: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_process == null || _process.HasExited) return;

        try
        {
#if NETCOREAPP
            _process.Kill(true); // Kill entire process tree
#else
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /T /PID {_process.Id}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(2000);
            }
            catch { /* Fallback to standard kill if taskkill fails */ }

            if (!_process.HasExited) _process.Kill();
#endif
            _process.WaitForExit(3000);
            _process = null;
            _logCallback?.Invoke("[System] Process stopped.");
        }
        catch (Exception ex)
        {
            _logCallback?.Invoke($"[Exception] Failed to stop process: {ex.Message}");
        }
    }

    /// <summary>
    /// [Korea] 지정된 폴더 내에서 실행 중인 특정 이름의 모든 프로세스를 강제로 종료합니다. (좀비 프로세스 청소용)
    /// </summary>
    public static void KillProcessByPath(string processNameWithoutExt, string directoryPath)
    {
        try
        {
            var processes = Process.GetProcessesByName(processNameWithoutExt);
            foreach (var p in processes)
            {
                try
                {
                    // 정확한 경로 확인 (내 폴더 안에 있는 녀석인지)
                    // 권한 문제로 접근 불가할 수 있으므로 try-catch
                    if (p.MainModule?.FileName != null)
                    {
                        string processPath = p.MainModule.FileName;
                        if (processPath.Contains(directoryPath, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            Debug.WriteLine($"[System] Killed zombie process: {processNameWithoutExt} ({p.Id})");
                        }
                    }
                }
                catch { /* Ignore access denied or already exited */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[System] Failed to clean up processes: {ex.Message}");
        }
    }
}
