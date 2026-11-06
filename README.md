# Farmverse

A cosy multiplayer farming and trading game: Unity 6 WebGL client, a Node game server and NEAR
contracts for items and the marketplace.

| Path | What |
|---|---|
| `Assets/Farmverse` | the game client (see its README) |
| `contracts/` | the single source of the client/server contract (zod → generated C#), the economy and conformance vectors |
| `backend/game` | the game rules, shared by the server and its tests |
| `backend/server` | the game server: API, WebSocket, jobs, chain indexer |
| `chain/` | the NEAR contracts (items, market, farm token) and their deploy scripts |
| `web/` | the marketplace, admin and landing sites |
| `deployment/` | the EC2 setup and deploy script |
| `tools/` | `codegen` (C# command wrappers, string-table and secrets checks), `dotnet` (headless C# tests, island engine), `art` |
| `Docs/architecture` | the design (13 covers the web and NEAR version) |

```bash
npm ci && npm test                                   # contracts, game rules, server
dotnet build tools/dotnet/Farmverse.TestHost -c Release && dotnet tools/dotnet/Farmverse.TestHost/bin/Release/net8.0/Farmverse.TestHost.dll
```

In Unity: **Farmverse ▸ Set Up Boot Scene**, then Play.

Store packs (ithappy and others) and the content built from them are not in git: see
[Assets/Farmverse/README.md](Assets/Farmverse/README.md) for what to import and which editor menus rebuild it.
