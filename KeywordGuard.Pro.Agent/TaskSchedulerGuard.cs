using System.Diagnostics;

namespace KeywordGuard.Pro.Agent;

/// <summary>
/// Verwaltet die TaskScheduler-Aufgaben fuer Autostart und Watchdog.
/// Wichtig: KEIN /ru Parameter – der Task laeuft als aktueller Benutzer.
/// </summary>
public static class TaskSchedulerGuard
{
    private const string StartupTaskName = "KeywordGuardProStartup";

    /// <summary>
    /// Erstellt die onlogon-Aufgabe, falls sie nicht existiert.
    /// Diese Aufgabe startet den Agenten bei Benutzer-Login in Session 1.
    /// Verwendet Register-ScheduledTask (PowerShell COM-API) statt schtasks.exe,
    /// um Fehlalarme von Antivirenprogrammen zu vermeiden.
    /// </summary>
    public static void EnsureStartupTask(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // Pruefen ob Task bereits existiert
            if (TaskExists(StartupTaskName))
                return;

            // Task erstellen via PowerShell Register-ScheduledTask (weniger AV-Fehlalarme als schtasks.exe)
            var psScript =
                $"$a = New-ScheduledTaskAction -Execute '{exePath}';" +
                "$t = New-ScheduledTaskTrigger -AtLogOn;" +
                "$p = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -RunLevel Highest -LogonType Interactive;" +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0;" +
                $"Register-ScheduledTask -TaskName '{StartupTaskName}' -Description 'Starts KeywordGuard Pro Agent on user logon' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";

            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
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