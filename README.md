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

iTask has two separate layers of roles. App roles decide who manages accounts; board roles
decide what someone can do on one board. Neither grants the other.

### App level: User and Platform admin

Everyone who registers is a **User**. The first **Platform admin** comes from the
`PLATFORM_ADMIN_EMAILS` secret (see setup below); after that, admins promote others on
**Admin › Users**.

| Action | User | Platform admin |
|---|:-:|:-:|
| Create boards, join boards by invite, use their own boards | ✓ | ✓ |
| Edit their own profile and password | ✓ | ✓ |
| Open **Admin › Users**: list every account in iTask (name, email, join date, user type, demo and locked status) | | ✓ |
| Lock or unlock an account (a locked user is signed out within a minute and can't sign in) | | ✓ |
| Change an account's user type (User or Platform admin) | | ✓ |
| Open a board they aren't a member of | | |

Admin limits: admins can't lock themselves or change their own user type, so at least one
admin always remains. Demo accounts keep their seeded type, and the demo admin can't change
anyone's type. A new user type reaches that person's session within a minute, without
signing them out.

### Board level: Owner, Editor and Viewer

Whoever creates a board is its **Owner**; Owners invite others as Owner, Editor or Viewer.

| Action on a board | Viewer | Editor | Owner |
|---|:-:|:-:|:-:|
| See the board, lists, cards, files and activity | ✓ | ✓ | ✓ |
| Star the board, see its members | ✓ | ✓ | ✓ |
| Comment; edit or delete their **own** comments | ✓ | ✓ | ✓ |
| Create, edit, move, complete and archive cards and lists | | ✓ | ✓ |
| Subtasks, labels, assignees and dates | | ✓ | ✓ |
| Upload files; delete their **own** files | | ✓ | ✓ |
| Delete **anyone's** comments or files | | | ✓ |
| Invite people, change roles, remove members | | | ✓ |
| Board settings, archive, restore and delete | | | ✓ |

Board rules: any member can leave a board, but the last Owner can't leave or be removed.
Nothing can change on an archived board until an Owner restores it. People who aren't
members get "not found" rather than "forbidden", so a private board's existence stays hidden.

### Demo accounts

Demo environment only: one account per role (Platform admin, Owner, Editor, Viewer) on a
shared sample board, reset nightly. Demo accounts can't change passwords, upload files or
use admin actions that change other accounts.

## CI/CD

| Workflow | Runs on | Does |
|---|---|---|
| `ci.yml` | pull requests to `main` | calls `ci-reusable.yml`: build, test, lint, Bicep lint, CodeQL |
| `ci-cd.yml` | manual (Run workflow) | calls `ci-reusable.yml`, then **deploy** after the owner approves |

The deploy job signs in to Azure with **OIDC** (no stored cloud secret), pushes the
API image to GHCR, deploys `infra/main.bicep`, and publishes the frontend to Static Web Apps.

## First-time Azure setup (owner only)

1. **Bootstrap** (creates `rg-itask`, identities, the GitHub OIDC trust, role
   assignments, a $5 budget and the budget kill switch). Re-run it after changes to
   `infra/bootstrap/`:
   ```sh
   az login
   export ITASK_ALERT_EMAIL=<your email>
   az deployment sub create --location southeastasia \
     --template-file infra/bootstrap/bootstrap.bicep \
     --parameters infra/bootstrap/bootstrap.bicepparam
   ```
2. **GitHub → Settings → Environments → New environment `production`:** add yourself
   as a required reviewer and limit deployment branches to `main`. Add these
   **environment secrets** (stored as Container Apps secrets on each deploy; all optional):
   - `DEMO_PASSWORD`: password of the demo accounts (empty = demo sign-in off)
   - `TURNSTILE_SECRET_KEY`: Cloudflare Turnstile secret key (empty = check skipped)
   - `PLATFORM_ADMIN_EMAILS`: emails (`;` separated) made Platform admin at startup and
     when they register. Kept as a secret so the addresses stay out of public run logs.
     Removing an email later does not demote the account; use Admin › Users for that.
   - `SMTP_USERNAME` / `SMTP_PASSWORD`: a Gmail address and its
     [app password](https://myaccount.google.com/apppasswords) (needs 2-Step Verification)
     used to send invites, password resets, email confirmation and digests. Use a dedicated
     account: recipients see the address. Gmail allows about 500 emails a day. Empty =
     emails are only logged and nothing is sent.
3. **GitHub → Settings → Secrets and variables → Actions → Variables** (not secrets,
   none of these are sensitive): `AZURE_CLIENT_ID` (bootstrap output `deployClientId`),
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, and optionally `TURNSTILE_SITE_KEY` and
   `SQL_ADMIN_GROUP_ID` (see below).
4. **Actions → ci-cd → Run workflow** on `main`, approve the deploy. After the first
   run, make the `itask-api` package on GHCR **public** so Azure can pull it without
   credentials (it contains no secrets), then run the workflow again.

### Operations

- **Health:** liveness `/api/health`, readiness `/api/health/ready` (checks the database).
- **Maintenance:** the API runs daily jobs (03:00 UTC slot): demo reset, activity older
  than 180 days, notifications past retention, uploads that were never confirmed. Hourly
  jobs send due-soon/overdue reminders and the email digests (08:00 in each user's time
  zone). With the API scaled to zero, a missed slot runs on the next start, so reminders
  and digests can arrive late. Jobs take a database lock, so only one replica runs each.
- **Budget kill switch:** when the $5 budget is spent, the action group runs the
  `logic-itask-killswitch` Logic App, which stops the API. Start it again with
  `az containerapp start -g rg-itask -n ca-itask-api`.
- **Manual SQL queries:** the SQL admin is the API identity. To query yourself, create
  an Entra group (for example `itask-sql-admins`), add yourself and `id-itask-api`,
  set its object id as the `SQL_ADMIN_GROUP_ID` variable and redeploy. Then connect
  with Entra authentication (Azure Data Studio, `sqlcmd -G`) after allowing your IP
  in the SQL server firewall.
- **Custom domain (optional):** without one, the site and API are different sites, so
  the auth cookie is `SameSite=None; Partitioned` and the API rejects writes from
  foreign origins. Browsers that block all third-party cookies (Safari by default)
  can't sign in until the site and API share a domain (`app.` and `api.` of the same
  domain); then set `crossSiteCookies = false` in `infra/main.bicepparam`.

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
