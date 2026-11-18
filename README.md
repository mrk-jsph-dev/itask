# iTask

A board-and-card task manager, rebuilt: **ASP.NET Core 10** API, **Vue 3** frontend,
and **Azure** infrastructure in **Bicep**, designed to run on free tiers.

```
backend/    ASP.NET Core minimal API, vertical slices (see backend/README.md)
frontend/   Vue 3 + Vite + TypeScript, feature-first (see frontend/README.md)
infra/      Bicep: bootstrap/ (one-time, run by the owner) and main.bicep (deployed by CI)
.github/    ci-reusable.yml, ci.yml (pull requests), ci-cd.yml (main + deploy)
```

## Develop

Works the same on a laptop, in a dev container / Codespaces, or in a cloud sandbox:
no database server or cloud account is needed for day-to-day work.

**Requirements:** .NET SDK 10, Node 22+. (Or open the repo in the dev container.)

```sh
# API on http://localhost:5080 (SQLite file, schema created automatically)
cd backend
dotnet user-secrets --project src/ITask.Api set "Demo:Password" "<choose one>"   # optional demo accounts
dotnet run --project src/ITask.Api

# Frontend on http://localhost:5173 (proxies /api to the API)
cd frontend
npm ci
npm run dev
```

Tests: `dotnet test` in `backend/`, `npm run test:unit` and `npm run test:e2e` in `frontend/`.

Want SQL Server locally for parity with Azure SQL? See `docker-compose.yml`.

## Roles

- App level: **Platform admin** and **User**. Admins manage accounts, not other people's boards.
- Board level: **Owner**, **Editor**, **Viewer** (viewers can comment).
- Demo accounts (demo environment only): one per role, on a shared sample board.

## CI/CD

| Workflow | Runs on | Does |
|---|---|---|
| `ci.yml` | pull requests to `main` | calls `ci-reusable.yml`: build, test, lint, Bicep lint, CodeQL |
| `ci-cd.yml` | push to `main`, manual | calls `ci-reusable.yml`, then **deploy** after the owner approves |

The deploy job signs in to Azure with **OIDC** (no stored cloud secret), pushes the
API image to GHCR, deploys `infra/main.bicep`, and publishes the frontend to Static Web Apps.

## First-time Azure setup (owner only)

1. **Bootstrap** (creates `rg-itask`, identities, the GitHub OIDC trust, role
   assignments and a $5 budget):
   ```sh
   az login
   export ITASK_ALERT_EMAIL=<your email>
   az deployment sub create --location southeastasia \
     --template-file infra/bootstrap/bootstrap.bicep \
     --parameters infra/bootstrap/bootstrap.bicepparam
   ```
2. **GitHub → Settings → Environments → New environment `production`:** add yourself
   as a required reviewer and limit deployment branches to `main`.
3. **GitHub → Settings → Secrets and variables → Actions → Variables** (not secrets,
   none of these are sensitive): `AZURE_CLIENT_ID` (bootstrap output `deployClientId`),
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
4. After the first deploy, make the `itask-api` package on GHCR **public** so Azure can
   pull it without credentials (it contains no secrets).

## Repository settings (owner checklist)

- Ruleset on `main`: require a pull request, require your approval, require the `ci`
  checks, block force pushes and deletion.
- Actions → General: workflow permissions **read-only**; require approval for **all
  outside collaborators**; allow only GitHub-owned and verified actions.
- Code security: enable secret scanning, **push protection**, Dependabot alerts and
  private vulnerability reporting.

## Cost guardrails

Free tiers everywhere (Static Web Apps Free, Container Apps consumption capped at 2
replicas, Azure SQL free offer set to pause instead of bill, Log Analytics with a daily
cap), a subscription budget with email alerts, rate limiting in the API, and per-user
AI caps. See `SECURITY.md` for the security model.
