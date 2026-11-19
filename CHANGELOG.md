# Changelog

All notable changes follow [Semantic Versioning](https://semver.org). Tags are `vX.Y.Z`.

## 1.1.0

Views, notifications, recurring cards, templates, live updates and external sign-in.

- **Views:** List and Calendar (week/month) views next to Board; filter, sort and group
  kept in the URL (`GET /api/boards/{id}/cards`).
- **Notifications:** bell with unread count, `/notifications`, due reminders and an
  optional daily/weekly email digest (SMTP when configured, otherwise logged).
- **Recurring cards and templates:** repeat rules that create the next copy on complete
  or on schedule; five built-in board templates and "Save as template".
- **Live updates:** board changes and presence over SignalR on board pages.
- **Sign in with GitHub or Microsoft:** "Continue with" buttons on sign-in and register,
  Connected accounts on the profile (connect, disconnect, set a password). Providers are
  off until their client id and secret are configured.
- **Sessions survive restarts:** data-protection keys are stored in the database, so a
  scale-to-zero restart no longer signs everyone out.

## 1.0.1

Design alignment with Direction C.

- **Shell:** left sidebar (Home, Search, Activity, Admin, boards with stars, board
  members, Profile, Sign out) around one white panel; icon rail on tablets and a menu
  sheet on phones.
- **Screens:** split sign-in page, Home with a due summary and ⏰ Due soon list, board
  header with "+ New task", redesigned columns and card tiles, card sheet with a field
  grid, Activity and profile page with Boards/Tasks/Done counts.
- **Cards:** optional emoji before the title; tiles show file counts.
- **API:** `GET /api/me/stats`, `GET /api/me/due-soon`; card `emoji` (PATCH) and
  `attachmentCount`.
- **Motion:** skeletons appear after 150 ms, route progress bar, button spinners, logo
  splash on first load, spring toasts.

## 1.0.0 (MVP)

First public release.

- **Accounts:** register, sign in, profile, password change, Cloudflare Turnstile,
  demo accounts for each role (reset nightly).
- **Boards:** create, settings, stars, archive and delete, members with Owner, Editor
  and Viewer roles, invites, platform admin user list.
- **Lists and cards:** wrapping columns, drag and drop with saved order, card keys,
  labels, assignees, priority, due dates, cover colours.
- **Card detail:** checklist, Markdown comments with mentions, attachments in Blob
  Storage through short-lived links.
- **Activity:** board, card and personal timelines, kept for 180 days.
- **Search:** global search with a Ctrl/Cmd+K command palette.
- **Operations:** Azure deploy through OIDC with owner approval, readiness check,
  daily maintenance jobs, cleanup of unconfirmed uploads, and a budget kill switch
  that stops the API at $5.
