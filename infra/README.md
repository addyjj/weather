# Azure-only deployment using free tiers

The reviewed stack was deployed on November 15, 2026. See the [deployment status](../docs/azure-status.md) for endpoints and verification. Creating templates, compiling them, and running Azure `what-if` does not deploy an app. Review the resource inventory before future creation or publishing.

## Resource inventory before publishing

Default resource group: `weather`. App Service and SQL use **West US 3**. Static Web Apps and the resource-group
metadata remain in **West US 2**; the static site is globally served. Names use `weather-<suffix>`, where `<suffix>` is stable
for the selected subscription and resource group. In the current subscription the
suffix is `yqwclkoqjorko`.

| Resource | Default name | Configuration / purpose |
| --- | --- | --- |
| Resource group | `weather` | Contains the complete stack |
| App Service plan | `weather-free-plan` | Linux **F1 / Free**; shared by both backend hosts |
| REST/SignalR web app | `weather-yqwclkoqjorko-api` | .NET 10; existing REST endpoints and `/weatherHub` |
| MCP web app | `weather-yqwclkoqjorko-mcp` | .NET 10; `/mcp`, including SQL history |
| Static Web App | `weather-yqwclkoqjorko-ui` | **Free**; hosts the built React dashboard instead of Vercel |
| SQL logical server | `weather-yqwclkoqjorko-sql-westus3` | Entra-only authentication; existing human administrator |
| SQL database | `weather-db` | **Free offer**, `GP_S_Gen5`, 2 vCores, maximum 32 GiB; pauses at the free limit |

These configuration/child resources are also part of the template:

| Child resource | Quantity | Purpose |
| --- | --- | --- |
| Web-app `config/appsettings` | 2 | Ambient secrets, managed-identity SQL connection, hostnames, and UI CORS origin |
| Web-app `basicPublishingCredentialsPolicies/scm` and `/ftp` | 4 | Disable password-based publishing for both hosts |
| SQL `firewallRules/AllowAzureServices` | 1 | Allows Azure-hosted clients through the SQL network firewall; Entra authentication is still required |
| SQL `backupShortTermRetentionPolicies/default` | 1 | Seven-day retention, daily differential backups, locally redundant storage |
| SQL `firewallRules/OperatorIp` | 0 by default; 1 if requested | A single explicitly supplied public IPv4 address for migrations and imports |

Azure also creates two **system-assigned managed identities**, one per web app,
and the SQL server's system `master` database. They have no separate hosting SKU.
SQL authorization is an explicit post-provisioning step: an API user with CONNECT
permission and an MCP user with CONNECT plus SELECT on `dbo.DeviceData`.
The existing administrator account is reused; no human Entra user, user-assigned
identity, app registration, role assignment, or GitHub OIDC identity is created.
ARM also records the `weather-free-stack` module deployment (no hosting charge).

No Azure SignalR service is needed: the existing API hosts SignalR itself. There
is no Azure Functions app, storage account, Container Registry, Key Vault, Log
Analytics workspace, Application Insights, Front Door, private endpoint, paid
certificate, custom domain purchase, or scheduled import job in this plan.
No Vercel service is used. This change does not delete the existing Vercel project.

## What free means here

The template fixes the hosting SKUs and SQL overage behavior rather than accepting
SKU overrides. If Azure refuses a free deployment, stop; do not substitute Basic,
Standard, paid serverless overages, or a trial credit as a fallback.

- App Service F1 has 60 CPU minutes per day, a 3-minute CPU allowance per 5-minute
  interval, 1 GiB storage, constrained memory/bandwidth, no Always On, and at most
  five WebSocket connections per instance. Expect cold starts, sleeping, and quota
  interruptions. The template creates one plan and no scale-out rules.
- Static Web Apps Free includes 100 GB monthly bandwidth per subscription and
  250 MB per deployment. It stops serving when free limits are exceeded rather
  than adding paid bandwidth overages.
- Azure SQL's recurring free offer includes 100,000 vCore-seconds, 32 GB data,
  and 32 GB backup storage monthly. `useFreeLimit: true` and
  `freeLimitExhaustionBehavior: 'AutoPause'` are required. The database pauses after
  15 idle minutes and can become inaccessible until the next month at its free
  limit. The template caps data at 32 GiB, uses local backups, seven-day retention,
  and creates no long-term retention or paid replica configuration.
- SQL and App Service share a region. The UI calls the API directly over HTTPS;
  it does not use Static Web Apps' paid linked-backend feature. The template uses
  the actual generated Azure hostnames, not guessed `.azurewebsites.net` names.
