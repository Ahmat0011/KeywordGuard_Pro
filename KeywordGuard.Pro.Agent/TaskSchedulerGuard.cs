using System.Diagnostics;

namespace KeywordGuard.Pro.Agent;

/// <summary>
/// Verwaltet die TaskScheduler-Aufgaben fuer Autostart und Watchdog.
/// Verwendet transparente TaskScheduler-Aufrufe ohne PowerShell-Bypass.
/// </summary>
public static class TaskSchedulerGuard
{
    private const string StartupTaskName = "KeywordGuardProStartup";

    /// <summary>
    /// Erstellt die onlogon-Aufgabe, falls sie nicht existiert.
    /// Diese Aufgabe startet den Agenten bei Benutzer-Login in Session 1.
    /// Verwendet schtasks mit klaren Parametern (ohne EncodedCommand/Bypass).
    /// </summary>
    public static void EnsureStartupTask(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // Pruefen ob Task bereits existiert
            if (TaskExists(StartupTaskName))
                return;

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/create /tn \"{StartupTaskName}\" /sc onlogon /rl highest /tr \"\\\"{exePath}\\\"\" /f",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch { }
    }

    /// <summary>
    /// Startet den Agenten ueber die TaskScheduler-Aufgabe.
    /// Dadurch wird der Agent in der Benutzersession gestartet (nicht Session 0).
    /// </summary>
    public static bool RunAgentTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/run /tn \"" + StartupTaskName + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool TaskExists(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /tn \"" + taskName + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}