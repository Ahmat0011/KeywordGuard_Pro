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
    private readonly ILogger<Worker> _logger;
    private bool _isShuttingDown = false;
    private bool _isCritical = false;
    private bool _wasEverActive = false;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() =>
        {
            // Pruefen ob das ein echter Windows-Shutdown ist
            if (!ProcessHardening.IsSystemShuttingDown())
            {
                var config = ConfigStore.Load();
                bool timerActive = (config != null && config.IsActive()) ||
                                   (config == null && _wasEverActive);
                if (timerActive)
                {
                    _logger.LogWarning("MANUELLER STOPP-VERSUCH! Timer aktiv -> Blockiere.");
                    BlockingLoop(stoppingToken);
                    return;
                }
            }

            _isShuttingDown = true;
            DeactivateCritical();
        });

        _logger.LogInformation("Service gestartet.");

        while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
        {
            try
            {
                var config = ConfigStore.Load();
                bool shouldBeActive = config != null && config.IsActive();

                if (shouldBeActive)
                {
                    _wasEverActive = true;
                    ActivateCritical();

                    // Agent ueberwachen
                    var agentProcs = Process.GetProcessesByName("KeywordGuard.Pro.Agent");
                    if (agentProcs.Length == 0)
                    {
                        if (IsUserLoggedIn())
                        {
                            _logger.LogInformation("Starte Agent via TaskScheduler...");
                            TaskSchedulerGuard.RunAgentTask();
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
                        DeactivateCritical();
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

        DeactivateCritical();
        _logger.LogInformation("Service beendet.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var config = ConfigStore.Load();
        bool timerActive = (config != null && config.IsActive()) ||
                           (config == null && _wasEverActive);
        if (timerActive && !ProcessHardening.IsSystemShuttingDown())
        {
            _logger.LogWarning("StopAsync blockiert – Timer noch aktiv!");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return;
        }

        _isShuttingDown = true;
        DeactivateCritical();
        await base.StopAsync(cancellationToken);
    }

    private void BlockingLoop(CancellationToken token)
    {
        _logger.LogWarning("BLOCKING-LOOP: Service verweigert Stopp.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                var config = ConfigStore.Load();
                bool timerStillActive = (config != null && config.IsActive()) ||
                                        (config == null && _wasEverActive);
                if (!timerStillActive)
                {
                    _logger.LogInformation("Timer abgelaufen -> Stopp freigegeben.");
                    DeactivateCritical();
                    _isShuttingDown = true;
                    return;
                }
                Thread.Sleep(1000);
            }
            catch
            {
                Thread.Sleep(1000);
            }
        }
    }

    private void ActivateCritical()
    {
        if (!_isCritical)
        {
            _isCritical = true;
            _logger.LogInformation("Service als kritisch markiert (simuliert, API deaktiviert).");
        }
    }

    private void DeactivateCritical()
    {
        if (_isCritical)
        {
            _isCritical = false;
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