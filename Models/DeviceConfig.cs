namespace TCPCommunication.Models;

/// <summary>
/// Represents the static configuration for a single TCP device.
/// </summary>
public sealed class DeviceConfig
{
    public string DeviceId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }

    /// <summary>Polling interval in milliseconds. Default 1000 ms.</summary>
    public int PollingIntervalMs { get; set; } = 1000;

    /// <summary>Connect / read timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>Delay before attempting reconnect after failure (ms).</summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>Free-form request payload to send to the device.</summary>
    public string RequestPayload { get; set; } = "GET_DATA\r\n";
}
