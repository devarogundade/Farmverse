# Farmverse

A cosy multiplayer farming and trading game: Unity 6 client, Firebase backend.

| Path | What |
|---|---|
| `Assets/Farmverse` | the game client (see its README) |
| `contracts/` | the single source of the client/server contract (zod → generated C#), the economy (`economy/economy.json`) and conformance vectors |
| `backend/` | Cloud Functions game server (`functions/`), security rules and their tests, indexes, Remote Config template, `firebase.json` |
| `tools/` | `codegen` (C# command wrappers, string-table and Remote Config checks), `dotnet` (headless C# tests, Unity compile check), `art` (UI art generator), `ci` |
| `Docs/architecture` | the design: 00 master plan … 12 IAP |
| `ArtSource/` | Blender sources |

```bash
npm ci && npm test                                   # contracts + backend
npm run test:emulator --workspace backend/rules-tests # security rules (needs Java)
dotnet build tools/dotnet/Farmverse.TestHost -c Release && dotnet tools/dotnet/Farmverse.TestHost/bin/Release/net8.0/Farmverse.TestHost.dll
```

In Unity: **Farmverse ▸ Set Up Boot Scene**, then Play.
