namespace Weather.UseCases.WeatherHistory.ImportWeatherHistory;

public sealed class ImportWeatherHistoryHandler(IWeatherImportSource source, IWeatherImportStore store, TimeProvider timeProvider)
{
    public async Task<ImportWeatherHistoryResult> HandleAsync(ImportWeatherHistoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.MacAddress);
        var latest = await store.GetLatestTimestampAsync(cancellationToken) ?? 0;
        var cursor = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var pages = 0;
        var readings = new Dictionary<long, WeatherImportReading>();
        while (cursor > latest)
        {
            // Match the original importer's pacing and avoid Ambient's API rate limit.
            if (pages > 0) await Task.Delay(TimeSpan.FromSeconds(4), timeProvider, cancellationToken);
            var page = await source.GetReadingsAsync(command.MacAddress, cursor, cancellationToken);
            pages++;
            if (page.Count == 0) break;
            if (page.Any(x => x.DateUtc <= 0 || x.DateUtc > cursor))
                throw new InvalidOperationException("Ambient history did not advance backward through valid timestamps. No readings were saved.");
            foreach (var reading in page.Where(x => x.DateUtc > latest)) readings.TryAdd(reading.DateUtc, reading);
            cursor = page.Min(x => x.DateUtc) - 1;
        }

        // Commit only after pagination completes, so interrupted imports can be restarted
        // without a partial high-water mark hiding older, unimported pages.
        var added = readings.Count == 0 ? 0 : await store.AddMissingReadingsAsync(readings.Values.ToArray(), cancellationToken);
        return new ImportWeatherHistoryResult(pages, added);
    }
}
