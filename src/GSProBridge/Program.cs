using GSProBridge.Bluetooth;
using GSProBridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Settings.Configuration;

// Configure Serilog early (before Host build)
Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/gspro-bridge-.log", rollingInterval: RollingInterval.Day)
            .CreateBootstrapLogger();

try
{
    // Check for traffic capture mode: --capture-traffic [output-file] [duration-seconds]
    if (args.Length >= 1 && args[0] == "--capture-traffic")
    {
        // Default output file: logs/r10-traffic-capture-{datetime}.log
        string outputFile;
        int duration;

        if (args.Length >= 2 && !int.TryParse(args[1], out int _))
        {
            // User provided filename
            outputFile = args[1];
            duration = args.Length >= 3 && int.TryParse(args[2], out int d) ? d : 60;
        }
        else
        {
            // Generate default filename
            _ = Directory.CreateDirectory("logs");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            outputFile = Path.Combine("logs", $"r10-traffic-capture-{timestamp}.log");
            duration = args.Length >= 2 && int.TryParse(args[1], out int d) ? d : 60;
        }

        Log.Information("Traffic Capture Mode");

        // Build minimal host for DI and logging
        IHost captureHost = Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) =>
            {
                var readerOptions = new ConfigurationReaderOptions(
                    typeof(ConsoleLoggerConfigurationExtensions).Assembly,
                    typeof(FileLoggerConfigurationExtensions).Assembly
                );

                _ = configuration
                    .ReadFrom.Configuration(context.Configuration, readerOptions)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext();
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Register R10 protocol components for DI
                _ = services.AddSingleton<R10MessageCollector>();      // COBS message collector
                _ = services.AddSingleton<R10MessageFramer>();         // Dual-format framer (depends on collector)
            })
            .Build();

        // Resolve dependencies from DI container (composition root)
        ILogger<TrafficCaptureMode> logger = captureHost.Services.GetRequiredService<ILogger<TrafficCaptureMode>>();
        R10MessageFramer messageFramer = captureHost.Services.GetRequiredService<R10MessageFramer>();

        // Create TrafficCaptureMode with injected dependencies + runtime config
        TrafficCaptureMode captureMode = new(logger, messageFramer, outputFile, duration);

        await captureMode.RunAsync();
        return 0;
    }

    // Normal mode: start hosted services
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
