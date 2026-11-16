[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]+$')]
    [string] $Location,
    [string] $SubscriptionId
)

$ErrorActionPreference = 'Stop'
$azureCli = (Get-Command az -ErrorAction Stop).Source
if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
    $SubscriptionId = & $azureCli account show --query id --output tsv
    if ($LASTEXITCODE -ne 0) { throw 'Sign in to the intended Azure subscription first.' }
    $SubscriptionId = $SubscriptionId.Trim()
}
$SubscriptionId = ([guid]::Parse($SubscriptionId)).ToString()
$capabilityJson = & $azureCli rest --method get --url "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Sql/locations/$Location/capabilities?api-version=2023-08-01" --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not read SQL regional capability; do not deploy without checking.' }
$capability = ($capabilityJson -join "`n") | ConvertFrom-Json
$serverVersion = $capability.supportedServerVersions | Where-Object name -eq '12.0'
if ($capability.status -ne 'Available' -or $serverVersion.status -notin @('Available', 'Default')) {
    throw "SQL provisioning is not available in $Location for this subscription. $($capability.reason)"
}
Write-Output "SQL provisioning is available in $Location. Also run the full what-if to check free App Service quota before creation."
