using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weather.UseCases.WeatherHistory.QueryWeatherHistory;

public sealed record WeatherHistoryResult(
    [property: JsonPropertyName("rows")] IReadOnlyList<Dictionary<string, JsonElement>> Rows,
    [property: JsonPropertyName("hasMore")] bool HasMore,
    [property: JsonPropertyName("nextSkip")] int? NextSkip,
    [property: JsonPropertyName("totalCount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TotalCount);