- Runtime SQL pooling is disabled to release idle sessions and allow serverless
  pause. Frequent history queries, health polling, or manual imports still consume
  the SQL free allowance. Do not keep database administration tools connected.
- These resource settings target zero Azure service charges; they are not a
  subscription-wide spending cap. Before creating resources, review any inherited
  policies that enable paid services. Do not enable Defender trials, diagnostics
  destinations, paid support, or extra resources as part of this deployment.

On November 15, 2026, West US 2 passed what-if but actual SQL server creation
failed with `ProvisioningDisabled`. The SQL capabilities endpoint reports that
region as `Visible`, not `Available`; what-if alone was insufficient. West US 3
reports SQL as available and passes the free F1 check, so it is the backend default.
Central US and East US 2 rejected F1 because the subscription's free quota was zero.
Run `Test-Region.ps1` and what-if before creation; neither guarantees eventual
provisioning when capacity changes.
This is not a promise of always-on service or a production SLA.

Official references:

- [App Service limits](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#app-service-limits)
- [Linux free-plan WebSockets](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/faqs-app-service-linux-new#are-websockets-supported)
- [Static Web Apps quotas](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas)
- [Static Web Apps Free stops at quota](https://azure.microsoft.com/en-us/pricing/details/app-service/static/)
- [Azure SQL free tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier)
- [SQL free-tier restrictions](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-tier-faq)

## Files and local validation

- `main.bicep`: subscription-scope entry point; creates the group and stack module.
- `resources.bicep`: all hosting, database, and configuration resources.
- `main.bicepparam`: reads credentials and administrator details from environment variables.
- `free-tier.test.mjs`: checks the compiled resource inventory and cost guards.
- `Test-Region.ps1`: read-only SQL provisioning availability check before creation.
- `authorize-apps.sql`: explicit least-privilege database authorization after migrations.
- `Write-DatabaseAuthorization.ps1`: resolves managed identity client IDs and renders the SQL script.

From the repository root, with Azure CLI, Bicep, and Node installed:

```powershell
az bicep install
az bicep build --file infra/main.bicep --outfile artifacts/infra/main.json
node --test infra/*.test.mjs
```

CI runs these checks without Azure sign-in. It also compiles parameter bindings
with dummy nonsecret values. CI builds, tests, and publishes downloadable artifacts;
it does not provision infrastructure or deploy to Azure on a push.

## Preview only

Sign in to the intended tenant/subscription first. For an interactive user login:

```powershell
az account show --query '{subscription:name,id:id,tenant:tenantId}'
$admin = az ad signed-in-user show --query '{id:id,login:userPrincipalName}' -o json | ConvertFrom-Json
$env:WEATHER_SQL_ADMIN_OBJECT_ID = $admin.id
$env:WEATHER_SQL_ADMIN_LOGIN = $admin.login
$env:WEATHER_AMBIENT_API_KEY = Read-Host 'Ambient API key' -MaskInput
$env:WEATHER_AMBIENT_APPLICATION_KEY = Read-Host 'Ambient application key' -MaskInput
$env:WEATHER_AMBIENT_MAC_ADDRESS = Read-Host 'Station MAC address'
# Optional: set to your known public IPv4 address if you will run migrations locally.
# $env:WEATHER_OPERATOR_IP = '203.0.113.10'
./infra/Test-Region.ps1 -Location westus3
az deployment sub what-if --name weather-free --location westus2 --parameters infra/main.bicepparam --validation-level ProviderNoRbac
```

Use dummy Ambient values for a planning-only preview if real credentials are not
available. Secure parameters keep the keys out of deployment outputs. Do not save
expanded parameter JSON with real keys in Git, logs, or build artifacts.
Confirm that the preview lists only the inventory above, F1/Free hosting, and the
SQL free-limit/AutoPause settings. Optional operator access must be a single IP,
not an unrestricted range. `AllowAzureServices` admits traffic from Azure outside
this subscription as well; it is not a substitute for database authorization.
F1 cannot use VNet integration/private endpoints, which this plan does not create.

## Creation and publishing, after approval only

The commands in this section create resources or publish code and require review
before execution. An authenticated Azure CLI user can deploy
without creating a new service principal or copying old GitHub secrets.

```powershell
az deployment sub create --name weather-free --location westus2 --parameters infra/main.bicepparam --output none
$outputs = az deployment sub show --name weather-free --query properties.outputs -o json | ConvertFrom-Json
$resources = $outputs.resources.value
$urls = $outputs.urls.value
$db = $outputs.database.value
```

Check the deployed SKUs and SQL settings before uploading any app:

```powershell
az appservice plan show -g weather -n $resources.plan --query sku
az staticwebapp show -g weather -n $resources.ui --query sku
az sql db show -g weather -s $resources.sqlServer -n $resources.database --query '{free:useFreeLimit,exhaustion:freeLimitExhaustionBehavior,maxSize:maxSizeBytes,backups:requestedBackupStorageRedundancy}'
```

Expect `F1`, Static Web Apps `Free`, SQL `free: true`, `AutoPause`, 34359738368
bytes, and `Local` backup redundancy. Stop if any differs.

### Initialize the empty database explicitly

The new database has no old readings. Infrastructure creation and host startup
never apply schema migrations or import weather history. The administrator needs
SQL network access (use the optional operator IP rule when connecting locally).

```powershell
$env:ConnectionStrings__sql = $db.adminConnectionString
dotnet tool restore
dotnet ef database update --project src/Weather.Infrastructure --configuration Release
./infra/Write-DatabaseAuthorization.ps1
```

Run the generated `artifacts/authorize-apps.sql` **in weather-db** using an Azure
SQL client authenticated as the Entra administrator. This grants only CONNECT to
the API's identity (for connectivity checks) and CONNECT/SELECT on `dbo.DeviceData`
to the MCP identity. It creates external users from managed identity **client-ID** SIDs without granting
the SQL server Microsoft Graph directory-reader permissions. ARM principal IDs are object IDs; the helper resolves their appId/client IDs before rendering SQL. Reruns refuse mismatched identities.

If initial history is needed, explicitly run `dotnet run --project src/Weather.Import -c Release` with the administrator connection and the existing Ambient settings.
The importer is a one-shot tool, not a permanently hosted job. Its writes and
pagination still consume the database/Ambient allowances. Close the database
client and remove the operator firewall rule after maintenance.

### Publish the two .NET hosts

```powershell
dotnet publish src/Weather.WebApi -c Release -o artifacts/webapi
dotnet publish src/Weather.Mcp -c Release -o artifacts/mcp
Compress-Archive -Path artifacts/webapi/* -DestinationPath artifacts/webapi.zip -Force
Compress-Archive -Path artifacts/mcp/* -DestinationPath artifacts/mcp.zip -Force
az webapp deploy -g weather -n $resources.api --src-path artifacts/webapi.zip --type zip
az webapp deploy -g weather -n $resources.mcp --src-path artifacts/mcp.zip --type zip
```

Azure CLI uses the signed-in Entra user's access; basic publishing credentials
remain disabled. Ambient secrets come from Bicep app settings, not browser files.
Azure terminates HTTPS; backend redirection is disabled to avoid redirect loops.
The existing API/MCP contracts remain unauthenticated and internet-accessible;
review the desired access policy before making the endpoints public.

### Publish the dashboard to Azure

The UI must be built with the API URL from the deployment; `/api` is only the
local Vite/reverse-proxy default. Its existing SignalR client supports direct
cross-origin HTTPS. The Bicep template has already allowed the exact SWA origin.

```powershell
$env:VITE_API_BASE_URL = $urls.api
npm --prefix ui ci
npm --prefix ui run build
$env:SWA_CLI_DEPLOYMENT_TOKEN = az staticwebapp secrets list -g weather -n $resources.ui --query properties.apiKey -o tsv
npx --yes --package @azure/static-web-apps-cli swa deploy ui/dist --env production
Remove-Item Env:SWA_CLI_DEPLOYMENT_TOKEN
```

Treat the SWA deployment token as a secret. Do not echo it or commit it. The
`staticwebapp.config.json` in `ui/public/` is copied to `ui/dist/` for SPA fallback.
There is no Vercel build, API proxy, linked Azure backend, or managed Functions API.

### Verify the deployed flow

Verify the dashboard at `$urls.ui`, REST `/Devices`, SignalR `/weatherHub/negotiate`
and actual `weatherData` updates, plus MCP discovery/current-conditions/history at
`$urls.mcp`. Verify both healthy and unavailable dependency responses. Database
history is empty until the explicit importer runs; never fabricate readings.
The low-cost liveness URL is `$urls.api/health`. Detailed health contacts SQL and
Ambient and should not be used as an always-on keepalive or frequent quota consumer.
The current deployment passed this flow on November 15, 2026; rerun live verification after subsequent deployments.

If switching an existing domain/bookmark, first verify the Azure dashboard. Remove
or disable the old Vercel project separately only after the Azure replacement is
working; none of these templates manage external Vercel resources.
