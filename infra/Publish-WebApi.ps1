[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutputPath = 'artifacts/webapi',
    [string] $ZipPath = 'artifacts/webapi.zip',
    [string] $WebJobZipPath = 'artifacts/weather-import.zip'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sitePath = [IO.Path]::GetFullPath($OutputPath, $repoRoot)
$siteZip = [IO.Path]::GetFullPath($ZipPath, $repoRoot)
$jobZip = [IO.Path]::GetFullPath($WebJobZipPath, $repoRoot)
$jobPath = Join-Path $sitePath 'App_Data/jobs/triggered/weather-import'

dotnet publish (Join-Path $repoRoot 'src/Weather.WebApi') -c $Configuration -o $sitePath
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
# Self-contained output does not assume Kudu has the web app's .NET runtime.
dotnet publish (Join-Path $repoRoot 'src/Weather.Import') -c $Configuration -r linux-x64 --self-contained true -p:UseAppHost=true -o $jobPath
if ($LASTEXITCODE -ne 0) { throw 'Import WebJob publish failed.' }
$localSettings = Get-ChildItem -LiteralPath $sitePath -Recurse -File | Where-Object { $_.Name -eq 'appsettings.Local.json' -or $_.Name -like '.env*' }
if ($localSettings) { throw 'Local configuration must not be packaged.' }
foreach ($archivePath in @($siteZip, $jobZip)) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($archivePath)) -Force | Out-Null
}
Compress-Archive -Path (Join-Path $jobPath '*') -DestinationPath $jobZip -Force
Compress-Archive -Path (Join-Path $sitePath '*') -DestinationPath $siteZip -Force
Write-Output "API with triggered weather-import WebJob: $siteZip"
Write-Output "Standalone WebJob upload: $jobZip"
