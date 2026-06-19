using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KeywordGuard.Pro.Security;

public static class ProcessHardening
{
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlSetProcessIsCritical(uint bNew, out uint pbOld, uint bNeedScb);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlAdjustPrivilege(int Privilege, bool bEnablePrivilege, bool IsThreadPrivilege, out bool PreviousValue);

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
    /// Markiert den aktuellen Prozess als systemkritisch.
    /// Wenn der Prozess ohne vorherige Deaktivierung gekillt wird,
    /// loest Windows einen Bluescreen aus.
    /// Nur mit Administratorrechten moeglich.
    /// </summary>
    public static bool SetCritical(bool isCritical)
    {
        try
        {
            // SeDebugPrivilege aktivieren (20)
            RtlAdjustPrivilege(20, true, false, out bool _);
            int status = RtlSetProcessIsCritical(isCritical ? 1u : 0u, out _, 0u);
            return status == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}