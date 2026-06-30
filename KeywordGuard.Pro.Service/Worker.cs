using System.Diagnostics;
using KeywordGuard.Pro.Agent;
using KeywordGuard.Pro.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeywordGuard.Pro.Service;

/// <summary>
/// Windows Service Watchdog.
/// Ueberwacht, ob der Agent laeuft. Startet ihn via TaskScheduler.
/// Schuetzt sich selbst, solange der Timer aktiv ist.
/// </summary>
public class Worker : BackgroundService
{
    private const string AgentProcessName = "KeywordGuard.Pro.Agent";
    private readonly ILogger<Worker> _logger;
    private bool _isShuttingDown = false;
    private bool _wasEverActive = false;
    private bool _agentSeenWhileLoggedIn = false;
    private volatile bool _isSystemShuttingDown = false;

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
        ProcessHardening.ClearLegalShutdownSignal();
        bool protectionApplied = ProcessHardening.ApplySelfProtection();
        _logger.LogInformation("Service-Selbstschutz aktiv: {ProtectionApplied}", protectionApplied);

        while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
        {
            try
            {
                if (ProcessHardening.IsSystemShuttingDown() || _isSystemShuttingDown)
                {
                    _isSystemShuttingDown = true;
                    _isShuttingDown = true;
                    break;
                }

                var config = ConfigStore.Load();
                bool shouldBeActive = config != null && config.IsActive();
                bool userLoggedIn = IsUserLoggedIn();

                if (shouldBeActive)
                {
                    _wasEverActive = true;
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

                var agentProcesses = Process.GetProcessesByName(AgentProcessName);
                bool agentRunning = agentProcesses.Length > 0;
                foreach (var process in agentProcesses)
                {
                    process.Dispose();
                }

                if (agentRunning)
                {
                    _agentSeenWhileLoggedIn = userLoggedIn;
                }

                // Zurueckgesetzt auf die vorherige Abfrage der Array-Laenge
                if (agentProcesses.Length == 0)
                {
                    // ...prüfe ZUERST, ob Windows gerade legal herunterfährt oder abmeldet
                    if (!_isSystemShuttingDown && !CheckLegalShutdownSignal())
                    {
                        // Nur wenn es KEIN legaler Shutdown ist, greift der Schutz!
                        if (userLoggedIn && _agentSeenWhileLoggedIn)
                        {
                            TriggerEmergencyShutdown();
                            break;
                        }
                    }
                }

                if (!agentRunning)
                {
                    if (!userLoggedIn)
                    {
                        _agentSeenWhileLoggedIn = false;
                    }
                    else if (shouldBeActive && !_agentSeenWhileLoggedIn)
                    {
                        _logger.LogInformation("Starte Agent via TaskScheduler...");
                        bool started = TaskSchedulerGuard.RunAgentTask();
                        if (!started)
                        {
                            _logger.LogWarning("TaskScheduler-Start fehlgeschlagen.");
                        }
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

    private void TriggerPartnerLossShutdown(string message)
    {
        if (ProcessHardening.IsSystemShuttingDown() || _isSystemShuttingDown)
        {
            _isShuttingDown = true;
            return;
        }

        _isShuttingDown = true;
        _logger.LogCritical("{Message}", message);

        if (!ProcessHardening.TriggerEmergencyShutdown(message))
        {
            _logger.LogCritical("Not-Herunterfahren konnte nicht gestartet werden.");
        }
    }

    public void OnShutdown()
    {
        _isSystemShuttingDown = true;
    }

    private bool CheckLegalShutdownSignal()
    {
        return ProcessHardening.CheckLegalShutdownSignal();
    }

    private void TriggerEmergencyShutdown()
    {
        TriggerPartnerLossShutdown("Agent-Prozess wurde unerwartet beendet.");
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