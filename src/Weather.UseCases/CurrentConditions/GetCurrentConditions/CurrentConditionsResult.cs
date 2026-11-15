using System.Text.Json.Serialization;

namespace Weather.UseCases.CurrentConditions.GetCurrentConditions;

public sealed record CurrentConditionsResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public required double TemperatureFahrenheit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? FeelsLikeFahrenheit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DewPointFahrenheit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? HumidityPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? WindDirectionDegrees { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? WindSpeedMph { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? WindGustMph { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? MaxDailyGustMph { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? RelativePressureInHg { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? AbsolutePressureInHg { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? HourlyRainRateInches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? DailyRainInches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? SolarRadiationWattsPerSquareMeter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? UvIndex { get; init; }
}
