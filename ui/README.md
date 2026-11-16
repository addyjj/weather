# Weather UI

React dashboard for the REST and SignalR host in this repository. The frontend lives in `ui/`; the .NET solution lives at the repository root.

## Run locally

Install Node.js 24.13+ and the .NET 10 SDK. From the repository root, configure Ambient credentials
in the environment or ignored `src/Weather.WebApi/appsettings.Local.json`, then run:

```powershell
dotnet run --project src/Weather.WebApi --no-launch-profile -- --Https:RedirectEnabled=false
```

In another terminal, from the repository root:

```powershell
cd ui
npm ci
npm run dev
```

Open the URL printed by Vite. Requests to `/api/devices` and `/api/weatherHub`
(including WebSockets) are proxied to `http://localhost:5001`; the `/api` prefix is
removed. Set `WEATHER_API_URL` on the Vite process to use another backend address.
`VITE_API_BASE_URL` defaults to `/api` and can override the browser's API base URL.
Restart Vite after changing environment files. Credentials stay on the server.

The dashboard loads the initial reading over REST and receives `weatherData`
updates from the SignalR hub. Failed initial connections retry; established
connections reconnect automatically. Older events do not replace newer readings.

To host the UI separately, set `VITE_API_BASE_URL` to the API's HTTPS origin
(without `/api`), and configure `Cors__AllowedOrigins__0` on the API to the UI's
origin, without a trailing slash. Additional allowed origins use indices 1, 2, etc.
The API supports REST and SignalR negotiation with the UI's `withCredentials: false`.
Use the HTTPS API URL directly; a browser preflight should not rely on redirection.
The HTTP-only local command above explicitly disables HTTPS redirection.

For production, route `/api/*` to the REST host with the prefix removed and enable
WebSocket forwarding in the reverse proxy. Vite's development proxy is not included
in static build output. Direct cross-origin hosting uses the API CORS configuration described above.

## Verify

```powershell
npm run build
npm test
npx playwright install chromium
npm run test:e2e
```

Playwright starts the real .NET REST/SignalR host, Vite, and a local Ambient HTTP/
Socket.IO stub. It checks initial weather rendering, live server-pushed readings,
and loading, empty, and error states. No live Ambient credentials are needed.
Ports 5128, 5129, 5178, and 5179 must be available. Set `WEATHER_API_PROJECT` if the backend
is not at `../src/Weather.WebApi/Weather.WebApi.csproj`.
Failure traces and screenshots are written to ignored `test-results/`.

Playwright runs the three dashboard scenarios in both proxy and cross-origin modes
(six tests total), including REST CORS headers and live SignalR updates.

## Azure hosting

The frontend is hosted on Azure Static Web Apps **Free**. Review the
[Azure resource inventory](../infra/README.md) before creating resources or publishing.
Build with `VITE_API_BASE_URL` set to the provisioned API's HTTPS origin, without
`/api`. The template permits the exact Static Web App origin on the API. This
uses the existing cross-origin flow and does not require a paid linked backend.
`public/staticwebapp.config.json` supplies SPA fallback in the static build.
