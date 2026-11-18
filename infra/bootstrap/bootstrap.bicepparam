using 'bootstrap.bicep'

// Set ITASK_ALERT_EMAIL in the shell before deploying.
param alertEmail = readEnvironmentVariable('ITASK_ALERT_EMAIL')
