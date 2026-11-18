using 'main.bicep'

// No secrets here: identities replace passwords, and anything secret lives in Key Vault.
// The pipeline passes apiImage at deploy time.
param apiImage = readEnvironmentVariable('ITASK_API_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')
param apiMaxReplicas = 2
