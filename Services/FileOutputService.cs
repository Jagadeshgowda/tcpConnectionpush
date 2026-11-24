namespace TCPCommunication.Services;

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TCPCommunication.Configuration;
using TCPCommunication.Models;

/// <summary>
/// Optionally writes per-device CSV flat files in addition to the database.
/// One file per device, one line per reading.  Enabled via appsettings.
/// </summary>
public sealed class FileOutputService
{
    private readonly TcpAcquisitionOptions _options;
    private readonly ILogger<FileOutputService> _logger;

    // Per-device SemaphoreSlim(1,1) prevents concurrent writes to the same file.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileOutputService(
        IOptions<TcpAcquisitionOptions> options,
        ILogger<FileOutputService> logger)
    {
        _options = options.Value;
        _logger  = logger;

        if (_options.EnableFileOutput)
            Directory.CreateDirectory(_options.FileOutputDirectory);
    }

    public async Task AppendAsync(DeviceReading reading)
    {
        if (!_options.EnableFileOutput)
            return;

        var sem = _locks.GetOrAdd(reading.DeviceId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            var filePath = Path.Combine(
                _options.FileOutputDirectory,
                $"{reading.DeviceId}_{DateTime.UtcNow:yyyyMMdd}.csv");

            var needsHeader = !File.Exists(filePath);
            await using var sw = new StreamWriter(filePath, append: true, Encoding.UTF8);

            if (needsHeader)
                await sw.WriteLineAsync("Timestamp,DeviceId,Success,ErrorMessage,RawData");

            var csv = string.Join(",",
                reading.Timestamp.ToString("o"),
                Escape(reading.DeviceId),
                reading.IsSuccess,
                Escape(reading.ErrorMessage ?? string.Empty),
                Escape(reading.RawData));

            await sw.WriteLineAsync(csv);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File output error for device {DeviceId}", reading.DeviceId);
        }
        finally
        {
            sem.Release();
        }
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
