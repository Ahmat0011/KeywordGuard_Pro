using System.Diagnostics;
using KeywordGuard.Pro.Agent;
using KeywordGuard.Pro.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace KeywordGuard.Pro.Service;

/// <summary>
/// Windows Service Watchdog.
/// Ueberwacht, ob der Agent laeuft. Startet ihn via TaskScheduler.
/// Schuetzt sich selbst, solange der Timer aktiv ist.
/// </summary>
public class Worker : BackgroundService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AgentRunValueName = "KeywordGuardProAgent";

    private readonly ILogger<Worker> _logger;
    private bool _isShuttingDown = false;
    private bool _wasEverActive = false;
    private string? _agentExePath;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() =>
        {
            _isShuttingDown = true;
        });

        _logger.LogInformation("Service gestartet.");
        _agentExePath = Path.Combine(AppContext.BaseDirectory, "KeywordGuard.Pro.Agent.exe");
        EnsureMachineRunAutostart();

        while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
        {
            try
            {
                EnsureMachineRunAutostart();
                var config = ConfigStore.Load();
                bool shouldBeActive = config != null && config.IsActive();

                if (shouldBeActive)
                {
                    _wasEverActive = true;

                    // Agent ueberwachen
                    var agentProcs = Process.GetProcessesByName("KeywordGuard.Pro.Agent");
                    if (agentProcs.Length == 0)
                    {
                        if (IsUserLoggedIn())
                        {
                            _logger.LogInformation("Starte Agent via TaskScheduler...");
                            bool started = TaskSchedulerGuard.RunAgentTask();
                            if (!started)
                            {
                                _logger.LogWarning("TaskScheduler-Start fehlgeschlagen. HKLM-Run-Fallback bleibt aktiv.");
                            }
                        }
                    }
                    else
                    {
                        foreach (var p in agentProcs) p.Dispose();
                    }
                }
                else
                {
                    // Config geloescht? Dann Schutz NICHT deaktivieren.
                    // Timer abgelaufen? Dann Schutz deaktivieren.
                    if (config == null && _wasEverActive)
                    {
                        // Config geloescht – Schutz bleibt!
                    }
                    else
                    {
                        _wasEverActive = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler im Watchdog-Loop");
            }

            await Task.Delay(2000, stoppingToken);
        }

        _logger.LogInformation("Service beendet.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _isShuttingDown = true;
        await base.StopAsync(cancellationToken);
    }

    private void EnsureMachineRunAutostart()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_agentExePath) || !File.Exists(_agentExePath))
            {
                return;
            }

            using var runKey = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true);
            if (runKey == null) return;

            string expectedCommand = $"\"{_agentExePath}\"";
            string? currentValue = runKey.GetValue(AgentRunValueName) as string;
            if (!string.Equals(currentValue, expectedCommand, StringComparison.Ordinal))
            {
                runKey.SetValue(AgentRunValueName, expectedCommand, RegistryValueKind.String);
                _logger.LogInformation("HKLM-Run-Autostart fuer Agent gesetzt.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Konnte HKLM-Run-Autostart nicht setzen.");
        }
    }

    private static bool IsUserLoggedIn()
    {
        try
        {
            var users = Process.GetProcessesByName("explorer");
            foreach (var u in users)
            {
                try
                {
                    if (u.SessionId > 0) return true;
                }
                finally { u.Dispose(); }
            }
            return false;
        }
        catch { return false; }
    }
}