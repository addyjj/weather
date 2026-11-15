using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Weather.Infrastructure.WeatherHistory;
using Weather.Mcp.Tools;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class WeatherHistoryTests(HistoryDatabase database) : IClassFixture<HistoryDatabase>
{
    [Fact]
    public async Task MapsAllColumnsAndProjectsUtcCalendarParts()
    {
        var result = await database.Query(("$top", "1"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(32, row.Count);
        Assert.Equal(-4, row["TempOut"].GetDouble());
        Assert.Equal(2025, row["Year"].GetInt32());
        Assert.Equal(12, row["Month"].GetInt32());
        Assert.Equal(31, row["Day"].GetInt32());
        Assert.Equal(23, row["Hour"].GetInt32());
        Assert.EndsWith("Z", row["Date"].GetString());
        Assert.True(result.HasMore);
        Assert.Equal(1, result.NextSkip);
        Assert.Null(result.TotalCount);
    }

    [Fact]
    public async Task FiltersSelectsComputesSortsAndCounts()
    {
        var result = await database.Query(("$filter", "Year eq 2026 and TempOut ge 50"),
            ("$compute", "TempOut sub 32 as AboveFreezing"), ("$select", "TempOut,AboveFreezing"),
            ("$orderby", "TempOut desc"), ("$count", "true"));
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(new double[] { 86, 68, 50, 50 }, result.Rows.Select(x => x["TempOut"].GetDouble()));
        Assert.Equal(54, result.Rows[0]["AboveFreezing"].GetDouble());
        Assert.DoesNotContain("HumidityOut", result.Rows[0].Keys);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task ComputeWorksWithoutSelectAndCanBeFilteredAndSorted()
    {
        var result = await database.Query(("$compute", "TempOut sub 32 as AboveFreezing"),
            ("$filter", "AboveFreezing gt 0"), ("$orderby", "AboveFreezing desc"));
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(54, result.Rows[0]["AboveFreezing"].GetDouble());
    }

    [Fact]
    public async Task FiltersUtcDatesAndStringFields()
    {
        var result = await database.Query(("$filter", "Date ge 2026-01-01T00:00:00Z and Date lt 2026-02-01T00:00:00Z and Loc eq 'North'"),
            ("$select", "Date,TempOut"));
        Assert.Equal(new double[] { 50, 68 }, result.Rows.Select(x => x["TempOut"].GetDouble()));
    }

    [Fact]
    public async Task RejectsExcessiveQueryComplexity()
    {
        database.Capture.Commands.Clear();
        var expression = string.Join(" or ", Enumerable.Repeat("TempOut gt 0", 80));
        await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => database.Query(("$filter", expression)));
        await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => database.Query(("$filter", new string('a', 16001))));
        Assert.Empty(database.Capture.Commands);
    }

    [Fact]
    public async Task DefaultAndMaximumPageSizesAreAccepted()
    {
        Assert.Equal(8, (await database.Query()).Rows.Count);
        Assert.Equal(8, (await database.Query(("$top", "1000"))).Rows.Count);
        var beyondEnd = await database.Query(("$skip", "8"));
        Assert.Empty(beyondEnd.Rows);
        Assert.False(beyondEnd.HasMore);
    }

    [Fact]
    public async Task AggregatesAreExecutedInSql()
    {
        database.Capture.Commands.Clear();
        var result = await database.Query(("$apply", "aggregate(TempOut with min as Min,TempOut with max as Max,TempOut with average as Avg,TempOut with sum as Sum,TempOut with countdistinct as Distinct,$count as Samples)"), ("$count", "true"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(-4, row["Min"].GetDouble());
        Assert.Equal(86, row["Max"].GetDouble());
        Assert.Equal(41, row["Avg"].GetDouble());
        Assert.Equal(328, row["Sum"].GetDouble());
        Assert.Equal(6, row["Distinct"].GetInt64());
        Assert.Equal(8, row["Samples"].GetInt64());
        Assert.Equal(1, result.TotalCount);
        Assert.Contains(database.Capture.Commands, sql => sql.Contains("AVG(") && sql.Contains("SUM(") && sql.Contains("GROUP BY"));
        Assert.DoesNotContain(database.Capture.Commands, sql => sql.Contains("OFFSET") || sql.Contains("TOP("));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(10, 0)]
    public async Task UngroupedAggregatePreservesPagingAndCountWithoutSqlRowLimits(int skip, int expectedRows)
    {
        database.Capture.Commands.Clear();
        var result = await database.Query(
            ("$apply", "aggregate(Date with min as First,Date with max as Last,$count as Samples)"),
            ("$top", "1"), ("$skip", skip.ToString()), ("$count", "true"));
        Assert.Equal(expectedRows, result.Rows.Count);
        Assert.Equal(1, result.TotalCount);
        Assert.False(result.HasMore);
        Assert.Null(result.NextSkip);
        if (skip == 0) Assert.Equal(8, Assert.Single(result.Rows)["Samples"].GetInt64());
        Assert.DoesNotContain(database.Capture.Commands, sql => sql.Contains("OFFSET") || sql.Contains("TOP("));
    }

    [Fact]
    public async Task GroupsUtcDaysAndSortsByAggregate()
    {
        var result = await database.Query(("$apply", "filter(Year eq 2026)/groupby((Year,Month,Day),aggregate(TempOut with average as AverageTemperature,$count as Samples))"),
            ("$orderby", "AverageTemperature desc"), ("$count", "true"));
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(new double[] { 86, 68, 44, 32 }, result.Rows.Select(x => x["AverageTemperature"].GetDouble()));
        Assert.Equal(3, result.Rows[2]["Samples"].GetInt64());
        Assert.Contains(database.Capture.Commands, sql => sql.Contains("DATEPART") && sql.Contains("GROUP BY"));
    }

    [Fact]
    public async Task GroupsUtcHoursAndFiltersAggregateResults()
    {
        var result = await database.Query(("$apply", "filter(Year eq 2026 and Month eq 1 and Day eq 1)/groupby((Year,Month,Day,Hour),aggregate(TempOut with average as Avg))"),
            ("$filter", "Avg gt 40"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, row["Hour"].GetInt32());
        Assert.Equal(50, row["Avg"].GetDouble());
    }

    [Fact]
    public async Task DuplicatedSourceFilterExplainsHowToCorrectGroupedQuery()
    {
        const string filter = "Date ge 2026-06-01T05:00:00Z and Date lt 2026-09-01T05:00:00Z";
        const string apply = "filter(" + filter + ")/groupby((Year,Month,Day,Hour),aggregate(TempOut with average as Temp,TempOut with max as High,TempOut with min as Low,WindGust with max as Gust,HourlyRainRate with max as RainRate,BaromRel with average as Pressure))";
        database.Capture.Commands.Clear();
        var error = await Assert.ThrowsAsync<WeatherHistoryQueryException>(() =>
            database.Query(("$apply", apply), ("$filter", filter), ("$top", "1000")));
        Assert.Contains("top-level filter runs after apply", error.Message);
        Assert.Contains("remove a duplicated top-level filter", error.Message);
        Assert.Empty(database.Capture.Commands);
        var corrected = await database.Query(("$apply", apply), ("$top", "1000"));
        Assert.Empty(corrected.Rows);
        Assert.NotEmpty(database.Capture.Commands);
    }

    [Fact]
    public async Task SelectsAggregateAliases()
    {
        var result = await database.Query(("$apply", "groupby((Year),aggregate(TempOut with average as Avg))"),
            ("$select", "Year,Avg"),
            ("$filter", "Avg gt 0"), ("$orderby", "Avg desc"), ("$count", "true"));
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(53, result.Rows[0]["Avg"].GetDouble());
        Assert.Equal(2026, result.Rows[0]["Year"].GetInt32());
    }

    [Fact]
    public async Task UnsupportedComputedAggregateShapeReturnsUsefulErrorWithoutReadingRows()
    {
        database.Capture.Commands.Clear();
        var error = await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => database.Query(
            ("$apply", "groupby((Year),aggregate(TempOut with average as Avg))"),
            ("$compute", "Avg sub 32 as AboveFreezing"), ("$orderby", "AboveFreezing")));
        Assert.Contains("aggregate aliases", error.Message);
        Assert.Empty(database.Capture.Commands);
    }

    [Fact]
    public async Task ReadingPagesAreStableEvenWhenSelectedSortValuesTie()
    {
        var ids = new List<long>();
        var skip = 0;
        do
        {
            var result = await database.Query(("$select", "DateUtc,TempOut"), ("$orderby", "TempOut"),
                ("$top", "2"), ("$skip", skip.ToString()), ("$count", "true"));
            Assert.Equal(8, result.TotalCount);
            ids.AddRange(result.Rows.Select(x => x["DateUtc"].GetInt64()));
            if (!result.HasMore) { Assert.Null(result.NextSkip); break; }
            skip = result.NextSkip!.Value;
        } while (skip < 20);
        Assert.Equal(8, ids.Count);
        Assert.Equal(8, ids.Distinct().Count());
        var expected = await database.Query(("$select", "DateUtc"), ("$orderby", "TempOut"));
        Assert.Equal(expected.Rows.Select(x => x["DateUtc"].GetInt64()), ids);
    }

    [Fact]
    public async Task ExplicitIdentityOrderingDoesNotDuplicateTieBreakers()
    {
        var readings = await database.Query(("$orderby", "DateUtc desc"), ("$top", "1"));
        Assert.Equal(2027, Assert.Single(readings.Rows)["Year"].GetInt32());
        var groups = await database.Query(("$apply", "groupby((Year,Month,Day),aggregate(TempOut with average as Avg))"),
            ("$orderby", "Avg desc,Year,Month,Day"));
        Assert.Equal(6, groups.Rows.Count);
    }

    [Fact]
    public async Task AggregatePagesMatchUnpagedResultsAndCountGroups()
    {
        const string apply = "groupby((Year),aggregate(TempOut with average as Avg))";
        var all = await database.Query(("$apply", apply), ("$orderby", "Avg desc"));
        var rows = new List<Dictionary<string, JsonElement>>();
        for (var skip = 0; skip < 3; skip++)
        {
            var page = await database.Query(("$apply", apply), ("$orderby", "Avg desc"),
                ("$top", "1"), ("$skip", skip.ToString()), ("$count", "true"));
            Assert.Equal(3, page.TotalCount);
            rows.Add(Assert.Single(page.Rows));
            Assert.Equal(skip < 2, page.HasMore);
        }
        Assert.Equal(all.Rows.Select(x => x["Avg"].GetDouble()), rows.Select(x => x["Avg"].GetDouble()));
    }

    [Fact]
    public async Task EmptyReadingAndGroupedResultsAreWellFormed()
    {
        var empty = await database.Query(("$filter", "TempOut gt 200"), ("$count", "true"));
        Assert.Empty(empty.Rows);
        Assert.Equal(0, empty.TotalCount);
        Assert.False(empty.HasMore);
        var grouped = await database.Query(("$apply", "filter(TempOut gt 200)/groupby((Year),aggregate($count as Samples))"));
        Assert.Empty(grouped.Rows);
    }

    [Theory]
    [InlineData("$expand", "Anything")]
    [InlineData("$filter", "UnknownField eq 1")]
    [InlineData("$filter", "TempOut gt")]
    [InlineData("$select", "Secret")]
    [InlineData("$orderby", "UnknownField")]
    [InlineData("$apply", "aggregate(UnknownField with sum as Total)")]
    [InlineData("$top", "0")]
    [InlineData("$top", "1001")]
    [InlineData("$top", "-1")]
    [InlineData("$skip", "-1")]
    [InlineData("$skip", "2147483647")]
    [InlineData("$count", "maybe")]
    public async Task RejectsInvalidQueriesBeforeExecutingSql(string key, string value)
    {
        database.Capture.Commands.Clear();
        await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => database.Query((key, value)));
        Assert.Empty(database.Capture.Commands);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Services.GetRequiredService<IWeatherHistoryQuery>().ExecuteAsync(
                new QueryWeatherHistoryQuery(new Dictionary<string, string>()), cancellation.Token));
    }

    [Fact]
    public async Task MissingDatabaseConfigurationIsSanitized()
    {
        await using var services = new ServiceCollection().AddLogging().AddWeatherHistory(null).BuildServiceProvider();
        var ex = await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => services.GetRequiredService<IWeatherHistoryQuery>()
            .ExecuteAsync(new QueryWeatherHistoryQuery(new Dictionary<string, string>())));
        Assert.DoesNotContain("Server=", ex.ToString());
        Assert.Contains("configuration", ex.Message);
    }

    [Fact]
    public async Task UnreachableDatabaseIsSanitized()
    {
        const string connection = "Server=127.0.0.1,1;Database=weather;User ID=secret-user;Password=secret-password;Connect Timeout=1;Encrypt=false";
        await using var services = new ServiceCollection().AddLogging().AddWeatherHistory(connection).BuildServiceProvider();
        var error = await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => services.GetRequiredService<IWeatherHistoryQuery>()
            .ExecuteAsync(new QueryWeatherHistoryQuery(new Dictionary<string, string>())));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Contains("database request failed", error.Message);
    }

    [Fact]
    public async Task RealHttpMcpProcessQueriesLocalDbAndReturnsUsefulErrors()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var server = await HttpMcpServer.StartAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings__sql"] = database.ConnectionString
        }, timeout.Token);
        await using var client = await server.ConnectAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, x => x.Name == "query_weather_history");
        Assert.Contains(tools, x => x.Name == "get_current_conditions");
        var historyTool = Assert.Single(tools, x => x.Name == "query_weather_history");
        Assert.Contains("empty object", historyTool.JsonSchema.GetProperty("properties").GetProperty("options").GetProperty("description").GetString());
        var optionProperties = historyTool.JsonSchema.GetProperty("properties").GetProperty("options").GetProperty("properties");
        Assert.True(optionProperties.TryGetProperty("filter", out _));
        Assert.True(optionProperties.TryGetProperty("orderBy", out _));
        Assert.False(optionProperties.TryGetProperty("$filter", out _));
        Assert.Contains("integer", optionProperties.GetProperty("top").GetProperty("type").ToString());
        Assert.Contains("boolean", optionProperties.GetProperty("count").GetProperty("type").ToString());
        foreach (var property in optionProperties.EnumerateObject())
            Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()),
                $"Missing MCP input description for {property.Name}");
        Assert.Contains("groupby", optionProperties.GetProperty("apply").GetProperty("description").GetString());
        Assert.Contains("nextSkip", optionProperties.GetProperty("skip").GetProperty("description").GetString());
        Assert.Contains("America/Chicago", optionProperties.GetProperty("timeZone").GetProperty("description").GetString());
        var response = await client.CallToolAsync("query_weather_history", new Dictionary<string, object?>
        {
            ["options"] = new WeatherHistoryOptions
            {
                Apply = "groupby((Year),aggregate(TempOut with average as Avg))",
                OrderBy = "Avg desc", Top = 1, Count = true
            }
        }, cancellationToken: timeout.Token);
        Assert.False(response.IsError is true, string.Join("\n", response.Content.OfType<TextContentBlock>().Select(x => x.Text)));
        var json = JsonDocument.Parse(Assert.Single(response.Content.OfType<TextContentBlock>()).Text);
        var result = JsonSerializer.Deserialize<WeatherHistoryResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(53, Assert.Single(result.Rows)["Avg"].GetDouble());
        Assert.True(result.HasMore);
        Assert.Equal(1, result.NextSkip);
        Assert.Equal(3, result.TotalCount);
        var second = await CallHistoryAsync(client, new()
        {
            Apply = "groupby((Year),aggregate(TempOut with average as Avg))",
            OrderBy = "Avg desc", Top = 1, Skip = result.NextSkip!.Value
        }, timeout.Token);
        Assert.Equal(14, Assert.Single(second.Rows)["Avg"].GetDouble());
        var readingIds = new List<long>();
        var skip = 0;
        while (true)
        {
            var page = await CallHistoryAsync(client, new()
            {
                Filter = "Year eq 2026", Select = "DateUtc,TempOut", OrderBy = "TempOut desc",
                Top = 2, Skip = skip, Count = true
            }, timeout.Token);
            Assert.Equal(6, page.TotalCount);
            readingIds.AddRange(page.Rows.Select(x => x["DateUtc"].GetInt64()));
            if (!page.HasMore) break;
            skip = page.NextSkip!.Value;
            Assert.True(skip < 10);
        }
        Assert.Equal(6, readingIds.Count);
        Assert.Equal(6, readingIds.Distinct().Count());
        var empty = await CallHistoryAsync(client, new() { Filter = "TempOut gt 200", Count = true }, timeout.Token);
        Assert.Empty(empty.Rows);
        Assert.Equal(0, empty.TotalCount);
        var defaults = await CallHistoryAsync(client, new(), timeout.Token);
        Assert.Equal(8, defaults.Rows.Count);
        Assert.Null(defaults.TotalCount);
        var computed = await CallHistoryAsync(client, new()
        {
            Compute = "TempOut sub 32 as AboveFreezing", Select = "AboveFreezing", OrderBy = "TempOut desc", Top = 1
        }, timeout.Token);
        Assert.Equal(54, Assert.Single(computed.Rows)["AboveFreezing"].GetDouble());
        var local = await CallHistoryAsync(client, new()
        {
            TimeZone = "America/Chicago", Select = "Date,LocalDate,Year,Month,Day", OrderBy = "DateUtc desc", Top = 1
        }, timeout.Token);
        var localRow = Assert.Single(local.Rows);
        Assert.Equal("2027-01-01T00:00:00Z", localRow["Date"].GetString());
        Assert.Equal("2026-12-31T18:00:00-06:00", localRow["LocalDate"].GetString());
        Assert.Equal(2026, localRow["Year"].GetInt32());
        var localGroups = await CallHistoryAsync(client, new()
        {
            TimeZone = "America/Chicago",
            Apply = "filter(Year eq 2025 and Month eq 12 and Day eq 31)/groupby((Year,Month,Day),aggregate($count as Samples))"
        }, timeout.Token);
        Assert.Equal(4, Assert.Single(localGroups.Rows)["Samples"].GetInt64());
        const string summerFilter = "Date ge 2026-06-01T05:00:00Z and Date lt 2026-09-01T05:00:00Z";
        const string summerApply = "filter(" + summerFilter + ")/groupby((Year,Month,Day,Hour),aggregate(TempOut with average as Temp,WindGust with max as Gust,HourlyRainRate with max as RainRate,BaromRel with average as Pressure))";
        var duplicatedFilter = await client.CallToolAsync("query_weather_history", new Dictionary<string, object?>
        {
            ["options"] = new WeatherHistoryOptions
            {
                Filter = summerFilter, Apply = summerApply, TimeZone = "America/Chicago", Top = 1000
            }
        }, cancellationToken: timeout.Token);
        Assert.True(duplicatedFilter.IsError);
        Assert.Contains("remove a duplicated top-level filter",
            string.Join("\n", duplicatedFilter.Content.OfType<TextContentBlock>().Select(x => x.Text)));
        var correctedSummer = await CallHistoryAsync(client, new()
        {
            Apply = summerApply, TimeZone = "America/Chicago", Top = 1000, Skip = 2000
        }, timeout.Token);
        Assert.Empty(correctedSummer.Rows);
        Assert.False(correctedSummer.HasMore);
        var oldContract = await client.CallToolAsync("query_weather_history", new Dictionary<string, object?>
        {
            ["options"] = new Dictionary<string, string> { ["$filter"] = "TempOut lt 32" }
        }, cancellationToken: timeout.Token);
        Assert.True(oldContract.IsError);
        var invalid = await client.CallToolAsync("query_weather_history", new Dictionary<string, object?>
        {
            ["options"] = new WeatherHistoryOptions { Filter = "NoSuchField eq 1" }
        }, cancellationToken: timeout.Token);
        Assert.True(invalid.IsError);
        Assert.Contains("NoSuchField", string.Join("\n", invalid.Content.OfType<TextContentBlock>().Select(x => x.Text)));
        // Console output is consumed concurrently; allow its reader to catch up to the response.
        await WaitForLogAsync(server, line => line.Contains("query_weather_history") && line.Contains("NoSuchField"), timeout.Token);
        Assert.Contains(server.LogLines, line => line.Contains("Weather.Mcp.ToolCalls") && line.Contains("groupby((Year)") && line.Contains("Avg desc"));
        Assert.Contains(server.LogLines, line => line.Contains("Weather.Mcp.ToolCalls") && line.Contains("\"top\":1") && line.Contains("\"count\":true"));
        Assert.DoesNotContain(server.LogLines, line => line.Contains(database.ConnectionString));
    }

    private static async Task WaitForLogAsync(HttpMcpServer server, Func<string, bool> predicate, CancellationToken ct)
    {
        while (!server.LogLines.Any(predicate)) await Task.Delay(20, ct);
    }

    private static async Task<WeatherHistoryResult> CallHistoryAsync(McpClient client, WeatherHistoryOptions options, CancellationToken ct)
    {
        var response = await client.CallToolAsync("query_weather_history",
            new Dictionary<string, object?> { ["options"] = options }, cancellationToken: ct);
        Assert.False(response.IsError is true, string.Join("\n", response.Content.OfType<TextContentBlock>().Select(x => x.Text)));
        return JsonSerializer.Deserialize<WeatherHistoryResult>(Assert.Single(response.Content.OfType<TextContentBlock>()).Text)!;
    }
}
