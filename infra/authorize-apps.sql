-- Run in weather-db as the Entra administrator AFTER applying EF migrations.
-- Resolve each deployed identity's appId/client ID using Write-DatabaseAuthorization.ps1.
-- SQL's manual SID form requires client IDs for service principals and managed identities.
-- Do not substitute the principal/object IDs returned by the web-app ARM resource.
-- Uses explicit SIDs so no SQL server Graph directory-reader permission is needed.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @apiSid varbinary(16) = CONVERT(binary(16), CONVERT(uniqueidentifier, '$(ApiClientId)'));
DECLARE @mcpSid varbinary(16) = CONVERT(binary(16), CONVERT(uniqueidentifier, '$(McpClientId)'));

-- Prevent a rerun against a recreated app from silently retaining the old identity.
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'weather-api' AND sid <> @apiSid)
    THROW 50001, 'weather-api already refers to a different identity; review before changing it.', 1;
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'weather-mcp' AND sid <> @mcpSid)
    THROW 50002, 'weather-mcp already refers to a different identity; review before changing it.', 1;

DECLARE @apiSidLiteral varchar(34) = CONVERT(varchar(34), @apiSid, 1);
DECLARE @mcpSidLiteral varchar(34) = CONVERT(varchar(34), @mcpSid, 1);

IF DATABASE_PRINCIPAL_ID(N'weather-api') IS NULL
    EXEC(N'CREATE USER [weather-api] WITH SID = ' + @apiSidLiteral + N', TYPE = E;');
IF DATABASE_PRINCIPAL_ID(N'weather-mcp') IS NULL
    EXEC(N'CREATE USER [weather-mcp] WITH SID = ' + @mcpSidLiteral + N', TYPE = E;');

-- The triggered importer shares the API app's managed identity.
GRANT CONNECT TO [weather-api];
GRANT SELECT, INSERT ON OBJECT::dbo.DeviceData TO [weather-api];
GRANT CONNECT TO [weather-mcp];
GRANT SELECT ON OBJECT::dbo.DeviceData TO [weather-mcp];
COMMIT;
