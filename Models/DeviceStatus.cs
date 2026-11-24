namespace TCPCommunication.Models;

/// <summary>
/// Live runtime state of a managed device connection.
/// </summary>
public sealed record DeviceStatus
{
    public string DeviceId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    public DateTime? LastSuccessfulRead { get; set; }
    public DateTime? LastAttempt { get; set; }
    public long TotalReads { get; set; }
    public long TotalErrors { get; set; }
    public string? LastError { get; set; }
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Stopped
}
