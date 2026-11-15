using Weather.UseCases.WeatherHistory.QueryWeatherHistory;
using Xunit;

namespace Weather.Tests;

public sealed class WeatherHistoryHandlerTests
{
    [Fact]
    public async Task ForwardsQueryAndCancellationAndReturnsResult()
    {
        var query = new QueryWeatherHistoryQuery(new Dictionary<string, string> { ["$top"] = "2" }, "America/Chicago");
        using var cancellation = new CancellationTokenSource();
        var fake = new CapturingQuery();
        var result = await new QueryWeatherHistoryHandler(fake).HandleAsync(query, cancellation.Token);
        Assert.Same(query, fake.Query);
        Assert.Equal(cancellation.Token, fake.Cancellation);
        Assert.Same(fake.Result, result);
    }

    private sealed class CapturingQuery : IWeatherHistoryQuery
    {
        public QueryWeatherHistoryQuery? Query { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public WeatherHistoryResult Result { get; } = new([], false, null, 0);
        public Task<WeatherHistoryResult> ExecuteAsync(QueryWeatherHistoryQuery query, CancellationToken cancellationToken = default)
        {
            Query = query;
            Cancellation = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
