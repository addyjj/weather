using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weather.Infrastructure.WeatherHistory;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class TimeZoneHistoryTests(HistoryDatabase database) : IClassFixture<HistoryDatabase>
{
    private Task<WeatherHistoryResult> Query(string zone, params (string Key, string Value)[] options) =>
        database.Services.GetRequiredService<IWeatherHistoryQuery>().ExecuteAsync(
            new QueryWeatherHistoryQuery(options.ToDictionary(x => x.Key, x => x.Value), zone));

    [Theory]
    [InlineData("America/Chicago")]
    [InlineData("Central Standard Time")]
    public async Task ReturnsLocalOffsetWithoutChangingUtcTimestamps(string zone)
    {
        var result = await Query(zone, ("$select", "DateUtc,Date,LocalDate,Year,Month,Day,Hour"),
            ("$filter", "Date eq 2026-01-01T00:00:00Z"));
        var row = Assert.Single(result.Rows);
        Assert.Equal("2026-01-01T00:00:00Z", row["Date"].GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z").ToUnixTimeMilliseconds(), row["DateUtc"].GetInt64());
        Assert.Equal("2025-12-31T18:00:00-06:00", row["LocalDate"].GetString());
        Assert.Equal(2025, row["Year"].GetInt32());
        Assert.Equal(12, row["Month"].GetInt32());
        Assert.Equal(31, row["Day"].GetInt32());
        Assert.Equal(18, row["Hour"].GetInt32());
    }

    [Fact]
    public async Task GroupsAndFiltersByLocalCalendarDayInSql()
    {
        database.Capture.Commands.Clear();
        var result = await Query("America/Chicago",
            ("$apply", "filter(Year eq 2025 and Month eq 12 and Day eq 31)/groupby((Year,Month,Day),aggregate(TempOut with average as Avg,$count as Samples))"),
            ("$count", "true"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(4, row["Samples"].GetInt64());
        Assert.Equal(32, row["Avg"].GetDouble());
        Assert.Equal(1, result.TotalCount);
        Assert.Contains(database.Capture.Commands, sql => sql.Contains("AT TIME ZONE") && sql.Contains("GROUP BY"));
    }

    [Fact]
    public async Task ChicagoDstSpringGapAndFallOverlapPreserveOffsets()
    {
        var dates = new[] { "2026-03-08T07:30:00Z", "2026-03-08T08:30:00Z", "2026-11-01T06:30:00Z", "2026-11-01T07:30:00Z" };
        await using var db = await database.Services.GetRequiredService<IDbContextFactory<WeatherHistoryContext>>().CreateDbContextAsync();
        db.Readings.AddRange(dates.Select(date => new WeatherReading
        {
            DateUtc = DateTimeOffset.Parse(date).ToUnixTimeMilliseconds(),
            Date = DateTimeOffset.Parse(date).UtcDateTime, TempOut = 50, Loc = "DST test"
        }));
        await db.SaveChangesAsync();
        var result = await Query("America/Chicago", ("$filter", "Loc eq 'DST test'"),
            ("$select", "DateUtc,LocalDate,Hour"), ("$orderby", "DateUtc"));
        Assert.Equal(new[] { "2026-03-08T01:30:00-06:00", "2026-03-08T03:30:00-05:00",
            "2026-11-01T01:30:00-05:00", "2026-11-01T01:30:00-06:00" }, result.Rows.Select(x => x["LocalDate"].GetString()));
        Assert.Equal(new[] { 1, 3, 1, 1 }, result.Rows.Select(x => x["Hour"].GetInt32()));
        Assert.Equal(4, result.Rows.Select(x => x["DateUtc"].GetInt64()).Distinct().Count());
    }

    [Fact]
    public async Task UtcDefaultLocalDateHasZeroOffset()
    {
        var result = await database.Query(("$top", "1"), ("$select", "Date,LocalDate"));
        var row = Assert.Single(result.Rows);
        Assert.Equal(TimeSpan.Zero, row["LocalDate"].GetDateTimeOffset().Offset);
        Assert.Equal(row["Date"].GetDateTime(), row["LocalDate"].GetDateTimeOffset().UtcDateTime);
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RejectsUnknownTimeZonesBeforeSql(string zone)
    {
        database.Capture.Commands.Clear();
        var error = await Assert.ThrowsAsync<WeatherHistoryQueryException>(() => Query(zone));
        Assert.Contains("timeZone", error.Message);
        Assert.Empty(database.Capture.Commands);
    }
}
