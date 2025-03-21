using Serilog;
using System.Text.Json;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureAppConfiguration((hostContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs/log-.txt"),
                rollingInterval: RollingInterval.Day)
            .WriteTo.Console(restrictedToMinimumLevel:
                context.HostingEnvironment.IsDevelopment() ?
                    Serilog.Events.LogEventLevel.Debug :
                    Serilog.Events.LogEventLevel.Information)
            .CreateLogger();
        logging.AddSerilog();
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();
        var torHostSettings = configuration.GetSection("TorHost").Get<TorHostSettings>();
        webBuilder.UseUrls($"http://*:{torHostSettings.WebHost.WebHostPort}");
    })
    .ConfigureServices((hostContext, services) =>
    {
        var torHostSettings = hostContext.Configuration.GetSection("TorHost").Get<TorHostSettings>();
        services.AddSingleton(torHostSettings);
        services.AddSingleton<TorHost>();
        services.AddSingleton<WebServer>();
        services.AddSingleton<SshServer>();
        services.AddSingleton<TorHttpClient>();
        services.AddHostedService<Worker>();
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.WriteIndented = true;
            });
        services.Configure<HostOptions>(opts =>
            opts.ShutdownTimeout = TimeSpan.FromSeconds(30));
    })
    .Build();

if (Environment.UserInteractive)
{
    var launchArgs = Environment.GetCommandLineArgs();
        // Настройка цветного вывода для консоли
        Log.Logger = new LoggerConfiguration()
        .WriteTo.Console(
            theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
            outputTemplate: "{Timestamp:HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}")
        .MinimumLevel.Debug()
        .CreateLogger();
#if !DEBUG
    const string serviceName = "TorHost";
    if (!TorHostInstaller.IsServiceInstalled(serviceName) && launchArgs.Contains("-i"))
    {
        if (TorHostInstaller.IsUserAdministrator())
        {
            TorHostInstaller.InstallService();
            TorHostInstaller.StartService(serviceName);
        }
        else
        {
            Console.WriteLine("Administrator rights are required to install the service.");
            return;
        }
        Environment.Exit(0);
    }
    if (TorHostInstaller.IsServiceInstalled(serviceName) && launchArgs.Contains("-r"))
    {
        if (TorHostInstaller.IsUserAdministrator())
        {
            TorHostInstaller.StopService(serviceName);
            TorHostInstaller.RemoveService();
        }
        else
        {
            Console.WriteLine("Administrator rights are required to install the service.");
            return;
        }
        Environment.Exit(0);
    }
    if (TorHostInstaller.IsServiceInstalled(serviceName) && !TorHostInstaller.IsServiceRunning(serviceName))
    {
        TorHostInstaller.StartService(serviceName);
        Environment.Exit(0);
    }
#endif
    var service = host.Services.GetRequiredService<TorHost>();
    service.Start();
    Console.CancelKeyPress += (sender, e) => service.Stop();
    Thread.Sleep(Timeout.Infinite);
//#endif
}
else
{
    await host.RunAsync();
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
    }
    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}