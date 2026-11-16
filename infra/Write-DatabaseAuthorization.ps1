[CmdletBinding()]
param(
    [string] $DeploymentName = 'weather-free',
    [string] $OutputPath = 'artifacts/authorize-apps.sql'
)
$ErrorActionPreference = 'Stop'
$azureCli = (Get-Command az -ErrorAction Stop).Source
$outputJson = & $azureCli deployment sub show --name $DeploymentName --query properties.outputs --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read the completed infrastructure deployment outputs.' }
$outputs = ($outputJson -join "`n") | ConvertFrom-Json
$apiPrincipalId = ([guid]::Parse($outputs.identities.value.apiPrincipalId)).ToString()
$mcpPrincipalId = ([guid]::Parse($outputs.identities.value.mcpPrincipalId)).ToString()
$apiClientId = & $azureCli ad sp show --id $apiPrincipalId --query appId --output tsv
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve API managed identity client ID.' }
$mcpClientId = & $azureCli ad sp show --id $mcpPrincipalId --query appId --output tsv
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve MCP managed identity client ID.' }
$apiClientId = ([guid]::Parse($apiClientId.Trim())).ToString()
$mcpClientId = ([guid]::Parse($mcpClientId.Trim())).ToString()
$sql = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'authorize-apps.sql'))
$sql = $sql.Replace('$(ApiClientId)', $apiClientId).Replace('$(McpClientId)', $mcpClientId)
$targetPath = [IO.Path]::GetFullPath($OutputPath, (Join-Path $PSScriptRoot '..'))
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($targetPath)) -Force | Out-Null
[IO.File]::WriteAllText($targetPath, $sql)
Write-Output "Prepared SQL authorization using managed identity client IDs: $targetPath"
