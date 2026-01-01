using GSProBridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Settings.Configuration;

// Configure Serilog early (before Host build)
Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/gspro-bridge-.log", rollingInterval: RollingInterval.Day)
            .CreateBootstrapLogger();

try
{
    Log.Information("GSProBridge starting up...");

    IHost host = CreateHostBuilder(args).Build();
    await host.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "GSProBridge terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseSerilog((context, services, configuration) =>
        {
            // Explicitly specify assemblies for single-file publish compatibility
            // Use extension method types which are public
            var readerOptions = new ConfigurationReaderOptions(
                typeof(ConsoleLoggerConfigurationExtensions).Assembly,  // Serilog.Sinks.Console
                typeof(FileLoggerConfigurationExtensions).Assembly      // Serilog.Sinks.File
            );

            _ = configuration
                .ReadFrom.Configuration(context.Configuration, readerOptions)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        })
        .ConfigureServices((hostContext, services) =>
        {
            // Register R10 Bluetooth service
            _ = services.AddSingleton<IR10BluetoothService, R10BluetoothService>();

            // Register hosted service to run R10 connection
            _ = services.AddHostedService<R10HostedService>();
        });
}
