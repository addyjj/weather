using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weather.Infrastructure.WeatherHistory;
using Weather.Infrastructure.WeatherHistory.Import;
using Weather.UseCases.WeatherHistory.ImportWeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class WeatherImportHandlerTests
{
    [Fact]
    public async Task PagesBackwardFiltersExistingReadingsAndDeduplicatesBeforeSaving()
    {
        var source = new FakeSource([Reading(900), Reading(800), Reading(800)], [Reading(700), Reading(100)]);
        var store = new FakeStore(100);
        var handler = new ImportWeatherHistoryHandler(source, store, new FixedClock());
        var result = await handler.HandleAsync(new("station"));
        Assert.Equal(new long[] { 1000, 799 }, source.Cursors);
        Assert.Equal(new long[] { 900, 800, 700 }, store.Saved.Select(x => x.DateUtc));
        Assert.Equal(new ImportWeatherHistoryResult(2, 3), result);
    }

    [Fact]
    public async Task EmptyHistoryDoesNotWrite()
    {
        var store = new FakeStore(null);
        var result = await new ImportWeatherHistoryHandler(new FakeSource(Array.Empty<WeatherImportReading>()), store, new FixedClock()).HandleAsync(new("station"));
        Assert.Equal(new ImportWeatherHistoryResult(1, 0), result);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsInvalidUpstreamTimestampsWithoutWriting(long timestamp)
    {
        var store = new FakeStore(null);
        var handler = new ImportWeatherHistoryHandler(new FakeSource([Reading(timestamp)]), store, new FixedClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new("station")));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RequiresAStationBeforeReadingOrWriting(string? station)
    {
        var source = new FakeSource();
        var store = new FakeStore(null);
        var handler = new ImportWeatherHistoryHandler(source, store, new FixedClock());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => handler.HandleAsync(new(station!)));
        Assert.Empty(source.Cursors);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task UpToDateStoreDoesNotRequestHistoryOrWrite()
    {
        var source = new FakeSource();
        var store = new FakeStore(1000);
        var result = await new ImportWeatherHistoryHandler(source, store, new FixedClock()).HandleAsync(new("station"));
        Assert.Equal(new ImportWeatherHistoryResult(0, 0), result);
        Assert.Empty(source.Cursors);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task LaterPageFailureDoesNotSaveBufferedHistory()
    {
        var source = new FakeSource([Reading(900)], [Reading(800), Reading(0)]);
        var store = new FakeStore(null);
        var handler = new ImportWeatherHistoryHandler(source, store, new FixedClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new("station")));
        Assert.Equal(new long[] { 1000, 899 }, source.Cursors);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task CancellationBeforeTheNextPageDoesNotCommitPartialHistory()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new CancelingSource(cancellation);
        var store = new FakeStore(null);
        var handler = new ImportWeatherHistoryHandler(source, store, new FixedClock());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new("station"), cancellation.Token));
        Assert.Equal(0, store.Writes);
    }

    private static WeatherImportReading Reading(long timestamp) => new() { DateUtc = timestamp };
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(1000);
    }
    private sealed class FakeSource(params WeatherImportReading[][] pages) : IWeatherImportSource
    {
        public List<long> Cursors { get; } = [];
        public Task<IReadOnlyList<WeatherImportReading>> GetReadingsAsync(string macAddress, long endDateUtc, CancellationToken cancellationToken)
        {
            Cursors.Add(endDateUtc);
            return Task.FromResult<IReadOnlyList<WeatherImportReading>>(pages[Cursors.Count - 1]);
        }
    }
    private sealed class CancelingSource(CancellationTokenSource cancellation) : IWeatherImportSource
    {
        public Task<IReadOnlyList<WeatherImportReading>> GetReadingsAsync(string macAddress, long endDateUtc, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<WeatherImportReading>>([Reading(500)]);
        }
    }
    private sealed class FakeStore(long? latest) : IWeatherImportStore
    {
        public int Writes { get; private set; }
        public IReadOnlyList<WeatherImportReading> Saved { get; private set; } = [];
        public Task<long?> GetLatestTimestampAsync(CancellationToken cancellationToken) => Task.FromResult(latest);
        public Task<int> AddMissingReadingsAsync(IReadOnlyList<WeatherImportReading> readings, CancellationToken cancellationToken)
        {
            Writes++;
            Saved = readings;
            return Task.FromResult(readings.Count);
        }
    }
}

public sealed class WeatherImportIntegrationTests : IClassFixture<HistoryDatabase>
{
    private readonly HistoryDatabase database;
    public WeatherImportIntegrationTests(HistoryDatabase database) => this.database = database;

