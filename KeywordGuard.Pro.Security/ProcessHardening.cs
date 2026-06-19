using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KeywordGuard.Pro.Security;

public static class ProcessHardening
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_SHUTTINGDOWN = 0x2000;

    /// <summary>
    /// Ueberprueft, ob das System gerade herunterfaehrt oder neu startet.
    /// </summary>
    public static bool IsSystemShuttingDown()
    {
        try
        {
            if (Environment.HasShutdownStarted) return true;
            return GetSystemMetrics(SM_SHUTTINGDOWN) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kritischer Prozessmodus bleibt absichtlich deaktiviert,
    /// um sicherheitskritische Nebenwirkungen und AV-Fehlalarme zu vermeiden.
    /// </summary>
    public static bool SetCritical(bool isCritical)
    {
        return false;
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}