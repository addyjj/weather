using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Weather.UseCases.Devices;
using Weather.UseCases.Devices.GetDevices;
using Weather.UseCases.Devices.GetDeviceData;

namespace Weather.WebApi.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class DevicesController(GetDevicesHandler getDevices, GetDeviceDataHandler getDeviceData) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceResult>>> GetAsync(CancellationToken cancellationToken) =>
        Ok(await getDevices.HandleAsync(new GetDevicesQuery(), cancellationToken));

    [HttpGet("{macAddress}/data")]
    public async Task<ActionResult<IReadOnlyList<DeviceDataResult>>> GetDeviceDataAsync(
        string macAddress, [FromQuery] DateTime? endDate = null,
        [FromQuery, Range(1, 100)] int limit = 10, CancellationToken cancellationToken = default) =>
        Ok(await getDeviceData.HandleAsync(new GetDeviceDataQuery(macAddress, endDate, limit), cancellationToken));
}