    [Fact]
    public async Task ConsoleImportsEveryColumnAndRerunsSafelyWithAtomicConcurrentWrites()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var factory = database.Services.GetRequiredService<IDbContextFactory<WeatherHistoryContext>>();
        await using var db = await factory.CreateDbContextAsync(timeout.Token);
        await db.Readings.ExecuteDeleteAsync(timeout.Token);
        var lastDate = DateTime.UtcNow.AddDays(-1);
        lastDate = new DateTime(lastDate.Ticks - lastDate.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var latest = new DateTimeOffset(lastDate).ToUnixTimeMilliseconds();
        db.Readings.Add(new WeatherReading { DateUtc = latest, Date = lastDate });
        await db.SaveChangesAsync(timeout.Token);
        var expected = new WeatherImportReading
        {
            DateUtc = latest + 1000, Date = lastDate.AddSeconds(1), TempIn = 71, HumidityIn = 43,
            BaromRel = 30.1, BaromAbs = 29.5, TempOut = 68, BattOut = true, HumidityOut = 55,
            WindDir = 270, WindSpeed = 3.5, WindGust = 8, MaxDailyGust = 12, HourlyRainRate = .01,
            EventRain = .1, DailyRain = .2, WeeklyRain = .3, MonthlyRain = .4, TotalRain = .5,
            SolarRadiation = 125, Uv = 2, BattCo2 = false, FeelsLike = 67.5, DewPoint = 51,
            FeelsLikeIn = 70, DewPointIn = 48, Loc = "Backyard"
        };
        var sample = new
        {
            dateutc = expected.DateUtc, date = expected.Date, tempinf = 71, humidityin = 43,
            baromrelin = 30.1, baromabsin = 29.5, tempf = 68, battout = 1, humidity = 55,
            winddir = 270, windspeedmph = 3.5, windgustmph = 8, maxdailygust = 12, hourlyrainin = .01,
            eventrainin = .1, dailyrainin = .2, weeklyrainin = .3, monthlyrainin = .4, totalrainin = .5,
            solarradiation = 125, uv = 2, batt_co2 = false, feelsLike = 67.5, dewPoint = 51,
            feelsLikein = 70, dewPointin = 48, loc = "Backyard"
        };
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var ambient = new HttpListener();
        var ambientUrl = $"http://127.0.0.1:{port}/";
        ambient.Prefixes.Add(ambientUrl);
        ambient.Start();
        var serve = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var context = await ambient.GetContextAsync().WaitAsync(timeout.Token);
                try
                {
                    Assert.Equal("/devices/test-station", context.Request.Url!.AbsolutePath);
                    Assert.Equal("test-api", context.Request.QueryString["apiKey"]);
                    Assert.Equal("test-app", context.Request.QueryString["applicationKey"]);
                    Assert.True(long.Parse(context.Request.QueryString["endDate"]!) >= expected.DateUtc);
                    Assert.Null(context.Request.QueryString["limit"]);
                    context.Response.ContentType = "application/json";
                    var json = "[" + JsonSerializer.Serialize(sample) + "," +
                        JsonSerializer.Serialize(new { dateutc = latest, date = lastDate }) + "]";
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json), timeout.Token);
                }
                finally { context.Response.Close(); }
            }
        });
        Assert.Contains("1 readings added", await RunImportAsync(ambientUrl, timeout.Token));
        Assert.Contains("0 readings added", await RunImportAsync(ambientUrl, timeout.Token));
        await serve;
        var stored = await db.Readings.AsNoTracking().SingleAsync(x => x.DateUtc == expected.DateUtc, timeout.Token);
        foreach (var property in typeof(WeatherImportReading).GetProperties())
            Assert.Equal(property.GetValue(expected), typeof(WeatherReading).GetProperty(property.Name)!.GetValue(stored));

        var store = new SqlWeatherImportStore(factory);
        var extra = expected with { DateUtc = expected.DateUtc + 1000, Date = expected.Date.AddSeconds(1) };
        var concurrent = await Task.WhenAll(store.AddMissingReadingsAsync([extra], timeout.Token), store.AddMissingReadingsAsync([extra], timeout.Token));
        Assert.Equal(1, concurrent.Sum());
        var invalid = extra with { DateUtc = extra.DateUtc + 1000, Loc = new string('x', 101) };
        await Assert.ThrowsAsync<DbUpdateException>(() => store.AddMissingReadingsAsync(
            [extra with { DateUtc = invalid.DateUtc + 1000 }, invalid], timeout.Token));
        Assert.Equal(3, await db.Readings.CountAsync(timeout.Token));
    }

    private async Task<string> RunImportAsync(string ambientUrl, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(root, "src", "Weather.Import", "bin", configuration, "net10.0", "Weather.Import.dll");
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(dll);
        info.Environment["ConnectionStrings__sql"] = database.ConnectionString;
        info.Environment["Ambient__ApiUrl"] = ambientUrl;
        info.Environment["Ambient__ApiKey"] = "test-api";
        info.Environment["Ambient__ApplicationKey"] = "test-app";
        info.Environment["Ambient__DeviceMacAddress"] = "test-station";
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
