# Weather MCP server and REST API

A .NET 10 Streamable HTTP MCP server with two tools:

- `get_current_conditions`: latest outdoor conditions from Ambient Weather,
  as of the station's last upload.
- `query_weather_history`: read-only querying of stored SQL Server readings using
  OData filters, projections, aggregates, sorting, and pagination.

Historical queries use SQL only. The MCP and REST hosts do not import data or
change your database schema. A separate console importer can add Ambient history
to the existing table when explicitly run.

The solution also includes a separate REST API host with the device endpoints
from `Weather.Api`. Both hosts share the application and infrastructure projects.

## REST API

Start the device API from the repository root (after building):

```powershell
dotnet run --project src/Weather.WebApi --no-build --no-launch-profile
```

The default address is **http://localhost:5001**, so it can run alongside the MCP
host on port 5000. Override it with `-- --urls http://localhost:5051` or
`ASPNETCORE_URLS`. For a Release build, add `-c Release` to the run command.

| Route | Behavior |
| --- | --- |
| `GET /Devices` | Registered Ambient devices with `name`, `location`, and `latestData` |
| `GET /Devices/{macAddress}/data` | Ambient device readings, newest first |

The data route accepts optional `endDate` (an ISO 8601 timestamp, inclusive) and
`limit` (default 10, range 1–100). Supply UTC (`Z`) or an explicit offset for
unambiguous dates. The adapter converts `endDate` to epoch milliseconds for
Ambient Weather. Invalid dates or limits return HTTP 400.

```powershell
Invoke-RestMethod 'http://localhost:5001/Devices'
Invoke-RestMethod 'http://localhost:5001/Devices/AA:BB:CC:DD:EE:FF/data?endDate=2026-11-14T12:00:00Z&limit=10'
```

Both routes preserve Weather.Api's camel-case JSON contracts. Readings contain
`date`, `baromRel`, `tempOut`, `humidityOut`, `windDir`, `windSpeed`, `windGust`,
`eventRain`, `dailyRain`, `uvIndex`, `feelsLike`, `dewPoint`, and `solarRadiation`.
Missing device names/locations become empty strings, and missing reading fields
use the original contract's zero/default values. Temperatures are °F, pressure
is inHg, wind is mph, rainfall is inches, and solar radiation is W/m².

Configure `Ambient__ApiKey` and `Ambient__ApplicationKey` using the environment
variables below, or an ignored `src/Weather.WebApi/appsettings.Local.json` file.
`Ambient__ApiUrl` is optional; `DeviceMacAddress` is not required by these routes.
Local settings are copied beside the development executable and excluded from
publish output. Environment variables override local settings.

Results are cached for five minutes, separately for each device/date/limit
combination, matching Weather.Api's cache duration. These endpoints read Ambient
Weather directly and do not need SQL configuration. Upstream failures return
HTTP 502 Problem Details without upstream response bodies or credentials.
The REST host defaults to loopback with local hostnames and does not enable CORS
or authentication. Configure HTTPS and access controls before remote hosting.
The host also exposes SignalR at `/weatherHub`, broadcasting `weatherData` events
with the same reading contract as the REST endpoints. An Infrastructure adapter
subscribes to Ambient's real-time Socket.IO feed, while a host background service
relays readings through an application query handler. `Ambient__SocketUrl` defaults
to `https://rt2.ambientweather.net`. Disconnected feeds retry after ten seconds and
resubscribe. HTTP device queries continue working while the feed is unavailable.
Chat streaming and background database imports are outside this host's scope.

The sibling `weather-ui` project proxies `/api/devices` and `/api/weatherHub` to
this host, including WebSocket upgrades. Start the API with `--no-launch-profile`
as above to use port 5001, then run `npm run dev` from `weather-ui`. Set
`WEATHER_API_URL` on the UI development process if you use another backend port.
Its `npm run test:e2e` command uses Playwright, the real API host, and a local
Ambient HTTP/Socket.IO stub to verify initial readings, live updates, reconnects,
and loading/empty/error states without live credentials.

