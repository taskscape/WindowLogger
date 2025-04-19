using System.Runtime.InteropServices;

namespace WindowLogger;

public class UserActivityDetector
{
    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static bool IsUserInactive(TimeSpan threshold)
    {
        LASTINPUTINFO lastInputInfo = new();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

        if (!GetLastInputInfo(ref lastInputInfo))
        {
            throw new Exception("GetLastInputInfo failed.");
        }

        uint lastInputTick = lastInputInfo.dwTime;
        uint currentTick = (uint)Environment.TickCount;

        uint idleTicks = currentTick - lastInputTick;
        TimeSpan idleTime = TimeSpan.FromMilliseconds(idleTicks);

        return idleTime >= threshold;
    }
}