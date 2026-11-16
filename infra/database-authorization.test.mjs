import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmdirSync, unlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { delimiter, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const repoRoot = fileURLToPath(new URL("../", import.meta.url));
const apiObjectId = "11111111-1111-4111-8111-111111111111";
const mcpObjectId = "22222222-2222-4222-8222-222222222222";
const apiClientId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const mcpClientId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

function renderWithAzureFixture(apiAppId = apiClientId) {
  const directory = mkdtempSync(join(tmpdir(), "weather-authorization-test-"));
  const commandPath = join(directory, process.platform === "win32" ? "az.cmd" : "az");
  const outputPath = join(directory, "authorize.sql");
  const output = JSON.stringify({identities:{value:{apiPrincipalId:apiObjectId,mcpPrincipalId:mcpObjectId}}});
  const mock = process.platform === "win32"
    ? `@echo off\r\nif "%~1"=="deployment" (\r\n echo ${output}\r\n exit /b 0\r\n)\r\nif "%~5"=="${apiObjectId}" (\r\n echo ${apiAppId}\r\n exit /b 0\r\n)\r\nif "%~5"=="${mcpObjectId}" (\r\n echo ${mcpClientId}\r\n exit /b 0\r\n)\r\nexit /b 1\r\n`
    : `#!/bin/sh\nif [ "$1" = "deployment" ]; then\n printf '%s\\n' '${output}'\n exit 0\nfi\ncase "$5" in\n '${apiObjectId}') printf '%s\\n' '${apiAppId}'; exit 0;;\n '${mcpObjectId}') printf '%s\\n' '${mcpClientId}'; exit 0;;\nesac\nexit 1\n`;
  try {
    writeFileSync(commandPath, mock, {mode:0o700});
    const result = spawnSync("pwsh", ["-NoLogo","-NoProfile","-File",join(repoRoot,"infra","Write-DatabaseAuthorization.ps1"),"-OutputPath",outputPath], {
      cwd:repoRoot, env:{...process.env, PATH:directory + delimiter + process.env.PATH}, encoding:"utf8", timeout:30000
    });
    if (result.error) throw result.error;
    return {...result, sql:existsSync(outputPath) ? readFileSync(outputPath,"utf8") : undefined};
  } finally {
    // Remove only the two files created by this fixture; no recursive deletion.
    if (existsSync(outputPath)) unlinkSync(outputPath);
    if (existsSync(commandPath)) unlinkSync(commandPath);
    rmdirSync(directory);
  }
}

test("database authorization resolves managed identity client IDs instead of using ARM object IDs", () => {
  const result = renderWithAzureFixture();
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.sql.includes(`'${apiClientId}'`));
  assert.ok(result.sql.includes(`'${mcpClientId}'`));
  assert.equal(result.sql.includes(apiObjectId), false);
  assert.equal(result.sql.includes(mcpObjectId), false);
  assert.equal(result.sql.includes("$("), false);
  assert.ok(result.sql.includes("GRANT CONNECT TO [weather-api]"));
  assert.ok(result.sql.includes("GRANT SELECT ON OBJECT::dbo.DeviceData TO [weather-mcp]"));
  assert.ok(result.sql.includes("GRANT SELECT, INSERT ON OBJECT::dbo.DeviceData TO [weather-api]"));
  assert.doesNotMatch(result.sql, /GRANT\s+(?:UPDATE|DELETE|CONTROL)|db_owner/i);
  assert.doesNotMatch(result.sql, /GRANT[^;\n]*INSERT[^;\n]*TO \[weather-mcp\]/i);
});

test("invalid identity metadata stops authorization generation before SQL is written", () => {
  const result = renderWithAzureFixture("not-a-client-guid");
  assert.notEqual(result.status, 0);
  assert.equal(result.sql, undefined);
});
