import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const root = JSON.parse(readFileSync(new URL("../artifacts/infra/main.json", import.meta.url), "utf8"));
const deployment = root.resources.find((resource) => resource.type === "Microsoft.Resources/deployments");
const stack = deployment.properties.template;
const resources = stack.resources;
const ofType = (type) => resources.filter((resource) => resource.type === type);

test("the resource inventory cannot silently add metered supporting services", () => {
  assert.deepEqual(root.resources.map((resource) => resource.type).sort(), [
    "Microsoft.Resources/deployments", "Microsoft.Resources/resourceGroups",
  ]);
  const inventory = Object.fromEntries([...new Set(resources.map((resource) => resource.type))]
    .map((type) => [type, ofType(type).length]));
  assert.deepEqual(inventory, {
    "Microsoft.Web/serverfarms": 1,
    "Microsoft.Web/staticSites": 1,
    "Microsoft.Sql/servers": 1,
    "Microsoft.Sql/servers/databases": 1,
    "Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies": 1,
    "Microsoft.Sql/servers/firewallRules": 2,
    "Microsoft.Web/sites": 2,
    "Microsoft.Web/sites/config": 2,
    "Microsoft.Web/sites/basicPublishingCredentialsPolicies": 4,
  });
  assert.equal(Object.keys(stack.parameters).some((name) => /sku|tier|capacity|exhaustion/i.test(name)), false);
});

test("hosting stays on fixed Free SKUs with no always-on or scaled workers", () => {
  assert.deepEqual(ofType("Microsoft.Web/serverfarms")[0].sku, { name: "F1", tier: "Free" });
  const ui = ofType("Microsoft.Web/staticSites")[0];
  assert.deepEqual(ui.sku, { name: "Free", tier: "Free" });
  assert.equal(ui.properties.stagingEnvironmentPolicy, "Disabled");
  assert.equal(ui.properties.repositoryToken, undefined);
  assert.equal(ui.properties.repositoryUrl, undefined);
  for (const app of ofType("Microsoft.Web/sites")) {
    assert.equal(app.properties.siteConfig.alwaysOn, false);
    assert.equal(app.properties.siteConfig.linuxFxVersion, "DOTNETCORE|10.0");
    assert.equal(app.properties.serverFarmId.includes("Microsoft.Web/serverfarms"), true);
  }
});

test("SQL stops at the free allowance, caps data at 32 GiB, and avoids paid backup options", () => {
  const database = ofType("Microsoft.Sql/servers/databases")[0];
  assert.equal(database.properties.useFreeLimit, true);
  assert.equal(database.properties.freeLimitExhaustionBehavior, "AutoPause");
  assert.equal(database.properties.maxSizeBytes, 32 * 1024 ** 3);
  assert.equal(database.properties.requestedBackupStorageRedundancy, "Local");
  assert.equal(database.properties.zoneRedundant, false);
  assert.deepEqual(database.sku, { name: "GP_S_Gen5", tier: "GeneralPurpose", family: "Gen5", capacity: 2 });
  assert.equal(ofType("Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies")[0].properties.retentionDays, 7);
  assert.equal(ofType("Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies")[0].properties.diffBackupIntervalInHours, 24);
});

test("SQL uses existing Entra admin and scoped app identities rather than passwords", () => {
  const sql = ofType("Microsoft.Sql/servers")[0];
  assert.equal(sql.properties.administrators.azureADOnlyAuthentication, true);
  assert.equal(sql.properties.administratorLoginPassword, undefined);
  assert.equal(sql.properties.administrators.principalType, "User");
  for (const app of ofType("Microsoft.Web/sites")) {
    assert.equal(app.identity.type, "SystemAssigned");
    assert.equal(app.properties.httpsOnly, true);
  }
  for (const setting of ofType("Microsoft.Web/sites/config")) {
    assert.match(setting.properties, /Authentication=Active Directory Managed Identity/);
  }
  assert.equal(ofType("Microsoft.Web/sites/config").every((setting) => setting.properties.includes("Pooling=False")), true);
  assert.equal(ofType("Microsoft.Web/sites/basicPublishingCredentialsPolicies").every((policy) => policy.properties.allow === false), true);
});

test("Ambient credentials stay secure inputs and are never deployment outputs", () => {
  for (const name of ["ambientApiKey", "ambientApplicationKey"]) {
    assert.equal(root.parameters[name].type, "securestring");
    assert.equal(stack.parameters[name].type, "securestring");
    assert.equal(JSON.stringify(root.outputs).includes(name), false);
    assert.equal(JSON.stringify(stack.outputs).includes(name), false);
  }
});

test("UI CORS uses the provisioned Azure hostname and operator SQL access is opt-in", () => {
  const settings = ofType("Microsoft.Web/sites/config");
  const apiSettings = settings.find((setting) => setting.properties.includes("Cors__AllowedOrigins__0"));
  assert.match(apiSettings.properties, /defaultHostname/);
  assert.equal(JSON.stringify(stack).includes("vercel"), false);
  const operatorRule = ofType("Microsoft.Sql/servers/firewallRules").find((rule) => rule.condition);
  assert.equal(operatorRule.condition, "[not(empty(parameters('operatorIpAddress')))]");
  assert.equal(root.parameters.operatorIpAddress.defaultValue, "");
});

test("the Static Web Apps build has SPA fallback and no paid linked backend", () => {
  const config = JSON.parse(readFileSync(new URL("../ui/public/staticwebapp.config.json", import.meta.url), "utf8"));
  assert.equal(config.navigationFallback.rewrite, "/index.html");
  assert.equal(config.navigationFallback.exclude.includes("/api/*"), true);
  assert.equal(config.platform, undefined);
  assert.equal(config.forwardingGateway, undefined);
});
