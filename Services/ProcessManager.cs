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
            _process.Kill(true); // Kill entire process tree
            _process.WaitForExit(3000);
            _process = null;
            _logCallback?.Invoke("[System] Process stopped.");
        }
        catch (Exception ex)
        {
            _logCallback?.Invoke($"[Exception] Failed to stop process: {ex.Message}");
        }
    }
}
