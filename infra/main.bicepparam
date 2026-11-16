using './main.bicep'

param resourceGroupName = 'weather'
param location = 'westus3'
param staticSiteLocation = 'westus2'
param namePrefix = 'weather'
param sqlAdminObjectId = readEnvironmentVariable('WEATHER_SQL_ADMIN_OBJECT_ID')
param sqlAdminLogin = readEnvironmentVariable('WEATHER_SQL_ADMIN_LOGIN')
param ambientApiKey = readEnvironmentVariable('WEATHER_AMBIENT_API_KEY')
param ambientApplicationKey = readEnvironmentVariable('WEATHER_AMBIENT_APPLICATION_KEY')
param ambientDeviceMacAddress = readEnvironmentVariable('WEATHER_AMBIENT_MAC_ADDRESS')
param operatorIpAddress = readEnvironmentVariable('WEATHER_OPERATOR_IP', '')
