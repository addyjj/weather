# Azure deployment status

Deployed and verified on November 15, 2026 after review of the resource inventory.

| Application | URL |
| --- | --- |
| Dashboard | https://kind-beach-0fd6c791e.5.azurestaticapps.net |
| API | https://weather-yqwclkoqjorko-api.azurewebsites.net |
| MCP | https://weather-yqwclkoqjorko-mcp.azurewebsites.net/mcp |

Resource group: `weather` (metadata location West US 2). App Service and SQL are in
West US 3; Static Web Apps is in West US 2. West US 2 SQL provisioning was restricted
despite a successful what-if. The failed attempt's empty web apps and plan were
replaced with explicit approval. The SQL server uses a region-specific name,
`weather-yqwclkoqjorko-sql-westus3`, to avoid the failed attempt's reserved name.

Verified free configuration:

- `weather-free-plan`: F1; both .NET hosts use the same free Linux plan.
- Static Web Apps: Free.
- `weather-db`: `useFreeLimit=true`, exhaustion behavior `AutoPause`, 32 GiB maximum
  data size, locally redundant backups, seven-day retention.
- No paid SKU, scale-out, Always On, diagnostic destination, or separate SignalR
  service was added. Free-tier sleep, CPU, connection, and monthly SQL limits apply.

Verified application flow:

- REST liveness and detailed health return HTTP 200; SQL and Ambient checks are Healthy.
- REST returns real Ambient station readings.
- MCP discovers both tools and successfully returns current conditions and a SQL history query.
- The dashboard renders station readings, connects to SignalR, and receives a real
  `weatherData` event; the browser verification recorded no page errors.
- All 128 .NET tests and ten infrastructure regression checks pass. UI build,
  lint, component tests, and local proxy/cross-origin browser tests passed during preparation.

On November 15, 2026, 505,907 readings were copied from the local SQL Express
`weather.dbo.DeviceData` table into Azure. All 27 fields of every row were verified
against the local source before committing. Stored readings span December 30,
2021 through November 15, 2026 (UTC). Local data and Azure migration history were
preserved. The live MCP history query also confirmed all 505,907 readings.
The API identity has CONNECT and SELECT/INSERT on `dbo.DeviceData` for its import
WebJob; MCP has CONNECT and SELECT only. Neither has schema, UPDATE, or DELETE grants.
For manual SID-based external user creation, **managed identities use their client
IDs, not ARM principal/object IDs**. `Write-DatabaseAuthorization.ps1` resolves the
client IDs before rendering the SQL. Regression tests use different object/client
IDs to protect this mapping.

Temporary operator SQL firewall rules were removed after setup and troubleshooting.
DataGrip access uses a separate `DataGripWorkstation` firewall rule restricted to
this computer's public IPv4 address and Microsoft Entra sign-in. Update or remove
that rule when the address changes or workstation access is no longer needed.
Application startup was restored and all uploaded diagnostic files were removed.
Ambient keys exist only in server-side app settings/secure deployment inputs; they
are absent from the client build and committed configuration. No SQL password is used.

Cleanup completed: the former Vercel project, standalone API/UI GitHub repositories,
and their local checkouts have been deleted. Their Git histories remain preserved
in the consolidated `addyjj/weather` repository.

The API also hosts the triggered `weather-import` WebJob. It has no schedule.
Its first Azure run succeeded, adding 299 readings from two Ambient history pages,
bringing the total to 506,206. A second run succeeded with zero new readings,
confirming safe reruns. The job uses inherited app settings and the API's managed identity. The job
handles Kudu shutdown signals and emits periodic progress for longer backfills.
Linux WebJobs require both `WEBSITE_SKIP_RUNNING_KUDUAGENT=false` and
`config/web.webJobsEnabled=true`. API host filtering uses
`WeatherHttp__AllowedHosts` to avoid interfering with KuduAgent. Basic publishing
credentials and Always On remain disabled; the plan is still F1.

See [the complete resource inventory and deployment guide](../infra/README.md)
for future deployments, maintenance, limits, and database authorization commands.
