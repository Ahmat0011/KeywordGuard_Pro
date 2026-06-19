using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KeywordGuard.Pro.Service;

namespace KeywordGuard.Pro.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "KeywordGuardProService";
            })
            .ConfigureServices((_, services) =>
            {
                services.AddHostedService<Worker>();
            });
}