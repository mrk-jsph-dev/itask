# Security policy

Please report vulnerabilities privately through GitHub's
**Security → Report a vulnerability** on this repository. Do not open a public issue.

You can expect an acknowledgement within a few days. Please give reasonable time
for a fix before any disclosure.

## How this repo is protected

- No secrets in the repository. Azure is reached from CI through OpenID Connect,
  and the app uses managed identities; the few remaining app secrets are Container Apps secrets
  set from GitHub environment secrets at deploy time.
- Deployments run only from `main`, only in the `production` environment, and only
  after the owner approves. Pull requests, including from forks, never get cloud access.
- Actions are pinned to full commit SHAs and run with read-only tokens by default.
- Secret scanning with push protection, Dependabot and CodeQL are enabled.
