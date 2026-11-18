using 'main.bicep'

// ITASK_API_IMAGE is set by the pipeline; the default lets the file build locally.
param apiImage = readEnvironmentVariable('ITASK_API_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')
param apiMaxReplicas = 2
param sqlAdminGroupId = readEnvironmentVariable('ITASK_SQL_ADMIN_GROUP_ID', '')

// Secrets come from GitHub environment secrets in the deploy job; empty when building locally.
param demoPassword = readEnvironmentVariable('ITASK_DEMO_PASSWORD', '')
param turnstileSecretKey = readEnvironmentVariable('ITASK_TURNSTILE_SECRET_KEY', '')
