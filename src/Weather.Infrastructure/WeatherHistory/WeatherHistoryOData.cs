using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;
using Microsoft.OData.UriParser.Aggregation;
using Weather.UseCases.WeatherHistory.QueryWeatherHistory;

namespace Weather.Infrastructure.WeatherHistory;

// Shared by MCP today and a future HTTP adapter. This class never executes SQL.
public sealed class WeatherHistoryOData
{
    public IEdmModel Model { get; }
    private static readonly HashSet<string> Allowed =
        ["$filter", "$select", "$orderby", "$apply", "$compute", "$top", "$skip", "$count"];

    public WeatherHistoryOData()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<WeatherReading>("WeatherReadings").EntityType.HasKey(x => x.DateUtc);
        Model = builder.GetEdmModel();
    }

    public ODataQueryOptions<WeatherReading> Parse(IReadOnlyDictionary<string, string> options, IServiceProvider services)
    {
        if (options.Count > Allowed.Count || options.Any(x => !Allowed.Contains(x.Key)))
            throw new WeatherHistoryQueryException("Supported options: " + string.Join(", ", Allowed));
        if (options.Any(x => x.Value is null) || options.Sum(x => x.Value.Length) > 16000)
            throw new WeatherHistoryQueryException("Query options must be non-null and at most 16000 characters in total.");

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.QueryString = QueryString.Create(options.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)));
        var path = new ODataPath(new EntitySetSegment(Model.EntityContainer.FindEntitySet("WeatherReadings")));
        http.Request.ODataFeature().Model = Model;
        http.Request.ODataFeature().Path = path;
        var context = new ODataQueryContext(Model, typeof(WeatherReading), path);
        var parsed = new ODataQueryOptions<WeatherReading>(context, http.Request);
        // Bind aggregate aliases before validating $compute/$filter/$select that refer to them.
        _ = parsed.Apply?.ApplyClause;
        parsed.Validate(new ODataValidationSettings
        {
            AllowedQueryOptions = AllowedQueryOptions.Filter | AllowedQueryOptions.Select |
                AllowedQueryOptions.OrderBy | AllowedQueryOptions.Apply | AllowedQueryOptions.Compute |
                AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.Count,
            MaxTop = 1000, MaxNodeCount = 200, MaxOrderByNodeCount = 16,
            MaxAnyAllExpressionDepth = 1
        });
        return parsed;
    }

    public PreparedHistoryQuery Prepare(IQueryable<WeatherReading> source,
        IReadOnlyDictionary<string, string> input, IServiceProvider services)
    {
        var parsed = Parse(input, services);
        var top = parsed.Top?.Value ?? 100;
        var skip = parsed.Skip?.Value ?? 0;
        if (top < 1 || skip > int.MaxValue - 1001)
            throw new WeatherHistoryQueryException("$top must be 1–1000 and $skip must be between 0 and 2147482646.");

        // The final grouping defines the result's identity; an ungrouped aggregate has one row.
        var keys = new List<string> { "DateUtc" };
        if (parsed.Apply is not null)
        {
            var transformations = parsed.Apply.ApplyClause.Transformations.ToList();
            if (transformations.Count > 16)
                throw new WeatherHistoryQueryException("At most 16 $apply transformations are supported.");
            foreach (var transformation in transformations)
            {
                if (transformation is GroupByTransformationNode group)
                    keys = group.GroupingProperties.Select(x => x.Name).ToList();
                else if (transformation is AggregateTransformationNode)
                    keys.Clear();
            }
        }
        var options = input.ToDictionary(x => x.Key, x => x.Value);
        if (options.ContainsKey("$compute") && !options.ContainsKey("$select"))
            options["$select"] = "*";
        options.TryGetValue("$orderby", out var ordering);
        var orderedProperties = new HashSet<string>();
        for (var clause = parsed.OrderBy?.OrderByClause; clause is not null; clause = clause.ThenBy)
        {
            if (clause.Expression is SingleValuePropertyAccessNode property)
                orderedProperties.Add(property.Property.Name);
            else if (clause.Expression is SingleValueOpenPropertyAccessNode openProperty)
                orderedProperties.Add(openProperty.Name);
        }
        var missingKeys = keys.Where(x => !orderedProperties.Contains(x)).ToList();
        if (missingKeys.Count > 0)
            options["$orderby"] = string.Join(",", new[] { ordering }.Where(x => !string.IsNullOrWhiteSpace(x)).Concat(missingKeys));
        parsed = Parse(options, services);
        var settings = new ODataQuerySettings
        {
            EnsureStableOrdering = false, HandleNullPropagation = HandleNullPropagationOption.False,
            EnableConstantParameterization = true, TimeZone = TimeZoneInfo.Utc
        };
        var query = parsed.ApplyTo(source, settings,
            AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.Count | AllowedQueryOptions.Select);
        return new PreparedHistoryQuery(query, parsed, settings, (int)top, (int)skip, parsed.Count?.Value == true, keys.Count == 0);
    }
}

public sealed record PreparedHistoryQuery(IQueryable Query, ODataQueryOptions<WeatherReading> Options,
    ODataQuerySettings Settings, int PageSize, int Skip, bool IncludeCount, bool IsSingleton);
