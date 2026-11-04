# Farmverse backend (Firebase)

`functions/` is the new Cloud Functions codebase (Docs/architecture/03). It is built
beside the legacy `functions/` folder at the repository root and replaces it in
Phase 3, when `firebase.json` switches over.

```bash
npm test --workspace backend/functions        # pipeline, callable, core
npm run build --workspace backend/functions   # typecheck + esbuild bundle → lib/index.js
```

Layout: `core/` (errors, clock, logger, ids, cursors, rate limit, callable pipeline),
`infra/` (store port, unit of work, Firestore + memory stores, paths, idempotency,
aggregate gateway), `actions/` (registry + the batch pipeline), `api/` (deployed
functions). Game verbs (`domain/`, `modules/`) arrive in Phases 2–3.

The bundle inlines `@farmverse/contracts` and zod, so the deployed package only
depends on `firebase-admin` and `firebase-functions`.
