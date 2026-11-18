using 'bootstrap.bicep'

// Your email stays out of the repo: export ITASK_ALERT_EMAIL before deploying.
param alertEmail = readEnvironmentVariable('ITASK_ALERT_EMAIL')