## Health endpoints

The REST host includes the old API's two health routes:

| Route | Behavior |
| --- | --- |
| `GET /health` | Lightweight liveness check; returns plain-text `Healthy` and HTTP 200 while the host is running |
| `GET /health/details` | Checks SQL connectivity and Ambient Weather; returns JSON and HTTP 200 when both are healthy, or HTTP 503 when either is unhealthy |

Detailed results retain the original `status` and `checks` envelope. Each check
has `name`, `status`, `description`, and `exception` fields. Check names are
`database` and `ambient_weather_api`; `exception` is always null so provider
exceptions, connection details, and credentials are not exposed.

The database check opens the configured `ConnectionStrings:sql` database without
reading or changing tables. The Ambient check makes a fresh authenticated
`/devices/{DeviceMacAddress}?limit=1` request and bypasses the device response cache.
It requires `Ambient:DeviceMacAddress` in addition to the API/application keys.
Each dependency check has a ten-second timeout. Missing configuration or unavailable
dependencies produce unhealthy detail results; liveness does not contact them.
Health responses are not cached. Poll detailed health sparingly because each call
uses Ambient API quota. These checks test dependency connectivity, not the live
Socket.IO subscription or the presence of the SQL history table.

```powershell
Invoke-RestMethod 'http://localhost:5001/health'
Invoke-RestMethod 'http://localhost:5001/health/details'
```

## Import Ambient history

`Weather.Import` replaces the old Weather.Api console import command. It reads
Ambient history backward from now until reaching the newest saved `DateUtc`,
then adds only newer readings to the existing `dbo.DeviceData` table. It preserves
all stored weather fields, including indoor sensors and numeric/boolean battery
flags. Like the original importer, it waits four seconds between API requests.

Configure `ConnectionStrings__sql`, `Ambient__ApiKey`,
`Ambient__ApplicationKey`, and `Ambient__DeviceMacAddress` in the importer process
environment. `Ambient__ApiUrl` is optional. Alternatively, put the corresponding
`ConnectionStrings` and `Ambient` objects in the ignored
`src/Weather.Import/appsettings.Local.json`; environment variables override it.
The import host reads its own local settings file. It does not automatically read
the MCP or REST host's settings. Use SQL credentials with SELECT and INSERT access.

```powershell
dotnet run --project src/Weather.Import -- --help
dotnet run --project src/Weather.Import
# Override the configured station for this run:
dotnet run --project src/Weather.Import -- --macAddress AA:BB:CC:DD:EE:FF
```

This command writes data when run. It does not create a database, table, or
migration. Run it against the existing Weather.Api schema described in
`docs/database-schema.md`. Exit codes are 0 for success (including no new readings),
1 for configuration/import failure, and 130 for Ctrl+C cancellation.

Pagination completes before writes begin, matching the original importer's
all-or-nothing behavior. Readings are buffered in memory, so a large initial
backfill needs memory proportional to its history. All inserts commit in one
transaction; errors and cancellation roll back the transaction. Duplicate timestamps
are ignored, and concurrent import processes serialize their inserts with a SQL
transaction lock. Existing readings are not overwritten. Restart a failed import
to retry; partially advanced timestamps cannot hide unimported older pages.

The table has a timestamp-only primary key and no station key, as in the old API.
Use one station per database; switching stations in the same database can cause
overlapping timestamps to be treated as existing readings. This incremental command
does not fill historical gaps older than the newest saved reading.

Tests run the real console host against a local Ambient stub and a disposable
LocalDB database. They verify every stored column, duplicate/rerun behavior,
concurrent inserts, transaction rollback, pagination, and cancellation. They never
run imports against your configured weather database.

## Current conditions

Call `get_current_conditions` without arguments. It replaces `get_current_temperature`
and returns a JSON object instead of a single number. Refresh tool discovery in
existing MCP clients after restarting the server.

