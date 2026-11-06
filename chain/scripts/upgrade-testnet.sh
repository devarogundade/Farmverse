#!/usr/bin/env bash
# Upgrades the testnet items and market contracts in place to v2 (drops, offers, rentals) and sets up
# what v2 needs. Every token, listing and setting stays: the new code is deployed and `migrate` runs in
# the same transaction.
#
#   MASTER=farmverse.testnet chain/scripts/upgrade-testnet.sh
#
# Needs what deploy-testnet.sh needs (near CLI with MASTER's and the contract accounts' keys in the
# file keychain, chain/res/*.wasm from scripts/build.sh), and the contracts deployed by it.
#
# Then:
#   1. items: deploy + migrate, set_drop_payment(FARM, treasury), register with FARM, NEAR float for drop storage
#   2. market: deploy + migrate
#   3. faucet.<MASTER>: created, registered with FARM, funded with NEAR and FARM; its key goes into
#      backend/server/.env as FAUCET_ACCOUNT / FAUCET_PRIVATE_KEY (the server pays faucet claims from it)
#
# Safe to re-run: a contract already on v2 is not deployed again, and every step checks first.
# Testnet only. Mainnet upgrades are signed by the owner multisig.
set -euo pipefail
cd "$(dirname "$0")/.."

: "${MASTER:?set MASTER to the testnet account that owns the contracts, e.g. farmverse.testnet}"
NETWORK=testnet
RPC=${RPC:-https://rpc.testnet.near.org}
SIGN_WITH=${SIGN_WITH:-sign-with-legacy-keychain}
SAVE_TO=${SAVE_TO:-save-to-legacy-keychain}
OWNER=${OWNER:-$MASTER}
ITEMS_FLOAT_NEAR=${ITEMS_FLOAT_NEAR:-3}      # drop storage: ≈ 0.01 NEAR per edition above a 0.5 NEAR reserve
FAUCET_NEAR=${FAUCET_NEAR:-10}               # NEAR the faucet account starts with
FAUCET_FARM=${FAUCET_FARM:-1000000}          # whole FARM moved from the treasury to the faucet
ENV_FILE=../backend/server/.env
CREDS=${CREDS:-$HOME/.near-credentials/$NETWORK}

FARM=farm.$MASTER
ITEMS=items.$MASTER
MARKET=market.$MASTER
TREASURY=treasury.$MASTER
STUDIO=studio.$MASTER
FAUCET=faucet.$MASTER

for f in res/farmverse_items.wasm res/farmverse_market.wasm; do
  [[ -f $f ]] || { echo "missing $f: run scripts/build.sh first" >&2; exit 1; }
done
command -v near >/dev/null || { echo "near CLI not found" >&2; exit 1; }

rpc() { curl -fsS "$RPC" -H 'content-type: application/json' -d "$1"; }
exists() { rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"view_account\",\"finality\":\"final\",\"account_id\":\"$1\"}}" | grep -q '"result"'; }
view() { # contract method args → JSON result, or nothing when the call fails (e.g. no such method)
  local args; args=$(printf %s "$3" | base64 -w0)
  rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"call_function\",\"finality\":\"final\",\"account_id\":\"$1\",\"method_name\":\"$2\",\"args_base64\":\"$args\"}}" |
    python3 -c 'import json,sys; r=json.load(sys.stdin).get("result"); print(bytes(r["result"]).decode()) if r and "result" in r else None'
}
call() { # contract method args deposit signer [gas]
  near contract call-function as-transaction "$1" "$2" json-args "$3" prepaid-gas "${6:-100.0 Tgas}" attached-deposit "$4" \
    sign-as "$5" network-config "$NETWORK" "$SIGN_WITH" send
}
upgrade() { # account wasm probe-method
  if [[ -n $(view "$1" "$3" '{}') ]]; then echo "• $1 is already on v2"; return; fi
  echo "• upgrading $1 (deploy + migrate)"
  near contract deploy "$1" use-file "$2" with-init-call migrate json-args '{}' prepaid-gas '100.0 Tgas' attached-deposit '0 NEAR' \
    network-config "$NETWORK" "$SIGN_WITH" send
  [[ -n $(view "$1" "$3" '{}') ]] || { echo "$1 didn't answer $3 after the upgrade" >&2; exit 1; }
}
registered() { [[ $(view "$FARM" storage_balance_of "{\"account_id\":\"$1\"}") != null ]]; }
register() { registered "$1" || { echo "• registering $1 with FARM"; call "$FARM" storage_deposit "{\"account_id\":\"$1\"}" '0.0125 NEAR' "$MASTER"; }; }
near_balance() { # whole NEAR, rounded down
  rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"view_account\",\"finality\":\"final\",\"account_id\":\"$1\"}}" |
    python3 -c 'import json,sys; print(int(json.load(sys.stdin)["result"]["amount"]) // 10**24)'
}
send_near() { # to NEAR
  echo "• sending $2 NEAR to $1"
  near tokens "$MASTER" send-near "$1" "$2 NEAR" network-config "$NETWORK" "$SIGN_WITH" send
}

for a in "$MASTER" "$FARM" "$ITEMS" "$MARKET" "$TREASURY" "$STUDIO"; do
  exists "$a" || { echo "$a doesn't exist: run deploy-testnet.sh first" >&2; exit 1; }
done

# ---------------------------------------------------------------- items
upgrade "$ITEMS" res/farmverse_items.wasm get_drops
CFG=$(view "$ITEMS" get_config '{}')
if [[ $(printf %s "$CFG" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("farm_id") or "")') != "$FARM" ]]; then
  echo "• items: drops are paid in $FARM, takings to $TREASURY"
  call "$ITEMS" set_drop_payment "{\"farm_id\":\"$FARM\",\"treasury_id\":\"$TREASURY\"}" '1 yoctoNEAR' "$OWNER"
fi
register "$ITEMS"
if (( $(near_balance "$ITEMS") < ITEMS_FLOAT_NEAR + 4 )); then send_near "$ITEMS" "$ITEMS_FLOAT_NEAR"; fi

# ---------------------------------------------------------------- market
upgrade "$MARKET" res/farmverse_market.wasm get_offers_count

# ---------------------------------------------------------------- faucet
if ! exists "$FAUCET"; then
  echo "• creating $FAUCET ($FAUCET_NEAR NEAR)"
  near account create-account fund-myself "$FAUCET" "$FAUCET_NEAR NEAR" autogenerate-new-keypair --signature-scheme ed25519 "$SAVE_TO" \
    sign-as "$MASTER" network-config "$NETWORK" "$SIGN_WITH" send
fi
register "$FAUCET"
FAUCET_HAS=$(view "$FARM" ft_balance_of "{\"account_id\":\"$FAUCET\"}" | tr -d '"')
if [[ ${FAUCET_HAS:-0} == 0 ]]; then
  echo "• moving $FAUCET_FARM FARM from $TREASURY to $FAUCET"
  call "$FARM" ft_transfer "{\"receiver_id\":\"$FAUCET\",\"amount\":\"${FAUCET_FARM}000000000000000000\",\"memo\":\"testnet faucet\"}" '1 yoctoNEAR' "$TREASURY"
fi

# The server signs faucet transfers with the faucet account's key (never the treasury's).
KEY_FILE=$(ls "$CREDS/$FAUCET.json" "$CREDS/$FAUCET"/*.json 2>/dev/null | head -n1 || true)
[[ -n $KEY_FILE ]] || { echo "no key for $FAUCET in $CREDS: import it, then re-run" >&2; exit 1; }
FAUCET_KEY=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["private_key"])' "$KEY_FILE")
touch "$ENV_FILE"
for kv in "FAUCET_ACCOUNT=$FAUCET" "FAUCET_PRIVATE_KEY=$FAUCET_KEY"; do
  k=${kv%%=*}
  if grep -qE "^$k=" "$ENV_FILE"; then sed -i -E "s|^$k=.*$|$kv|" "$ENV_FILE"; else printf '%s\n' "$kv" >>"$ENV_FILE"; fi
done
echo "• FAUCET_ACCOUNT and FAUCET_PRIVATE_KEY written to backend/server/.env"

if ! grep -q '^NEAR_FAUCET=' deploy/testnet.env 2>/dev/null; then
  printf 'NEAR_FAUCET=%s\n' "$FAUCET" >>deploy/testnet.env
fi

echo
echo "items:  $(view "$ITEMS" get_config '{}')"
echo "market: $(view "$MARKET" get_config '{}')"
echo "faucet: $(near_balance "$FAUCET") NEAR, $(view "$FARM" ft_balance_of "{\"account_id\":\"$FAUCET\"}") FARM units"
echo
echo "Next: MASTER=$MASTER chain/scripts/drops.sh, then node chain/scripts/farmhands.mjs"
