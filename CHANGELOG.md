# Changelog

All notable changes follow [Semantic Versioning](https://semver.org). Tags are `vX.Y.Z`.

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
