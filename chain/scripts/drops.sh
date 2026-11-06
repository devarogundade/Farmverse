#!/usr/bin/env bash
# Creates the launch drops on the items contract, signed by the studio (minter) account.
# Prices, caps and limits are the decisions in Docs/architecture/15-game-market-economy-plan.md §9.
#
#   MASTER=farmverse.testnet chain/scripts/drops.sh
#
# Run upgrade-testnet.sh first. Safe to re-run: drops that exist are skipped (change them with
# update_drop instead; a cap can only go down).
set -euo pipefail
cd "$(dirname "$0")/.."

: "${MASTER:?set MASTER to the testnet account that owns the contracts, e.g. farmverse.testnet}"
NETWORK=${NETWORK:-testnet}
RPC=${RPC:-https://rpc.testnet.near.org}
SIGN_WITH=${SIGN_WITH:-sign-with-legacy-keychain}
ITEMS=items.$MASTER
STUDIO=studio.$MASTER
E18=000000000000000000   # FARM has 18 decimals

rpc() { curl -fsS "$RPC" -H 'content-type: application/json' -d "$1"; }
view() {
  local args; args=$(printf %s "$3" | base64 -w0)
  rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"call_function\",\"finality\":\"final\",\"account_id\":\"$1\",\"method_name\":\"$2\",\"args_base64\":\"$args\"}}" |
    python3 -c 'import json,sys; r=json.load(sys.stdin).get("result"); print(bytes(r["result"]).decode()) if r and "result" in r else None'
}

[[ -n $(view "$ITEMS" get_drops '{}') ]] || { echo "$ITEMS has no drops yet: run upgrade-testnet.sh first" >&2; exit 1; }

# id kind item_id quantity props title media price(FARM) cap(0=open) per_wallet(0=none)
drop() {
  local id=$1 kind=$2 item=$3 qty=$4 props=$5 title=$6 media=$7 price=$8 cap=$9 per=${10}
  if [[ $(view "$ITEMS" get_drop "{\"id\":\"$id\"}") != null ]]; then echo "• $id exists"; return; fi
  local args
  args=$(python3 - "$id" "$kind" "$item" "$qty" "$props" "$title" "$media" "$price$E18" "$cap" "$per" <<'PY'
import json, sys
i, kind, item, qty, props, title, media, price, cap, per = sys.argv[1:]
print(json.dumps({"drop": {
    "id": i, "kind": kind, "item_id": item, "quantity": int(qty), "props": props, "title": title,
    "media": media, "price": price, "cap": int(cap) or None, "per_wallet": int(per) or None,
    "starts_at_ms": 0, "ends_at_ms": None,
}}))
PY
)
  echo "• creating $id"
  near contract call-function as-transaction "$ITEMS" create_drop json-args "$args" prepaid-gas '100.0 Tgas' attached-deposit '0.05 NEAR' \
    sign-as "$STUDIO" network-config "$NETWORK" "$SIGN_WITH" send
}

drop farm_pass_s1 pack farm_pass    1 '{"season":1}' 'Farm Pass · Founders'  items/farm_pass.png    250 2000 5
drop builder_pass pack builder_pass 1 '{}'           "Builder's Pass"        items/builder_pass.png 400 1000 3
drop starter_pack pack starter_pack 1 '{}'           'Starter Pack'          items/starter_pack.png  30    0 1
drop gems_100     pack gems_100     1 '{}'           '100 Gems'              items/gems_100.png      20    0 0
drop gems_500     pack gems_500     1 '{}'           '500 Gems'              items/gems_500.png      90    0 0
drop gems_2000    pack gems_2000    1 '{}'           '2,000 Gems'            items/gems_2000.png    320    0 0
drop land_deed    land deed         1 '{}'           'Genesis Land Deed'     items/deed.png         300 1000 2

echo
view "$ITEMS" get_drops '{}' | python3 -c 'import json,sys
for d in json.load(sys.stdin):
    print("  %-14s %5d FARM  sold %d/%s" % (d["id"], int(d["price"]) // 10**18, d["sold"], d["cap"] or "open"))'
