namespace TCPCommunication.Services;

using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TCPCommunication.Configuration;
using TCPCommunication.Data;
using TCPCommunication.Models;

/// <summary>
/// Hosted service that orchestrates all device workers.
/// It starts one independent <see cref="DeviceWorker"/> per configured device,
/// each running on its own Task so that one device cannot block any other.
/// </summary>
public sealed class DeviceManagerService : BackgroundService
{
    private readonly TcpAcquisitionOptions _options;
    private readonly DeviceReadingRepository _repository;
    private readonly FileOutputService _fileOutput;
    private readonly ILogger<DeviceManagerService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Live map: DeviceId → worker
    private readonly ConcurrentDictionary<string, DeviceWorker> _workers = new();

    public DeviceManagerService(
        IOptions<TcpAcquisitionOptions> options,
        DeviceReadingRepository repository,
        FileOutputService fileOutput,
        ILogger<DeviceManagerService> logger,
        ILoggerFactory loggerFactory)
    {
        _options       = options.Value;
        _repository    = repository;
        _fileOutput    = fileOutput;
        _logger        = logger;
        _loggerFactory = loggerFactory;
    }

    // -------------------------------------------------------------------------
    //  BackgroundService
    // -------------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var devices = _options.Devices;

        if (devices.Count == 0)
        {
            _logger.LogWarning("No devices configured.  Add entries to TcpAcquisition:Devices in appsettings.json.");
            return;
        }

        _logger.LogInformation("Starting acquisition for {Count} device(s).", devices.Count);

        // Spin up all workers concurrently
        foreach (var config in devices)
        {
            var worker = new DeviceWorker(
                config,
                _repository,
                _fileOutput,
                _loggerFactory.CreateLogger<DeviceWorker>());

            if (_workers.TryAdd(config.DeviceId, worker))
            {
                worker.Start(stoppingToken);
            }
            else
            {
                _logger.LogWarning("Duplicate DeviceId '{DeviceId}' — skipped.", config.DeviceId);
            }
        }

        // Block here until the host requests shutdown
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });

        _logger.LogInformation("Stopping all device workers…");
        var stopTasks = _workers.Values.Select(w => w.StopAsync());
        await Task.WhenAll(stopTasks);
        _logger.LogInformation("All workers stopped.");
    }

    // -------------------------------------------------------------------------
    //  Monitoring surface (called by DeviceStatusController)
    // -------------------------------------------------------------------------

    /// <summary>Returns a snapshot of all device statuses.</summary>
    public IReadOnlyList<DeviceStatus> GetAllStatuses() =>
        _workers.Values.Select(w => w.Status).ToList();

    /// <summary>Returns the status of a single device, or null if unknown.</summary>
    public DeviceStatus? GetStatus(string deviceId) =>
        _workers.TryGetValue(deviceId, out var w) ? w.Status : null;
}
