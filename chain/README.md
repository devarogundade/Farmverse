# Farmverse on NEAR — smart contracts

Three Rust contracts (near-sdk 5.30, Rust ≥ 1.95). The design is in
[Docs/architecture/13-near-web3-plan.md](../Docs/architecture/13-near-web3-plan.md) §6.

| Contract | Crate | Mainnet account (proposed) | Standards |
|---|---|---|---|
| FARM token | `contracts/farm_token` | `farm.farmverse.near` | NEP-141, NEP-145, NEP-148 |
| Items (goods, buildings, land, tools, packs, workers) | `contracts/items` | `items.farmverse.near` | NEP-171, 177, 178, 181, 199, 297 |
| Marketplace | `contracts/market` | `market.farmverse.near` | NEP-178 receiver, NEP-141 receiver, 297 |

## Build and test

Linux, macOS or WSL (the NEAR sandbox has no Windows build):

```bash
cargo test                       # unit tests of all three contracts
scripts/build.sh                 # wasm → res/*.wasm (needs cargo-near)
cargo test -p farmverse-chain-tests   # sandbox: deploys res/*.wasm and runs the real flows
```

For a mainnet deploy, build verifiably with `cargo near build reproducible-wasm` (Docker), so
anyone can check the deployed code matches this source.

## FARM

The full supply is minted once, to the treasury, in `new`. There is no mint function, no owner,
no pause: nothing about the token can change after deploy.

```bash
near contract call-function as-transaction farm.farmverse.near new json-args \
  '{"treasury_id":"treasury.farmverse.near","total_supply":"<supply × 10^18>",
    "metadata":{"spec":"ft-1.0.0","name":"Farmverse","symbol":"FARM","decimals":18,"icon":"data:image/svg+xml,…"}}' …
```

## Items

One item type per token: `kind` (`good | boost | building | land | tool | pack | worker`),
`item_id` (catalogue id), `quantity`, `props` (JSON object, ≤ 1 KiB), `minted_from`,
`minted_at_ms`. Stored in contract state (`get_item`) and mirrored into NEP-177 `extra`.

| Method | Caller | Notes |
|---|---|---|
| `mint_with_voucher(voucher, signer, signature)` | the player | payable (storage, ~0.01 NEAR; excess refunded) |
| `studio_mint(token_id, receiver_id, kind, item_id, quantity, props, title, media)` | `minter_id` | ids start `s-` |
| `redeem(token_id)` | token owner | 1 yocto; burns, refunds storage, emits `item_redeem`; workers can't be redeemed |
| `nft_payout`, `nft_transfer_payout` | anyone / approved market | NEP-199; royalties capped at 10 % in code |
| `add_signer`, `remove_signer`, `set_minter`, `set_royalties`, `set_paused`, `set_base_uri`, `propose_owner` / `accept_owner` | owner (multisig) | 1 yocto (full-access key only) |
| `get_item`, `is_burned`, `get_config`, `voucher_message` | views | |

`paused` stops minting and redeeming only; transfers and sales always work, so nobody's tokens
can be frozen.

### The mint voucher — what the game server signs

```
message   = "farmverse:items:mint:v1"  (23 ASCII bytes, no terminator)
          ‖ borsh(MintVoucher)
signature = ed25519(message) with a key registered via add_signer
```

`MintVoucher`, borsh field order (**append only**):

| # | Field | Borsh |
|---|---|---|
| 1 | `contract_id` | string (u32 LE length + UTF-8) — this contract's account id |
| 2 | `token_id` | string — `g-` + unique id (e.g. a ULID); never reused |
| 3 | `receiver_id` | string — the player's wallet; only it can submit |
| 4 | `kind` | u8 — `good 0, boost 1, building 2, land 3, tool 4, pack 5, worker 6` (vouchers may not use 5 or 6) |
| 5 | `item_id` | string — `[a-z][a-z0-9_]{0,39}` |
| 6 | `quantity` | u64 LE — exactly 1 for building/land |
| 7 | `props` | string — JSON object |
| 8 | `title` | string — 1–64 chars |
| 9 | `media` | string — path under `base_uri`, e.g. `items/wheat.png` |
| 10 | `expires_at_ms` | u64 LE — ≤ 7 days ahead |

