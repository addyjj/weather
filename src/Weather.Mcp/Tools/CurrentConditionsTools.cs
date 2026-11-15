using System.ComponentModel;
using ModelContextProtocol.Server;
using Weather.UseCases.CurrentConditions.GetCurrentConditions;

namespace Weather.Mcp.Tools;

[McpServerToolType]
public sealed class CurrentConditionsTools(GetCurrentConditionsHandler handler)
{
    [McpServerTool(Name = "get_current_conditions", ReadOnly = true, Idempotent = false)]
    [Description("Gets the latest reported outdoor conditions from the configured Ambient Weather station: observation time (UTC), temperature, feels-like and dew point (Fahrenheit), humidity (%), wind direction (degrees), speed and gusts (mph), pressure (inHg), hourly rain rate (inches/hour), daily rain (inches), solar radiation (W/m²), and UV index. Missing optional readings are null. Values are from the station's last upload, not a forecast.")]
    public Task<CurrentConditionsResult> GetCurrentConditions(CancellationToken cancellationToken = default) =>
        handler.HandleAsync(new GetCurrentConditionsQuery(), cancellationToken);
}
