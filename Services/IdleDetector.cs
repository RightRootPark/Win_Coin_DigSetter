using System.Runtime.InteropServices;

namespace EncryptionMinerControl.Services;

public static class IdleDetector
{
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    /// <summary>
    /// Returns the number of seconds since the last user input (mouse or keyboard).
    /// </summary>
    public static double GetIdleTimeSeconds()
    {
        var lastInputInfo = new LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

        if (GetLastInputInfo(ref lastInputInfo))
        {
            // Environment.TickCount is in milliseconds. 
            // Note: TickCount loops every 24.9 days, but for idle detection < 1 min, simple subtraction is usually safe enough 
            // unless it wraps right *now*, which is rare edge case we can ignore for this purpose or handle with uint math.
            uint ticks = (uint)Environment.TickCount;
            uint idleTicks = ticks - lastInputInfo.dwTime;
            return idleTicks / 1000.0;
        }

        return 0;
    }
}
