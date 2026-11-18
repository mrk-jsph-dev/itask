using 'main.bicep'

// ITASK_API_IMAGE is set by the pipeline; the default lets the file build locally.
param apiImage = readEnvironmentVariable('ITASK_API_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')
param apiMaxReplicas = 2
