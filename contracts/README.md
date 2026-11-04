# @farmverse/contracts

The single source of truth for everything that crosses the client/server
boundary: requests, responses, action kinds, error codes, ids and limits.
Written in TypeScript with zod; the C# side is **generated**.

```bash
npm run contracts:gen     # write Assets/Farmverse/Runtime/Contracts/Generated/*.g.cs
npm run contracts:check   # CI: fail if the generated C# is stale
npm test --workspace contracts
```

- `src/common` — primitives (coins, quantities, epoch ms, basis points), ids, errors, envelope, paging
- `src/actions` — one `defineAction` per game verb: request, result, role, exclusivity
- `src/callables.ts` — every callable function and its request/response
- `codegen/` — `z.toJSONSchema` → C# emitter (no reflection; explicit `ToWire`/`FromWire`)
- `fixtures/wire-samples.json` — samples both test suites parse and round-trip

Rules: register every object/enum with `named()`; additive changes only within a
contract version (Docs/architecture/05 §6).
