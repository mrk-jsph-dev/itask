# iTask

A board-and-card task manager: **ASP.NET Core 10** API, **Vue 3** frontend,
and **Azure** infrastructure in **Bicep**, designed to run on free tiers.

## About this revamp

This repository is the revamp of the legacy iTask app, a Laravel 5.2 project from
2020 (kept privately as `itask-legacy`). It keeps the same idea, boards with lists
and cards, labels, checklists, comments, attachments and an activity log, and
rebuilds it with a modern stack, proper authorization and cloud hosting. Compared
with the legacy app it adds:

- authorization on every board route, with Owner, Editor and Viewer roles per board
- attachments scoped to their card and stored in Blob Storage instead of local disk
- persisted ordering of lists and cards
- automated tests, CI/CD and infrastructure as code

## Versions

The project tracks **Long Term Support** releases only:

| Component | Version | Notes |
|---|---|---|
| .NET | 10 (LTS) | `backend/global.json` pins the 10.0 SDK band; Dependabot ignores major .NET bumps (STS releases such as .NET 11). The next move is to the next LTS. |
| Node.js | 22 (LTS) | Used by the frontend toolchain and CI. |

```
backend/    ASP.NET Core minimal API, vertical slices (see backend/README.md)
frontend/   Vue 3 + Vite + TypeScript, feature-first (see frontend/README.md)
infra/      Bicep: bootstrap/ (one-time, run by the owner) and main.bicep (deployed by CI)
.github/    ci-reusable.yml, ci.yml (pull requests), ci-cd.yml (main + deploy)
```

## Versioning

Releases follow [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.

| Part | Bump when | Example |
|---|---|---|
| **MAJOR** (1st digit) | a change breaks compatibility: API contract, data model or behavior existing users rely on | `1.4.2` → `2.0.0` |
| **MINOR** (2nd digit) | a new feature is added in a backward-compatible way | `1.4.2` → `1.5.0` |
| **PATCH** (3rd digit) | a backward-compatible bug or security fix | `1.4.2` → `1.4.3` |

- Versions below `1.0.0` are pre-release; `1.0.0` is the first production release (the MVP).
- Releases are git tags `vMAJOR.MINOR.PATCH` (e.g. `v1.2.0`) with release notes.
- The API (`backend/Directory.Build.props`, `<Version>`) and the frontend
  (`frontend/package.json`, `version`) carry the same version.

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