| Output fields | Units / meaning |
| --- | --- |
| `observedAtUtc` | Station observation timestamp in UTC |
| `temperatureFahrenheit`, `feelsLikeFahrenheit`, `dewPointFahrenheit` | °F |
| `humidityPercent` | Outdoor relative humidity, % |
| `windDirectionDegrees` | Degrees |
| `windSpeedMph`, `windGustMph`, `maxDailyGustMph` | mph; gust is the last 10-minute maximum |
| `relativePressureInHg`, `absolutePressureInHg` | inHg |
| `hourlyRainRateInches` | Inches/hour |
| `dailyRainInches` | Cumulative daily inches |
| `solarRadiationWattsPerSquareMeter`, `uvIndex` | W/m², UV index |

Outdoor temperature is required; missing optional measurements and timestamps are
null, so unavailable sensors are distinguishable from zero. These are measured
station conditions, with no inferred sky description or forecast. Field mappings
follow [Ambient Weather's device specifications](https://github.com/ambient-weather/api-docs/wiki/Device-Data-Specs).
The existing `/devices/{macAddress}?limit=1` endpoint is retained; its latest reading
can lag the station upload, so use `observedAtUtc` to assess freshness.

## SQL history configuration

Point `ConnectionStrings:sql` at the existing database containing `dbo.DeviceData`.
For example, set this in the environment of the running server process (replace the
example with a connection string appropriate for your SQL Server):

```text
ConnectionStrings__sql=Server=localhost,1433;Database=weather;Integrated Security=true;TrustServerCertificate=true
```

Or add a `ConnectionStrings` object with a `sql` property to the ignored
`src/Weather.Mcp/appsettings.Local.json` file alongside your existing `Ambient`
settings. The file is copied beside the development executable when built.
Environment variables override local settings. SQL configuration is only needed
when calling the history tool; the current-conditions tool still works without it.

Use credentials with SELECT access to `dbo.DeviceData`. Secrets belong in the
server environment or ignored local settings, not committed configuration.

## Query weather history

The tool accepts a typed `options` object with optional properties `filter`, `select`,
`orderBy`, `apply`, `compute`, `top`, `skip`, `count`, and `timeZone`. Expression properties are
strings using OData syntax; `top` and `skip` are integers and `count` is a boolean.
Use exact, case-sensitive weather field names. The MCP adapter maps these properties
to standard OData options internally. This replaces
the previous dictionary contract; `$`-prefixed JSON property names are not accepted.
Arbitrary SQL,
navigation expansion, search, and additional entity sets are not exposed.

Filtered readings, with selected columns and an optional total count:

```json
{
  "options": {
    "filter": "Date ge 2026-01-01T00:00:00Z and Date lt 2026-02-01T00:00:00Z and TempOut lt 32",
    "select": "DateUtc,Date,TempOut,HumidityOut",
    "orderBy": "DateUtc desc",
    "top": 100,
    "count": true
  }
}
```

Daily summaries (UTC by default), ordered by their average temperature:

```json
{
  "options": {
    "apply": "filter(Year eq 2026)/groupby((Year,Month,Day),aggregate(TempOut with min as MinTemperature,TempOut with max as MaxTemperature,TempOut with average as AverageTemperature,$count as Samples))",
    "orderBy": "AverageTemperature desc",
    "top": 10,
    "count": true
  }
}
```

Other supported aggregates include `sum` and `countdistinct`. Use `Hour` as another
grouping key for hourly summaries. Always include Year/Month/Day when grouping
calendar days, so dates in different months or years do not get combined.

Computed output fields:

```json
{
  "options": {
    "compute": "TempOut sub 32 as DegreesAboveFreezing",
    "select": "Date,TempOut,DegreesAboveFreezing",
    "orderBy": "DateUtc"
  }
}
```

OData evaluates `$apply` before the top-level `$filter`. To filter **source readings**
before calculating a summary, put `filter(...)` inside `$apply`, ahead of
`groupby(...)` or `aggregate(...)`. A top-level `$filter` can instead filter the
resulting aggregate aliases.
Source fields removed by grouping cannot be used in the top-level `filter`.
For example, duplicating a `Date` range there can produce OData's misleading
`grouping expression 'Date' must evaluate to a property access value` error.
Keep the date range only in `apply`'s `filter(...)` before grouping.
Expressions must translate to SQL Server; unsupported
expressions return a tool error instead of fetching all readings to aggregate in memory.
The current OData binder does not support sorting by new `$compute` expressions
over aggregate aliases. Select, filter, or sort aggregate aliases directly; use
`$compute` over source reading fields. Unsupported compositions return an explicit error.

### Pagination and results

Results contain `rows`, `hasMore`, `nextSkip`, and optional `totalCount`.
Row property names retain the selected field names and aggregate aliases.
This is an MCP JSON response, not a complete OData HTTP response envelope.

`top` is the per-request page size (default 100, range 1–1000). `skip` defaults
to zero. To fetch another page, repeat the same query with `skip` set to
`nextSkip`. Stop when `hasMore` is false; `nextSkip` will then be null.
`count: true` counts all matching readings or aggregate groups before pagination;
otherwise `totalCount` is omitted.

Sorting and aggregation happen before paging. A `DateUtc` tie-breaker is appended
for reading queries; grouped queries use their grouping keys. Ungrouped aggregate
queries produce at most one summary. Offset paging is deterministic for unchanged
data; concurrent imports can shift page boundaries. Count and page reads are
separate statements, not a snapshot of concurrent writes.

### Fields and units

| Fields | Meaning / units |
| --- | --- |
| `DateUtc`, `Date` | Epoch milliseconds (primary key), UTC timestamp |
| `LocalDate` | Timestamp in `timeZone` with the applicable UTC offset |
| `Year`, `Month`, `Day`, `Hour` | Calendar parts in `timeZone` (UTC by default), not stored columns |
| `TempIn`, `TempOut`, `FeelsLike`, `DewPoint`, `FeelsLikeIn`, `DewPointIn` | °F |
| `HumidityIn`, `HumidityOut` | Percent |
| `BaromRel`, `BaromAbs` | inHg |
| `WindDir` | Degrees |
| `WindSpeed`, `WindGust`, `MaxDailyGust` | mph |
| `HourlyRainRate` | Inches/hour |
| `EventRain`, `DailyRain`, `WeeklyRain`, `MonthlyRain`, `TotalRain` | Cumulative inches |
| `SolarRadiation`, `Uv` | W/m², UV index |
| `BattOut`, `BattCo2`, `Loc` | Stored battery flags and location identifier |

Averages are sample averages, not time-weighted averages. Summing cumulative rain
measurements does not calculate rainfall totals. Automatic weather-specific
derived calculations are not included.

### User's local time

Pass the user's timezone explicitly; the server does not infer it from its own
clock or the client connection. IANA IDs such as `America/Chicago` and Windows
IDs such as `Central Standard Time` are supported. Omitted `timeZone` defaults
to `UTC`; unknown or empty IDs return an error.

```json
{
  "options": {
    "timeZone": "America/Chicago",
    "select": "Date,LocalDate,TempOut",
    "orderBy": "DateUtc desc",
    "top": 10
  }
}
```

`Date` and `DateUtc` always remain UTC. `LocalDate` includes a numeric offset,
for example `2026-11-12T06:50:00-06:00`. Select `LocalDate` when displaying readings
to the user; omitting `select` includes it automatically. Daylight-saving offsets
are calculated separately for each reading.

`Year`, `Month`, `Day`, and `Hour` filters/grouping use the requested timezone,
with conversion performed in SQL before aggregation. For example, this counts
readings on a Chicago calendar day, including the appropriate UTC boundaries:

```json
{
  "options": {
    "timeZone": "America/Chicago",
    "apply": "filter(Year eq 2026 and Month eq 11 and Day eq 12)/groupby((Year,Month,Day),aggregate(TempOut with average as AverageTemperature,$count as Samples))"
  }
}
```

Explicit `Date` filters continue to use UTC instants; use timezone-derived
calendar fields for local dates, or supply explicit offsets in timestamp filters.
Keep `timeZone` unchanged across pages. During a fall-back transition, grouping
only by calendar `Hour` combines both occurrences of the repeated clock hour;
individual `LocalDate` values retain their distinct offsets.

Queries have a 30-second SQL command timeout, cancellation support, a 16,000-character
input limit, and bounded expression complexity. Invalid options and unavailable SQL
connections return errors without connection strings or database credentials.

## Ambient Weather configuration

Uses the same `Ambient` settings and authentication as Weather.Api. Set these
environment variables in the running server process's environment:

```text
Ambient__ApiKey=your-api-key
Ambient__ApplicationKey=your-application-key
Ambient__DeviceMacAddress=your-station-mac-address
Ambient__ApiUrl=https://api.ambientweather.net/v1/
```

Alternatively, create `src/Weather.Mcp/appsettings.Local.json` with an `Ambient`
object containing `ApiKey`, `ApplicationKey`, `DeviceMacAddress`, and `ApiUrl`.
This file is git-ignored and copied beside the development executable on build;
it is excluded from publish output. Environment variables override this file.
Keep credentials out of source control. ApiUrl defaults to the URL above.

Each call requests `/devices/{macAddress}?limit=1` using the two keys in query
parameters, as in Weather.Api. Missing outdoor readings and HTTP failures return
tool errors. Calls are not cached; Ambient Weather's [API rate limits](https://ambientweather.docs.apiary.io/)
apply (one request per second per user API key).

## Build and test

EF migrations, Docker Compose, publish artifacts, and the Azure GitHub Actions
workflow are documented in [deployment guidance](docs/deployment.md). Database
migrations are explicit administrative commands; API/MCP startup never applies them.

Install the .NET 10 SDK and ASP.NET Core runtime (included with the SDK). For the
SQL integration/E2E tests, install SQL Server Express LocalDB on Windows and make
the `MSSQLLocalDB` instance available under the user running the tests:

```powershell
SqlLocalDB info
SqlLocalDB start MSSQLLocalDB
```

Run from the repository root:

```powershell
dotnet build Weather.slnx
dotnet test Weather.slnx --no-build
```

The suite creates a uniquely named `WeatherMcpTests_<guid>` LocalDB database,
creates the expected table, seeds known readings, and drops only that test database
afterward. It never writes to the existing `weather` database. An unavailable
LocalDB instance causes an explicit test failure, not a skipped test or in-memory fallback.

SQL tests check exact filters, computed fields, UTC boundaries, aggregate values,
group counts, stable pagination, timezone conversion (including Chicago's spring
and fall DST transitions), and actual SQL grouping/aggregate/paging commands.
The MCP E2E test launches the real HTTP server on an OS-assigned loopback port,
connects with the official Streamable HTTP MCP client, discovers both tools, and
queries the seeded SQL database through the complete production pipeline.
Current-conditions tests retain the local Ambient HTTP stub; no live Ambient
credentials or network requests are needed.
REST tests launch the production WebApi host against a local Ambient HTTP stub
and verify both device routes, exact response fields, query defaults and date
conversion, caching, validation, and upstream error responses.

If a running MCP instance locks Debug build files, stop it before rebuilding,
or run `dotnet build Weather.slnx -c Release` followed by
`dotnet test Weather.slnx -c Release --no-build`.

## Connect an MCP client

Start the server from the repository root:

```powershell
dotnet run --project src/Weather.Mcp --no-build
```

The endpoint is **http://localhost:5000/mcp**. Configure your client to use
Streamable HTTP at that URL. For clients using JSON `mcpServers` configuration:

```json
{
  "mcpServers": {
    "weather": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

Client configuration formats vary. The server now runs independently of the MCP
client; keep the server process running while connecting or calling tools.
Replace any previous command/args (stdio) configuration with the HTTP URL.
The endpoint speaks MCP, rather than exposing tools as ordinary REST routes.
Resource and resource-template discovery return empty lists, since this server
currently exposes tools only.

To choose a different local port:

```powershell
dotnet run --project src/Weather.Mcp --no-build -- --urls http://localhost:5050
```

You can also set `ASPNETCORE_URLS` on the server process. The host uses stateless
Streamable HTTP and has no dependency on client stdin/stdout. Logs are ordinary
ASP.NET Core console logs. By default it listens only on loopback, accepts local
hostnames (`localhost`, `127.0.0.1`, `[::1]`), and does not enable CORS or authentication.
Remote hosting would require deliberate URL/AllowedHosts, HTTPS, and access-control configuration.

## Logging

Serilog writes to the server console. Every incoming MCP tool call logs the tool
name and its supplied arguments as JSON, before argument binding or query execution.
This includes invalid queries, so you can see what the client attempted:

```text
[14:25:10 INF] Weather.Mcp.ToolCalls: MCP tool query_weather_history called with parameters {"options":{"filter":"TempOut lt 32","top":10,"count":true}}
```

`Weather.Mcp.Lifecycle` logs startup with the hosting environment, readiness with
each actual `/mcp` URL, and stopping/stopped events during graceful shutdown
(for example Ctrl+C). Force-killing the process cannot produce shutdown logs.
These are app logs, so they remain visible while framework Information logs are filtered.

By default, `Weather` application categories log at Information and above; all
other categories (framework, MCP SDK, EF Core, and Serilog HTTP request logs)
only appear at Warning and above. Tool query parameters remain visible. Tool results,
configuration, and credentials are not added to the tool-call log. Query text is
logged verbatim. Minimum levels can be changed under `Serilog:MinimumLevel` in
`appsettings.json`, ignored local settings, or server environment variables (for
example `Serilog__MinimumLevel__Override__Weather=Debug` for more app detail).

## Architecture

- `Weather.UseCases`: current conditions and history features, handlers, outbound ports,
  and application-owned JSON result contracts. No EF Core, OData, or MCP dependencies.
- `Weather.Infrastructure`: Ambient HTTP adapter, SQL Server EF mapping, and
  shared OData model/query pipeline. `WeatherHistoryOData` parses and composes
  queries; `SqlWeatherHistoryQuery` executes and normalizes results. The read model
  is also EF-mapped, with calendar parts projected in SQL to avoid duplicate models.
- `Weather.Mcp`: ASP.NET Core HTTP host, thin tool adapters, startup, and dependency
  injection. Maps MCP at `/mcp`; OData's internal query request context stays inside
  Infrastructure. No OData REST routes are exposed.
- `Weather.WebApi`: separate ASP.NET Core REST host with thin device controllers.
  Invokes the device query handlers in UseCases; Infrastructure maps Ambient
  device payloads to application DTOs and caches results for five minutes.
  A hosted service relays the application weather stream to SignalR clients at
  `/weatherHub`; Socket.IO dependencies and deserialization stay in Infrastructure.
  Operational health checks live in Infrastructure, with `/health` response
  formatting and endpoint selection in the REST host.
- `Weather.Import`: console entry point for the `ImportWeatherHistory` command and
  handler in UseCases. Infrastructure supplies the full Ambient history adapter and
  transactional SQL insert adapter. The existing read model maps stored readings;
  application import DTOs stay independent of EF Core and Ambient payload names.

The REST device routes use Ambient Weather. The shared OData pipeline remains
available for future SQL history routes. No automatic schema creation,
migrations, or changes to Weather.Api are performed by either host.

No Core project is needed until domain behavior exists. No mediator is needed.
SQL persistence is used by the explicit import workflow. HTTP calls and cancellation flow asynchronously through the
tool, handler, and provider; transport types stay outside UseCases.

The host uses the [official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk).
