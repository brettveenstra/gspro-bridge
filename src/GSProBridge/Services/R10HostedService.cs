using LaunchMonitor.Proto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace GSProBridge.Services;

/// <summary>
/// Hosted service that manages R10 connection lifecycle and displays shot data
/// </summary>
public class R10HostedService : IHostedService
{
    private readonly IR10BluetoothService _r10Service;
    private readonly ILogger<R10HostedService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="R10HostedService"/> class
    /// </summary>
    /// <param name="r10Service">R10 Bluetooth service</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="appLifetime">Application lifetime manager</param>
    public R10HostedService(
        IR10BluetoothService r10Service,
        ILogger<R10HostedService> logger,
        IHostApplicationLifetime appLifetime)
    {
        _r10Service = r10Service;
        _logger = logger;
        _appLifetime = appLifetime;
    }

    /// <summary>
    /// Starts the R10 hosted service and establishes Bluetooth connection
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting R10 Hosted Service...");

        // Wire up events
        _r10Service.ConnectionStatusChanged += OnConnectionStatusChanged;
        _r10Service.ShotDataReceived += OnShotDataReceived;

        // Display initial UI
        AnsiConsole.MarkupLine("[bold green]GSProBridge - R10 Shot Data Display (MVP)[/]");
        AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]");
        AnsiConsole.WriteLine();

        // Connect to R10
        try
        {
            await _r10Service.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to R10");
            AnsiConsole.MarkupLine("[red]Failed to connect to R10. See logs for details.[/]");
            _appLifetime.StopApplication();
        }
    }

    /// <summary>
    /// Stops the R10 hosted service and closes Bluetooth connection
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping R10 Hosted Service...");

        _r10Service.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _r10Service.ShotDataReceived -= OnShotDataReceived;

        await _r10Service.DisconnectAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow]Disconnected from R10[/]");
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        if (isConnected)
        {
            AnsiConsole.MarkupLine("[green]✓ Connected to R10[/]");
            AnsiConsole.MarkupLine("[dim]Waiting for shots...[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine("[red]✗ Disconnected from R10[/]");
        }
    }

    private void OnShotDataReceived(object? sender, Metrics metrics)
    {
        // Display shot data in a formatted table
        var table = new Table();
        _ = table.Border(TableBorder.Rounded);
        _ = table.Title($"[bold yellow]Shot #{metrics.ShotId}[/]");

        _ = table.AddColumn("[bold]Metric[/]");
        _ = table.AddColumn("[bold]Value[/]");

        // Ball metrics
        if (metrics.BallMetrics != null)
        {
            BallMetrics ball = metrics.BallMetrics;

            _ = table.AddRow("Ball Speed", $"{ball.BallSpeed * 2.237:F1} MPH");
            _ = table.AddRow("Launch Angle", $"{ball.LaunchAngle:F1}°");
            _ = table.AddRow("Launch Direction", $"{ball.LaunchDirection:F1}°");
            _ = table.AddRow("Total Spin", $"{ball.TotalSpin:F0} RPM");
            _ = table.AddRow("Spin Axis", $"{ball.SpinAxis:F1}°");
        }

        // Club metrics
        if (metrics.ClubMetrics != null)
        {
            ClubMetrics club = metrics.ClubMetrics;

            _ = table.AddRow("", ""); // Separator
            _ = table.AddRow("Club Speed", $"{club.ClubHeadSpeed * 2.237:F1} MPH");
            _ = table.AddRow("Club Path", $"{club.ClubAnglePath:F1}°");
            _ = table.AddRow("Face Angle", $"{club.ClubAngleFace:F1}°");
            _ = table.AddRow("Attack Angle", $"{club.AttackAngle:F1}°");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
