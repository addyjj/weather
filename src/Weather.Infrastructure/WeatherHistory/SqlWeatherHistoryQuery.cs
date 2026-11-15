using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory;

public sealed class SqlWeatherHistoryQuery(IDbContextFactory<WeatherHistoryContext> contexts,
    WeatherHistoryOData odata, IServiceScopeFactory scopes) : IWeatherHistoryQuery
{
    public async Task<WeatherHistoryResult> ExecuteAsync(QueryWeatherHistoryQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var sqlTimeZone = WeatherTimeZone.ToSqlServerId(query.TimeZone);
            var prepared = odata.Prepare(db.QueryReadings(sqlTimeZone), query.Options, scope.ServiceProvider);
            return await ExecutePreparedAsync(prepared, cancellationToken);
        }
        catch (ODataException ex)
        {
            var guidance = query.Options.ContainsKey("$apply") && query.Options.ContainsKey("$filter")
                ? " The top-level filter runs after apply and can only reference fields in its result. " +
                  "Put source-reading filters (such as Date ranges) inside apply before groupby/aggregate; " +
                  "remove a duplicated top-level filter."
                : "";
            throw new WeatherHistoryQueryException("Invalid OData query: " + ex.Message + guidance);
        }
        catch (SqlException)
        {
            throw new WeatherHistoryQueryException("Weather history database request failed. Check SQL configuration and availability.");
        }
        catch (InvalidOperationException)
        {
            throw new WeatherHistoryQueryException("The history query could not be executed. Check SQL configuration or simplify expressions to supported SQL operations.");
        }
        catch (NotSupportedException)
        {
            throw new WeatherHistoryQueryException("This query expression is not supported by the SQL history provider.");
        }
        catch (ArgumentException ex) when (ex.ParamName == "propertyName")
        {
            throw new WeatherHistoryQueryException("OData cannot bind this property expression on the result shape. Select or sort aggregate aliases directly rather than computing new fields from them.");
        }
        catch (ArgumentException)
        {
            throw new WeatherHistoryQueryException("Invalid history query or SQL configuration. Check option values and the configured connection string.");
        }
    }

    private static async Task<WeatherHistoryResult> ExecutePreparedAsync(PreparedHistoryQuery prepared, CancellationToken ct)
    {
        var count = prepared.IncludeCount ? await CountAsync(prepared.Query, ct) : (long?)null;
        var query = prepared.Query;
        // An ungrouped aggregate has at most one row and needs no SQL paging or ordering.
        if (!prepared.IsSingleton)
        {
            query = Call(query, nameof(Queryable.Skip), prepared.Skip);
            query = Call(query, nameof(Queryable.Take), prepared.PageSize + 1);
        }
        if (prepared.Options.SelectExpand is not null)
        {
            prepared.Options.SelectExpand.Compute = prepared.Options.Compute;
            query = prepared.Options.SelectExpand.ApplyTo(query, prepared.Settings);
        }
        var rows = await ReadAsync(query, ct);
        if (prepared.IsSingleton && prepared.Skip > 0) rows.Clear();
        var hasMore = rows.Count > prepared.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new WeatherHistoryResult(rows, hasMore, hasMore ? prepared.Skip + prepared.PageSize : null, count);
    }

    private static IQueryable Call(IQueryable query, string method, int value) => query.Provider.CreateQuery(
        System.Linq.Expressions.Expression.Call(typeof(Queryable), method, [query.ElementType],
            query.Expression, System.Linq.Expressions.Expression.Constant(value)));

    private static Task<long> CountAsync(IQueryable query, CancellationToken ct) =>
        typeof(SqlWeatherHistoryQuery).GetMethod(nameof(CountTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(query.ElementType).CreateDelegate<Func<IQueryable, CancellationToken, Task<long>>>()(query, ct);

    private static Task<long> CountTypedAsync<T>(IQueryable query, CancellationToken ct) =>
        ((IQueryable<T>)query).LongCountAsync(ct);

    private static Task<List<Dictionary<string, JsonElement>>> ReadAsync(IQueryable query, CancellationToken ct) =>
        typeof(SqlWeatherHistoryQuery)
            .GetMethod(nameof(ReadTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(query.ElementType)
            .CreateDelegate<Func<IQueryable, CancellationToken, Task<List<Dictionary<string, JsonElement>>>>>()(query, ct);

    private static async Task<List<Dictionary<string, JsonElement>>> ReadTypedAsync<T>(IQueryable query, CancellationToken ct)
    {
        var result = new List<Dictionary<string, JsonElement>>();
        await foreach (var row in ((IQueryable<T>)query).AsAsyncEnumerable().WithCancellation(ct))
        {
            object? value = row;
            if (value is ISelectExpandWrapper selected) value = selected.ToDictionary();
            // OData's aggregate wrappers carry their own System.Text.Json converters.
            var json = JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
            result.Add(json.EnumerateObject().ToDictionary(x => x.Name, x =>
                x.Value.ValueKind == JsonValueKind.String && DateTime.TryParse(x.Value.GetString(), out var date) && x.Name == "Date"
                    ? JsonSerializer.SerializeToElement(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : x.Value.Clone()));
        }
        return result;
    }
}