To check an encoder, call the view `voucher_message(voucher)`: it returns the exact bytes
(base64) the contract verifies against.

Rules the contract enforces: right contract, right receiver, not expired, TTL ≤ 7 days, known
signer, valid signature, `token_id` never minted **or burned** before. So after `expires_at_ms`
the server may refund the goods if `nft_token(token_id)` is `null`: that voucher can no longer
mint anything.

### Events (NEP-297, `standard: "farmverse_items"`)

`item_mint {token_id, owner_id, source: voucher|studio, item}`, `item_redeem {token_id, owner_id, item}`,
`config_change {field, value}` — alongside the standard `nep171` `nft_mint`, `nft_transfer`, `nft_burn`.

## Marketplace

```
list : owner → items.nft_approve(token_id, "market…", msg: {"price":"<FARM units>"})
       (first: market.storage_deposit, 0.01 NEAR per active listing)
buy  : buyer → farm.ft_transfer_call("market…", price, msg: {"nft_contract_id":"items…","token_id":"…"})
       300 TGas, 1 yocto
```

On buy, the listing leaves the book at once, the market calls `nft_transfer_payout` with
`price − fee`, then pays the fee to `treasury_id` and the NEP-199 payout (royalties + seller).

* NFT didn't move (sold elsewhere, redeemed, approval revoked) → buyer refunded in full.
* A payee not registered with FARM → the amount is kept as `owed`; they register and call
  `withdraw_owed`. Nothing is lost.
* `delist` (seller), `update_price` (seller), `remove_stale` (anyone; checks the NFT contract).
* Owner (multisig): `set_fee_bps` (≤ 1000 in code), `set_treasury`, `add_nft_contract` /
  `remove_nft_contract`, `set_paused` (stops listing and buying; delist and withdrawals still work).

Events (`standard: "farmverse_market"`): `market_list`, `market_delist {reason}`, `market_sale
{seller_id, buyer_id, price, fee, payout_valid}`, `market_sale_failed`, `market_owed`, `config_change`.

## Deploy order (testnet first)

**Testnet, scripted.** `chain/scripts/deploy-testnet.sh` follows the steps below. It runs in WSL/Linux
with near CLI and needs MASTER's key in the keychain and about 13 testnet NEAR on MASTER:

```bash
MASTER=farmverse.testnet chain/scripts/deploy-testnet.sh
```

It creates the five sub-accounts, then deploys and initialises FARM, items and market. Unless
OWNER is set, MASTER is the owner. If `backend/server/.env` has no `MINT_SIGNER_SEED`, the script
generates one there and registers its public key on items. It then registers the market with
FARM and writes `chain/deploy/testnet.env`: the account names, the block to start indexing from
and the signer key. Re-running it skips whatever already exists.

To give a tester FARM from the treasury: `MASTER=… chain/scripts/fund-tester.sh friend.testnet 500`.

1. Create `farm.`, `items.`, `market.`, `treasury.`, `studio.` sub-accounts of the team account,
   and the owner multisig (Sputnik DAO / Trezu).
2. Deploy and `new` FARM → items (owner = multisig, minter = studio, signers = the game server's
   ed25519 public key, royalties = `{treasury: 250}`) → market (owner = multisig, ft = FARM,
   treasury, fee 500, nft_contracts = [items]).
3. Register the market and the treasury with FARM (`storage_deposit`).
4. Remove the deployer's full-access keys from the three contract accounts (code then changes
   only if the multisig adds a key back).

Mainnet deploys are signed by the team, never by CI.

## Before mainnet

* External audit of all three contracts.
* `cargo near build reproducible-wasm` and publish the code hashes.
* The game server's signer key lives in a KMS/HSM; rotate with `add_signer` → `remove_signer`.
