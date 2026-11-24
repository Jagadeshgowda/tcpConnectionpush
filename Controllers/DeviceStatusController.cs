namespace TCPCommunication.Controllers;

using Microsoft.AspNetCore.Mvc;
using TCPCommunication.Data;
using TCPCommunication.Models;
using TCPCommunication.Services;

/// <summary>
/// REST API for monitoring device status and querying historical readings.
/// </summary>
[ApiController]
[Route("api/devices")]
public sealed class DeviceStatusController : ControllerBase
{
    private readonly DeviceManagerService _manager;
    private readonly DeviceReadingRepository _repository;

    public DeviceStatusController(
        DeviceManagerService manager,
        DeviceReadingRepository repository)
    {
        _manager    = manager;
        _repository = repository;
    }

    // GET api/devices
    /// <summary>Returns live connection status for all configured devices.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceStatus>), 200)]
    public IActionResult GetAll() => Ok(_manager.GetAllStatuses());

    // GET api/devices/{deviceId}
    /// <summary>Returns live connection status for a single device.</summary>
    [HttpGet("{deviceId}")]
    [ProducesResponseType(typeof(DeviceStatus), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetOne(string deviceId)
    {
        var status = _manager.GetStatus(deviceId);
        return status is null ? NotFound() : Ok(status);
    }

    // GET api/devices/{deviceId}/readings?count=50
    /// <summary>Returns the most recent readings for a device.</summary>
    [HttpGet("{deviceId}/readings")]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceReading>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetReadings(
        string deviceId,
        [FromQuery] int count = 50)
    {
        if (_manager.GetStatus(deviceId) is null)
            return NotFound();

        var readings = await _repository.GetRecentAsync(deviceId, Math.Clamp(count, 1, 1000));
        return Ok(readings);
    }

    // GET api/devices/{deviceId}/stats
    /// <summary>Returns aggregate statistics for a device.</summary>
    [HttpGet("{deviceId}/stats")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetStats(string deviceId)
    {
        var status = _manager.GetStatus(deviceId);
        if (status is null)
            return NotFound();

        var total = await _repository.GetTotalCountAsync(deviceId);
        return Ok(new
        {
            status.DeviceId,
            status.State,
            status.TotalReads,
            status.TotalErrors,
            status.LastSuccessfulRead,
            status.LastAttempt,
            status.LastError,
            TotalStoredRecords = total
        });
    }
}
