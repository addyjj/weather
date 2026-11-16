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

The UI's Vite proxy continues to use `http://localhost:5001`; set
`WEATHER_API_URL` in the UI process if the mapped API port changes. Production UI
hosting can use a reverse proxy forwarding `/api` (including WebSockets) to the API
with the prefix stripped. To call the API directly from a different origin, set
`VITE_API_BASE_URL` on the UI to the API's HTTPS address and configure
`Cors__AllowedOrigins__0` on the REST host to the UI origin (additional origins use
indices 1, 2, etc.). Compose exposes the first origin through `WEATHER_UI_ORIGIN`.
Only listed origins receive CORS headers; credentialed cookies are not enabled.
The existing UI's SignalR client uses `withCredentials: false`.

The REST host enables HTTPS redirection by default, with an optional `Https__Port`.
The HTTP-only container image and Compose stack explicitly disable backend
redirection. `WEATHER_HTTPS_REDIRECT_ENABLED` can override that Compose setting
when providing a real HTTPS listener/certificate. With TLS terminated at a reverse
proxy, enforce HTTPS there and leave backend redirection disabled. User Secrets
are available to all three hosts in Development, share the same local secret
store, and are never copied into containers or deployment artifacts. Production
uses environment/App Service settings instead. See the README for secret commands
and configuration precedence.

## Azure-only free-tier infrastructure and hosting

The current hosting proposal is declared in `infra/main.bicep`. See the
[complete resource inventory, free limits, and review-first deployment steps](../infra/README.md).
It uses Static Web Apps Free for the dashboard, a shared F1 Linux App Service plan
for REST/SignalR and MCP, and the Azure SQL free offer with monthly-limit AutoPause.
The UI calls the API directly over HTTPS; its production build receives the actual
API URL via `VITE_API_BASE_URL`. The Bicep template sets the matching CORS origin.
Vercel and Static Web Apps paid linked-backend features are not required.

App Service and SQL target West US 3; Static Web Apps remains in West US 2.
West US 2 SQL provisioning failed despite a successful what-if. Run the SQL regional
capability check and full `what-if` before every deployment. Never substitute a
paid SKU or enable SQL paid overages to bypass a quota failure.

The old weather resource group and its deployment identity were deleted. The
former GitHub secrets, Azure OIDC assumptions, App Service settings, and Vercel
origin are not used by this plan. Initial deployment uses the authenticated human
Azure CLI session. Runtime SQL access uses the web apps' system-assigned managed
identities and explicit least-privilege database grants; no SQL password is stored.

## GitHub Actions verification and artifacts

`.github/workflows/build-and-deploy.yml` runs on PRs, pushes to `main`/`master`,
and manual dispatch. It builds/tests .NET with disposable LocalDB, builds/tests the
UI and Storybook, runs browser workflows in proxy and cross-origin modes, validates
the free-only Bicep resource inventory, and validates/smoke-tests the Docker images.
After all verification jobs pass, it publishes downloadable webapi, MCP, importer,
UI, and database migration artifacts. It does not provision resources, deploy to
Azure, apply migrations, or run imports on a push. Publishing an artifact in CI is
not publishing the application to the internet.

Follow the approved manual Bicep and application deployment commands in
`infra/README.md`. Any future automated Azure deployment identity and permissions
must be added to the resource inventory before creation.
