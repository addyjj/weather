# Weather UI

React dashboard for the REST and SignalR host in the sibling Weather.Mcp repository.

## Run locally

Install Node.js 24.13+ and the .NET 10 SDK. From Weather.Mcp, configure Ambient credentials
in the environment or ignored `src/Weather.WebApi/appsettings.Local.json`, then run:

```powershell
dotnet run --project src/Weather.WebApi --no-launch-profile
```

From weather-ui:

```powershell
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

For production, route `/api/*` to the REST host with the prefix removed and enable
WebSocket forwarding in the reverse proxy. Vite's development proxy is not included
in static build output. Direct cross-origin hosting needs explicit API CORS configuration.

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
Ports 5128, 5129, and 5178 must be available. Set `WEATHER_API_PROJECT` if the backend
is not at `../Weather.Mcp/src/Weather.WebApi/Weather.WebApi.csproj`.
Failure traces and screenshots are written to ignored `test-results/`.
