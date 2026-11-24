namespace TCPCommunication.Configuration;

using TCPCommunication.Models;

/// <summary>
/// Root configuration section bound from appsettings.json.
/// </summary>
public sealed class TcpAcquisitionOptions
{
    public const string SectionName = "TcpAcquisition";

    /// <summary>Path to the SQLite database file.</summary>
    public string DatabasePath { get; set; } = "data/acquisitions.db";

    /// <summary>
    /// When true, the application also writes per-device CSV flat files
    /// alongside the database records.
    /// </summary>
    public bool EnableFileOutput { get; set; } = false;

    /// <summary>Directory for per-device CSV files.</summary>
    public string FileOutputDirectory { get; set; } = "data/device_logs";

    /// <summary>List of device configurations.</summary>
    public List<DeviceConfig> Devices { get; set; } = new();
}
