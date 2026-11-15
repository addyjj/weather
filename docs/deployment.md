# Database migrations, containers, and deployment

## EF Core migrations

Migrations are in `src/Weather.Infrastructure/WeatherHistory/Migrations`. The
repository pins `dotnet-ef` 10.0.15 to match its EF Core packages. Restore the tool:

```powershell
dotnet tool restore
dotnet ef migrations list --project src/Weather.Infrastructure --no-connect
dotnet ef migrations has-pending-model-changes --project src/Weather.Infrastructure
dotnet ef migrations script --idempotent --project src/Weather.Infrastructure -o artifacts/migrate.sql
```

The design-time factory reads `ConnectionStrings__sql` from the process environment.
It does not load ignored host settings. With no environment setting, it uses a
localhost `weather` database connection string; script generation and model checks
do not connect. Always set the intended connection explicitly before database updates.

The migration chain preserves `20260213032620_InitialCreate` from Weather.Api. A
second migration aligns `Loc` with `varchar(100)`, `Date` with `datetime`, and the
table with the explicit `dbo` schema used by the current application. It refuses
changes that would lose location characters or timestamp range/precision. Such
records require an explicit data/schema decision before the migration can proceed.

The existing local database was inspected read-only: it already has those column
types and records the original initial migration. The upgrade path for that exact
combination is covered by a disposable LocalDB integration test. No migrations have
been applied to that database by this work.

Review the generated SQL and back up the target database before running an update:

```powershell
$env:ConnectionStrings__sql = 'your intended SQL Server connection string'
dotnet ef database update --project src/Weather.Infrastructure
```

An empty database is created with the complete migration chain. An existing
database with the original EF history keeps its initial migration marked as
applied. If an existing table has no `__EFMigrationsHistory`, do not run the initial
create migration against it: first compare its full schema and establish an
appropriate migration baseline. Application hosts never auto-create or migrate
databases at startup. Deployment also leaves database changes as an explicit step.

For a migration executable that needs no SDK on the destination:

```powershell
dotnet ef migrations bundle --project src/Weather.Infrastructure --self-contained --target-runtime linux-x64 -o artifacts/efbundle
```

Run the Linux bundle on the target with `ConnectionStrings__sql` set (or pass its
standard `--connection` option). After downloading it from GitHub artifacts, run
`chmod +x efbundle` before `./efbundle`, since artifact downloads do not preserve
Unix executable permissions. The bundle is Linux x64; build another runtime ID for
other destinations. SQL migration scripts are platform independent.

## Docker Compose

Install Docker Engine/Desktop with Linux containers and Docker Compose. From the
repository root, copy `.env.example` to `.env` and fill in the SQL connection and
Ambient API/application keys and station MAC. `.env` is ignored by Git and excluded
from image build contexts. Do not put credentials in Compose files or build args.

```powershell
Copy-Item .env.example .env
docker compose --profile tools config --quiet
docker compose up --build -d
```

The default services are:

| Service | Host endpoint |
| --- | --- |
| `weather.webapi` | `http://localhost:5001` (REST, `/weatherHub`, `/health`) |
| `weather.mcp` | `http://localhost:5000/mcp` |

Images run as the .NET image's non-root application user on container port 8080.
Published host ports bind to loopback. Override `WEATHER_API_PORT` and
`WEATHER_MCP_PORT` if those ports are occupied by local `dotnet run` instances.
`WEATHER_ALLOWED_HOSTS` and `WEATHER_MCP_ALLOWED_HOSTS` can set permitted request
hostnames for proxies. The defaults allow localhost, loopback, and the Compose
service name. HTTPS and public access control belong at the deployment boundary.

Use a reachable SQL Server or Azure SQL database. Windows LocalDB cannot be reached
from Linux containers. `host.docker.internal` works for a SQL Server on a Docker
Desktop host; enable the SQL Server TCP listener/firewall deliberately. On a plain
Linux Docker host, supply a reachable hostname/IP instead. SQL Server is external
to this Compose stack, matching the original API's setup; no database volume is
created or removed by Compose.

