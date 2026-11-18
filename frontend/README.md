# iTask frontend

Vue 3 + Vite + TypeScript + Pinia + Tailwind CSS 4, organized by feature.

```
src/
  app/            App shell: App.vue, router, layouts
  features/<name> One folder per feature (auth, boards, cards, activity, integrations, ai)
                  with its own api.ts, store.ts, views/ and components/
  shared/         Code used by several features: api client, ui kit
  styles/         Direction C tokens and motion presets
```

Rules of thumb:

- A feature may import from `shared/` but not from another feature's internals.
- Server calls go through `shared/api/http.ts` (cookie auth, ProblemDetails errors).
- Board columns wrap with CSS grid; no horizontal scrolling.

## Commands

```sh
npm ci              # install
npm run dev         # http://localhost:5173, proxies /api to the API on :5080
npm run test:unit   # Vitest
npm run test:e2e    # Playwright (Chromium); first time: npx playwright install chromium
npm run lint
npm run build
```
