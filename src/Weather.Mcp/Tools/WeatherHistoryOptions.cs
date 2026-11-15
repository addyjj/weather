using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Mcp.Tools;

// Typed MCP contract; the shared query pipeline still accepts standard OData options.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WeatherHistoryOptions
{
    [JsonPropertyName("timeZone"), Description("User's timezone, for example America/Chicago. Defaults to UTC; never inferred from the server. " +
        "LocalDate includes the applicable UTC offset and daylight-saving adjustment. Year/Month/Day/Hour filters and groups use this timezone. " +
        "Date and DateUtc remain UTC. Select LocalDate to display local time. Keep timeZone unchanged when paging.")]
    public string? TimeZone { get; init; }

    [JsonPropertyName("filter"), Description("OData filter expression, for example TempOut lt 32 or Date ge 2026-01-01T00:00:00Z. " +
        "When apply is supplied, filters only fields retained in its results, including aggregate aliases. " +
        "To filter source readings before aggregation, use filter(...) inside apply; do not duplicate that filter here. Omit for no additional filter.")]
    public string? Filter { get; init; }

    [JsonPropertyName("select"), Description("Comma-separated fields or aggregate aliases to return, for example Date,TempOut,HumidityOut. " +
        "Use exact case-sensitive field names. Omit to return all result fields.")]
    public string? Select { get; init; }

    [JsonPropertyName("orderBy"), Description("Comma-separated OData sort expressions, for example TempOut desc or AverageTemperature desc,Year,Month,Day. " +
        "Stable identity tie-breakers are added automatically. Defaults to DateUtc ascending for readings or grouping keys for grouped results.")]
    public string? OrderBy { get; init; }

    [JsonPropertyName("apply"), Description("OData transformation pipeline, for example filter(Year eq 2026)/groupby((Year,Month,Day),aggregate(TempOut with average as AverageTemperature,$count as Samples)). " +
        "Supports min, max, average, sum, countdistinct, and $count as Alias. Calendar grouping uses timeZone (UTC by default); include Year,Month,Day for daily groups. " +
        "Averages are sample averages. Summing cumulative rain readings does not calculate rainfall totals. Omit for individual readings.")]
    public string? Apply { get; init; }

    [JsonPropertyName("compute"), Description("OData computed expression over reading fields, for example TempOut sub 32 as AboveFreezing. " +
        "Computed aliases can be selected, filtered, or sorted. Sorting computed expressions over aggregate aliases is unsupported. Omit for no computed fields.")]
    public string? Compute { get; init; }

    [JsonPropertyName("top"), Description("Page size, 1–1000; defaults to 100.")]
    public int? Top { get; init; }

    [JsonPropertyName("skip"), Description("Non-negative number of result rows or groups to skip after filtering, aggregation, and sorting; defaults to zero. " +
        "For the next page, use the returned nextSkip value and keep all other options unchanged. Stop when hasMore is false.")]
    public int? Skip { get; init; }

    [JsonPropertyName("count"), Description("Include the total matching reading/group count before paging; defaults to false.")]
    public bool? Count { get; init; }

    internal QueryWeatherHistoryQuery ToQuery()
    {
        var options = new Dictionary<string, string>();
        Add("$filter", Filter);
        Add("$select", Select);
        Add("$orderby", OrderBy);
        Add("$apply", Apply);
        Add("$compute", Compute);
        Add("$top", Top?.ToString(CultureInfo.InvariantCulture));
        Add("$skip", Skip?.ToString(CultureInfo.InvariantCulture));
        Add("$count", Count?.ToString().ToLowerInvariant());
        return new QueryWeatherHistoryQuery(options, TimeZone ?? "UTC");

        void Add(string name, string? value)
        {
            if (value is not null) options[name] = value;
        }
    }
}
