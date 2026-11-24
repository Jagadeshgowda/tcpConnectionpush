namespace TCPCommunication.Models;

/// <summary>
/// Represents a single data record received from a device.
/// </summary>
public sealed class DeviceReading
{
    public long Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RawData { get; set; } = string.Empty;

    /// <summary>Null means successful read; non-null contains the error message.</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => ErrorMessage is null;
}
