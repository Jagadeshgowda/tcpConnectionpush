namespace TCPCommunication.Services;

using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using TCPCommunication.Data;
using TCPCommunication.Models;

/// <summary>
/// Manages the lifecycle of a single TCP device connection.
/// Each instance runs its own independent polling loop on the thread-pool
/// so that one device can never block another.
/// </summary>
public sealed class DeviceWorker : IAsyncDisposable
{
    // -------------------------------------------------------------------------
    //  Fields
    // -------------------------------------------------------------------------

    private readonly DeviceConfig _config;
    private readonly DeviceReadingRepository _repository;
    private readonly FileOutputService _fileOutput;
    private readonly ILogger<DeviceWorker> _logger;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Thread-safe status snapshot — read by the monitoring API without locks.
    private volatile DeviceStatus _status;

    // -------------------------------------------------------------------------
    //  Constructor
    // -------------------------------------------------------------------------

    public DeviceWorker(
        DeviceConfig config,
        DeviceReadingRepository repository,
        FileOutputService fileOutput,
        ILogger<DeviceWorker> logger)
    {
        _config     = config;
        _repository = repository;
        _fileOutput = fileOutput;
        _logger     = logger;

        _status = new DeviceStatus
        {
            DeviceId = config.DeviceId,
            Host     = config.Host,
            Port     = config.Port,
            State    = ConnectionState.Disconnected
        };
    }

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

    public string DeviceId => _config.DeviceId;
    public DeviceStatus Status => _status;

    public void Start(CancellationToken applicationToken)
    {
        _cts      = CancellationTokenSource.CreateLinkedTokenSource(applicationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { /* expected */ }
            catch (TimeoutException) { /* force-stop */ }
        }

        UpdateState(ConnectionState.Stopped);
        DisposeTcpResources();
    }

    // -------------------------------------------------------------------------
    //  Core polling loop
    // -------------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("[{DeviceId}] Worker started ({Host}:{Port})",
            _config.DeviceId, _config.Host, _config.Port);

        while (!ct.IsCancellationRequested)
        {
            if (!IsConnected())
            {
                await ConnectAsync(ct);
                if (!IsConnected())
                {
                    // Back-off before next reconnect attempt
                    await SafeDelay(_config.ReconnectDelayMs, ct);
                    continue;
                }
            }

            await PollDeviceAsync(ct);
            await SafeDelay(_config.PollingIntervalMs, ct);
        }

        _logger.LogInformation("[{DeviceId}] Worker stopped.", _config.DeviceId);
    }

    // -------------------------------------------------------------------------
    //  Connect
    // -------------------------------------------------------------------------

    private async Task ConnectAsync(CancellationToken ct)
    {
        UpdateState(IsConnected() ? ConnectionState.Reconnecting : ConnectionState.Connecting);
        _logger.LogDebug("[{DeviceId}] Connecting to {Host}:{Port}…",
            _config.DeviceId, _config.Host, _config.Port);

        DisposeTcpResources();

        try
        {
            _tcpClient = new TcpClient { NoDelay = true };
            _tcpClient.SendTimeout    = _config.TimeoutMs;
            _tcpClient.ReceiveTimeout = _config.TimeoutMs;

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_config.TimeoutMs);

            await _tcpClient.ConnectAsync(_config.Host, _config.Port, connectCts.Token);
            _stream = _tcpClient.GetStream();

            UpdateState(ConnectionState.Connected);
            _logger.LogInformation("[{DeviceId}] Connected to {Host}:{Port}.",
                _config.DeviceId, _config.Host, _config.Port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("[{DeviceId}] Connection failed: {Message}",
                _config.DeviceId, ex.Message);

            var snap = _status;
            _status = snap with
            {
                State     = ConnectionState.Disconnected,
                LastError = ex.Message
            };

            DisposeTcpResources();
        }
    }

    // -------------------------------------------------------------------------
    //  Poll — send request, read response
    // -------------------------------------------------------------------------

    private async Task PollDeviceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        UpdateLastAttempt(now);

        try
        {
            // 1. Send request
            var requestBytes = Encoding.UTF8.GetBytes(_config.RequestPayload);
            await _stream!.WriteAsync(requestBytes, ct);
            await _stream.FlushAsync(ct);

            // 2. Read response with timeout
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(_config.TimeoutMs);

            var buffer   = new byte[4096];
            var sb       = new StringBuilder();
            int bytesRead;

            do
            {
                bytesRead = await _stream.ReadAsync(buffer, readCts.Token);
                if (bytesRead > 0)
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            }
            while (bytesRead > 0 && _stream.DataAvailable);

            var rawData = sb.ToString();

            // 3. Build reading and persist
            var reading = new DeviceReading
            {
                DeviceId  = _config.DeviceId,
                Timestamp = now,
                RawData   = rawData
            };

            await _repository.SaveAsync(reading);
            await _fileOutput.AppendAsync(reading);

            // 4. Update status (snapshot record)
            var snap = _status;
            _status = snap with
            {
                State              = ConnectionState.Connected,
                LastSuccessfulRead = now,
                TotalReads         = snap.TotalReads + 1,
                LastError          = null
            };

            _logger.LogDebug("[{DeviceId}] Polled OK — {Bytes} bytes received.",
                _config.DeviceId, Encoding.UTF8.GetByteCount(rawData));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Application shutdown — propagate
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[{DeviceId}] Poll error: {Message}",
                _config.DeviceId, ex.Message);

            // Persist the error record
            var errorReading = new DeviceReading
            {
                DeviceId     = _config.DeviceId,
                Timestamp    = now,
                RawData      = string.Empty,
                ErrorMessage = ex.Message
            };
            await _repository.SaveAsync(errorReading);

            var snap = _status;
            _status = snap with
            {
                State       = ConnectionState.Disconnected,
                TotalErrors = snap.TotalErrors + 1,
                LastError   = ex.Message
            };

            DisposeTcpResources();
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private bool IsConnected() =>
        _tcpClient is { Connected: true } && _stream is not null;

    private void DisposeTcpResources()
    {
        try { _stream?.Dispose(); }   catch { /* ignore */ }
        try { _tcpClient?.Dispose(); } catch { /* ignore */ }
        _stream    = null;
        _tcpClient = null;
    }

    private void UpdateState(ConnectionState state)
    {
        var snap = _status;
        _status = snap with { State = state };
    }

    private void UpdateLastAttempt(DateTime ts)
    {
        var snap = _status;
        _status = snap with { LastAttempt = ts };
    }

    private static async Task SafeDelay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    // -------------------------------------------------------------------------
    //  IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
