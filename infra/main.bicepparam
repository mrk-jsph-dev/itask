using 'main.bicep'

// ITASK_API_IMAGE is set by the pipeline; the default lets the file build locally.
param apiImage = readEnvironmentVariable('ITASK_API_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')
param apiMaxReplicas = 1

// Secrets come from GitHub environment secrets in the deploy job; empty when building locally.
param demoPassword = readEnvironmentVariable('ITASK_DEMO_PASSWORD', '')
param turnstileSecretKey = readEnvironmentVariable('ITASK_TURNSTILE_SECRET_KEY', '')
param platformAdminEmails = readEnvironmentVariable('ITASK_PLATFORM_ADMIN_EMAILS', '')
param smtpUsername = readEnvironmentVariable('ITASK_SMTP_USERNAME', '')
param smtpPassword = readEnvironmentVariable('ITASK_SMTP_PASSWORD', '')
param githubClientSecret = readEnvironmentVariable('ITASK_GITHUB_CLIENT_SECRET', '')
param microsoftClientSecret = readEnvironmentVariable('ITASK_MICROSOFT_CLIENT_SECRET', '')
param aiEndpoint = readEnvironmentVariable('ITASK_AI_ENDPOINT', '')
param aiDeployment = readEnvironmentVariable('ITASK_AI_DEPLOYMENT', '')
param sqlAdminGroupId = readEnvironmentVariable('ITASK_SQL_ADMIN_GROUP_ID', '')

// OAuth client ids are not secrets (they come from GitHub environment variables).
param githubClientId = readEnvironmentVariable('ITASK_GITHUB_CLIENT_ID', '')
param microsoftClientId = readEnvironmentVariable('ITASK_MICROSOFT_CLIENT_ID', '')