The tools profile adds explicit one-shot commands:

```powershell
# Review/apply migrations against the connection in .env:
docker compose --profile tools run --build --rm weather.migrate
# Import station history after the schema is ready:
docker compose --profile tools run --build --rm weather.import
docker compose logs -f weather.webapi weather.mcp
docker compose down
```

The single Dockerfile provides `webapi`, `mcp`, `import`, and `migrate` targets.
Normal `compose up` does not import data or apply migrations. The migration image
is Linux x64. All targets use .NET 10. Local settings files and development
credentials are excluded from both image contexts and published app artifacts.

The sibling UI's Vite proxy continues to use `http://localhost:5001`; set
`WEATHER_API_URL` in the UI process if the mapped API port changes. Production UI
hosting needs a reverse proxy forwarding `/api` (including WebSockets) to the API
with the prefix stripped, or deliberate cross-origin configuration.

## GitHub Actions and Azure App Service

`.github/workflows/build-and-deploy.yml` replaces the old Azure workflow's project
paths and .NET version and adds verification gates:

1. Windows builds/tests the full solution using disposable LocalDB databases.
2. Linux validates Compose, builds all four Docker targets, and smoke-tests the
   importer/migrator help commands, REST liveness, and MCP initialization.
3. After both pass, Linux publishes API, MCP, importer, and reviewed migration
   artifacts. It also checks the EF snapshot for unscaffolded changes and rejects
   local settings in published output.
4. Optional Azure deployment jobs run after publishing. The workflow does not
   apply database migrations or run an import.

CI runs on PRs and pushes to `main`/`master`. Azure deployment runs on pushes to
`main` or manual `workflow_dispatch`, and only for apps whose repository
variables have been configured. A push to `master` verifies/publishes without
deploying. Configure the `production` GitHub environment with the protection rules
appropriate to the deployment and these OIDC secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Create an Azure federated identity credential for the repository's `production`
environment and give it deployment permission for the selected apps. Configure:

| GitHub variable | Purpose |
| --- | --- |
| `AZURE_WEBAPP_NAME` | Existing REST App Service name, e.g. `weather-api`; blank skips REST deployment |
| `AZURE_WEBAPP_SLOT` | REST slot; defaults to `Production` |
| `AZURE_MCP_WEBAPP_NAME` | Optional MCP App Service name; blank skips MCP deployment |
| `AZURE_MCP_WEBAPP_SLOT` | MCP slot; defaults to `Production` |

The old repository's generated Azure secret names are not assumed to exist in this
repository. No Azure resources, credentials, or live deployments are created by
adding the workflow. The importer remains a downloaded one-shot artifact or
container; it is not started as an App Service.

Use Linux App Services with the .NET 10 runtime. Configure their startup command as
`dotnet Weather.WebApi.dll` or `dotnet Weather.Mcp.dll`, and set
`ASPNETCORE_URLS=http://0.0.0.0:8080` (or the port required by the selected host).
Set `AllowedHosts` to each app's actual hostname, and configure
`ConnectionStrings__sql`, `Ambient__ApiKey`, `Ambient__ApplicationKey`,
`Ambient__DeviceMacAddress`, and any custom Ambient URLs as App Service settings.
Keep secrets out of GitHub artifacts. Enable WebSockets for the REST app, keep its
instance count at one for the existing in-process SignalR broadcast behavior, and
use HTTPS/access controls when exposing either host remotely. Set the App Service
health-check path to `/health`; use `/health/details` separately for dependency
diagnostics and account for Ambient API quotas.

Changes to SQL remain a separate, reviewed migration step. Run them using a
database deployment identity with DDL permissions; runtime identities need only
their normal read/import permissions. Review migration artifacts before promoting
an application version that depends on schema changes.
