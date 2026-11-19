# Changelog

All notable changes follow [Semantic Versioning](https://semver.org). Tags are `vX.Y.Z`.

## 1.3.2

- **Lower AI caps for a near-$0 bill:** 10 AI requests per user and 30 for the whole app per day
  (were 20 and 200). This covers ✨ buttons, automation drafting and ✨ automation actions. Raise
  `Ai__DailyRequestsPerUser` / `Ai__DailyRequestsTotal` when there is a budget.

## 1.3.1

- **Automation counts:** the Automations sheet reloads when it opens, so run counts are current, and
  block-mode doorways show how many moves they blocked ("0 runs, 2 blocked in 30 days").
- **Card create:** simultaneous creates on one board retry with a short random pause.

## 1.3.0

Board automations: iTask's own take, with list doorways, explain and undo, and AI only where you ask for it.
Rules run inside the change that sets them off, so they cost nothing to run.

- **List doorways:** requirements for cards entering or leaving a list (an assignee, a label, a
  due date, a finished checklist, a priority). A doorway can *block* the move and say what is
  missing (owners can move anyway, which is recorded), *fill in* what's missing, or *warn* on the
  column. Shown as 🚪 on the column header.
- **Rules:** "when … if … then …" with up to three actions (move, complete, reopen, labels,
  assign, priority, due date, checklist items, comment, notify, archive). Instant triggers run
  in the same request; date triggers (due soon, overdue, stuck in a list, completed a while ago)
  run in the hourly job and can run late while the app sleeps. About 12 presets fit to the board.
- **Explain and undo:** every automated change shows as "(⚡ rule)" in the activity feeds and
  comes with an Undo toast. Changes made by automations never set off other automations.
- **Show me once:** record the steps on one card and get a rule draft.
- **Replay before adding:** see how often a rule would have run in the last 30 days.
- **Receipt:** a 7-day summary in the Automations sheet and a line in the weekly email digest.
- **✨ AI (optional):** "Describe it" and "Review my board" draft automations from plain words or
  from the board's activity counts (no card text is sent). Rules can include one opt-in ✨ action
  (break down into a checklist, pick labels and priority, or summarize) on instant triggers only,
  charged to the rule creator's daily AI quota and skipped with a note when it is spent.

## 1.2.1

Abuse protection before the first public deploy. Nothing here costs money to run.

- **Email code at sign-up:** a new account gets a 6-digit code by email and can't sign in
  until it's entered (10 minutes, 5 tries, new code after 60 s, at most 5 a day).
  Unconfirmed accounts are deleted after 24 hours. Production now requires a confirmed email.
- **Bot check fails closed:** in production, sign-up, sign-in, password reset and new codes
  are refused while the Turnstile secret is missing, instead of skipping the check.
- **Daily caps for the whole app:** 50 sign-ups, 200 AI requests (on top of 20 per user),
  400 emails, and 10 emails per address so no inbox can be flooded through iTask.
- **Storage and data limits:** 100 MB of files per user, 2 GB for the app, 50 upload links per
  user a day; at most 50 owned boards per user and 2,000 cards per board.

## 1.2.0

Installable app and AI helpers.

- **Installable app (PWA):** install from the browser, offline read-only boards from the
  last visit, and an update prompt when a new version is out.
- **AI helpers (OpenAI in Microsoft Foundry):** ✨ Quick add (a sentence becomes a task
  draft with due date, priority, labels and list), Break down (checklist steps), Summarize
  (a card's description, comments and activity in one paragraph, can be posted as a
  comment), Daily digest per board (due, stuck, done) and "From a goal" in New board
  (lists and starter cards). Every result is an editable draft with Accept or Discard;
  nothing is saved without the user. Editors and owners only, not for demo accounts,
  20 requests per user per day. Off until `AI_ENDPOINT` and `AI_DEPLOYMENT` are set.

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
