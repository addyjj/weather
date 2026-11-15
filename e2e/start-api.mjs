import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { resolve } from "node:path";
import { Server } from "socket.io";

const reading = {
  date: "2026-11-14T12:00:00Z",
  tempf: 68,
  feelsLike: 67.5,
  dewPoint: 51,
  humidity: 55,
  winddir: 270,
  windspeedmph: 3.5,
  windgustmph: 8,
  baromrelin: 30.1,
  eventrainin: 0.1,
  dailyrainin: 0.2,
  uv: 2,
  solarradiation: 125,
};
const ambient = createServer(async (req, res) => {
  const url = new URL(req.url, "http://127.0.0.1:5129");
  res.setHeader("Content-Type", "application/json");
  if (url.pathname === "/push" && req.method === "POST") {
    let body = "";
    for await (const chunk of req) body += chunk;
    io.emit("data", { ...reading, ...JSON.parse(body) });
    res.end("{}");
  } else if (url.pathname === "/disconnect" && req.method === "POST") {
    for (const socket of io.sockets.sockets.values()) socket.disconnect(true);
    res.end("{}");
  } else if (url.pathname === "/state") {
    res.end(JSON.stringify({ subscribers }));
  } else if (
    url.searchParams.get("apiKey") !== "e2e-api" ||
    url.searchParams.get("applicationKey") !== "e2e-app"
  ) {
    res.writeHead(401).end("{}");
  } else if (url.pathname === "/devices") {
    res.end(
      JSON.stringify([
        {
          info: {
            name: "Backyard station",
            coords: { location: "Chicago, IL" },
          },
          lastData: reading,
        },
      ]),
    );
  } else {
    res.end(JSON.stringify([reading]));
  }
});
let subscribers = 0;
const io = new Server(ambient, { allowEIO3: true, transports: ["websocket"] });
io.on("connection", (socket) => {
  socket.on("subscribe", (subscription) => {
    if (
      socket.handshake.query.applicationKey !== "e2e-app" ||
      subscription.apiKeys?.[0] !== "e2e-api"
    ) {
      socket.disconnect();
      return;
    }
    subscribers++;
    socket.emit("subscribed", { devices: [] });
  });
});
ambient.listen(5129, "127.0.0.1");
const project = resolve(
  process.env.WEATHER_API_PROJECT ||
    "../Weather.Mcp/src/Weather.WebApi/Weather.WebApi.csproj",
);
const api = spawn(
  "dotnet",
  ["run", "--project", project, "-c", "Release", "--no-launch-profile"],
  {
    windowsHide: true,
    stdio: "inherit",
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      ASPNETCORE_URLS: "http://127.0.0.1:5128",
      Ambient__ApiUrl: "http://127.0.0.1:5129/",
      Ambient__SocketUrl: "http://127.0.0.1:5129",
      Ambient__ApiKey: "e2e-api",
      Ambient__ApplicationKey: "e2e-app",
    },
  },
);
api.on("exit", (code) => {
  io.close();
  ambient.close();
  process.exit(code ?? 1);
});
for (const signal of ["SIGINT", "SIGTERM"])
  process.on(signal, () => {
    api.kill();
    io.close();
    ambient.close();
  });
