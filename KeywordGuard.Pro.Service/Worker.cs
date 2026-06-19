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
    private bool _wasEverActive = false;

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

        while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
        {
            try
            {
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