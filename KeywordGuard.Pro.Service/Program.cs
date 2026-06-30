using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using KeywordGuard.Pro.Security;
using System.ServiceProcess;
using KeywordGuard.Pro.Service;

namespace KeywordGuard.Pro.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        using IHost host = CreateHostBuilder(args).Build();

        if (WindowsServiceHelpers.IsWindowsService())
        {
            ServiceBase.Run(new KeywordGuardWindowsService(host));
            return;
        }

        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddHostedService<Worker>();
            });

    private sealed class KeywordGuardWindowsService : ServiceBase
    {
        private readonly IHost _host;

        public KeywordGuardWindowsService(IHost host)
        {
            _host = host;
            ServiceName = "KeywordGuardProService";
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            _host.StartAsync().GetAwaiter().GetResult();
        }

        protected override void OnStop()
        {
            _host.StopAsync().GetAwaiter().GetResult();
        }

        protected override void OnShutdown()
        {
            ProcessHardening.MarkSystemShutdown();
            var services = _host.Services.GetServices<IHostedService>();
            foreach (var service in services)
            {
                if (service is Worker worker)
                {
                    worker.OnShutdown();
                }
            }
            _host.StopAsync().GetAwaiter().GetResult();
            base.OnShutdown();
        }
    }
}