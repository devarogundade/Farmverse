# Farmverse backend

| Folder | What |
|---|---|
| `game/` | The game rules (`core/`, `game/`, `infra/`, `domain/`): plain TypeScript over a `Store`, no database or framework. Tested in memory. |
| `server/` | The game server that runs them: Fastify (HTTP + WebSocket), Postgres through Prisma, Redis, BullMQ jobs and the NEAR chain indexer. See its README. |

```bash
npm test --workspace backend/game      # game rules, in memory
npm test --workspace backend/server    # server unit tests
npm run test:db --workspace backend/server   # against Postgres + Redis
```

Deployment: [deployment/README.md](../deployment/README.md).
