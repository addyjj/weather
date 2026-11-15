import { execFileSync } from "node:child_process";
import { appendFileSync } from "node:fs";

const [resourceGroup, appName, slotName = "Production"] = process.argv.slice(2);
if (!resourceGroup || !appName) throw new Error("Azure resource group and app name are required.");
const target = ["-g", resourceGroup, "-n", appName];
if (slotName.toLowerCase() !== "production") target.push("--slot", slotName);
function az(args) {
  try {
    return execFileSync("az", args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  } catch {
    throw new Error("Azure settings operation failed. Credential values have been withheld.");
  }
}
function settingMap(entries) {
  return new Map(entries.map(({ name, value }) => [name.replaceAll(":", "__").toLowerCase(), value]));
}

try {
  const settings = settingMap(JSON.parse(az(["webapp", "config", "appsettings", "list", ...target, "-o", "json"])));
  const apiKey = settings.get("ambient__apikey");
  const applicationKey = settings.get("ambient__applicationkey");
  if (!apiKey || !applicationKey) throw new Error("Configure Ambient API and application keys in App Service settings.");
  const changes = [];
  // Preserve the legacy API's endpoint default when no Azure override exists.
  const apiUrl = settings.get("ambient__apiurl") || "https://rt.ambientweather.net/v1/";
  if (!settings.get("ambient__apiurl")) changes.push(`Ambient__ApiUrl=${apiUrl}`);
  if (!settings.get("ambient__devicemacaddress")) {
    let devices;
    try {
      const url = new URL("devices", apiUrl.replace(/\/$/, "") + "/");
      url.searchParams.set("apiKey", apiKey);
      url.searchParams.set("applicationKey", applicationKey);
      const response = await fetch(url, { signal: AbortSignal.timeout(30000) });
      if (!response.ok) throw new Error();
      devices = await response.json();
    } catch {
      throw new Error("Ambient station discovery failed. Check the configured API URL and credentials.");
    }
    if (!Array.isArray(devices) || devices.length !== 1 || !devices[0]?.macAddress)
      throw new Error("Set Ambient__DeviceMacAddress explicitly; automatic selection requires exactly one registered station.");
    changes.push(`Ambient__DeviceMacAddress=${devices[0].macAddress}`);
    console.log("Selected the account's single registered station for dependency checks.");
  }
  if (changes.length) az(["webapp", "config", "appsettings", "set", ...target, "--settings", ...changes, "-o", "none"]);
  const connections = JSON.parse(az(["webapp", "config", "connection-string", "list", ...target, "-o", "json"]));
  const databaseConfigured = Boolean(settings.get("connectionstrings__sql") ||
    connections.some(entry => entry.name.toLowerCase() === "sql" && entry.connectionString));
  if (process.env.GITHUB_OUTPUT)
    appendFileSync(process.env.GITHUB_OUTPUT, `database_configured=${databaseConfigured}\n`);
  console.log(databaseConfigured ? "SQL connection is configured; deployment will require database health." :
    "::notice::SQL is not configured in Azure. Device REST/SignalR endpoints do not require SQL; detailed health will continue reporting this explicitly.");
} catch (error) {
  console.error(error instanceof Error ? error.message : "Azure configuration failed.");
  process.exitCode = 1;
}
