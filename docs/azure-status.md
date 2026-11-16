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
- All 125 .NET tests and nine infrastructure regression checks pass. UI build,
  lint, component tests, and local proxy/cross-origin browser tests passed during preparation.

The new history table is empty. No historical import or database restore was run.
The API identity has CONNECT only; MCP has CONNECT and SELECT on `dbo.DeviceData`.
For manual SID-based external user creation, **managed identities use their client
IDs, not ARM principal/object IDs**. `Write-DatabaseAuthorization.ps1` resolves the
client IDs before rendering the SQL. Regression tests use different object/client
IDs to protect this mapping.

Temporary operator SQL firewall rules were removed after setup and troubleshooting.
Application startup was restored and all uploaded diagnostic files were removed.
Ambient keys exist only in server-side app settings/secure deployment inputs; they
are absent from the client build and committed configuration. No SQL password is used.

The Azure-hosted dashboard has no Vercel dependency. The old Vercel project and
old Git repositories were not deleted or modified by this deployment.

See [the complete resource inventory and deployment guide](../infra/README.md)
for future deployments, maintenance, limits, and database authorization commands.
