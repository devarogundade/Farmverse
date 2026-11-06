#!/usr/bin/env bash
# Market Town merchants (Docs/architecture/15-game-market-economy-plan.md §13.1): creates bea., ruth., tomas.
# and ivy.<MASTER>, each with NEAR for gas and storage and a FARM float from the treasury, then writes their keys
# to backend/server/.env as MERCHANTS (the server's merchant-tick job signs with them).
#
#   MASTER=farmverse.testnet chain/scripts/merchants.sh
#
# Run after upgrade-testnet.sh. Safe to re-run: accounts that exist are kept and funded only if short.
set -euo pipefail
cd "$(dirname "$0")/.."

: "${MASTER:?set MASTER to the testnet account that owns the contracts, e.g. farmverse.testnet}"
NETWORK=${NETWORK:-testnet}
RPC=${RPC:-https://rpc.testnet.near.org}
SIGN_WITH=${SIGN_WITH:-sign-with-legacy-keychain}
SAVE_TO=${SAVE_TO:-save-to-legacy-keychain}
MERCHANT_NEAR=${MERCHANT_NEAR:-1.5}      # gas, market storage for offers and listings, approvals
MERCHANT_FARM=${MERCHANT_FARM:-20000}    # whole FARM each merchant starts with
ENV_FILE=../backend/server/.env
CREDS=${CREDS:-$HOME/.near-credentials/$NETWORK}
FARM=farm.$MASTER
TREASURY=treasury.$MASTER

rpc() { curl -fsS "$RPC" -H 'content-type: application/json' -d "$1"; }
exists() { rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"view_account\",\"finality\":\"final\",\"account_id\":\"$1\"}}" | grep -q '"result"'; }
view() {
  local args; args=$(printf %s "$3" | base64 -w0)
  rpc "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"call_function\",\"finality\":\"final\",\"account_id\":\"$1\",\"method_name\":\"$2\",\"args_base64\":\"$args\"}}" |
    python3 -c 'import json,sys; r=json.load(sys.stdin).get("result"); print(bytes(r["result"]).decode()) if r and "result" in r else None'
}
call() { # contract method args deposit signer
  near contract call-function as-transaction "$1" "$2" json-args "$3" prepaid-gas '100.0 Tgas' attached-deposit "$4" \
    sign-as "$5" network-config "$NETWORK" "$SIGN_WITH" send
}

entries=()
for name in bea ruth tomas ivy; do
  acc=$name.$MASTER
  if ! exists "$acc"; then
    echo "• creating $acc ($MERCHANT_NEAR NEAR)"
    near account create-account fund-myself "$acc" "$MERCHANT_NEAR NEAR" autogenerate-new-keypair --signature-scheme ed25519 "$SAVE_TO" \
      sign-as "$MASTER" network-config "$NETWORK" "$SIGN_WITH" send
  fi
  if [[ $(view "$FARM" storage_balance_of "{\"account_id\":\"$acc\"}") == null ]]; then
    echo "• registering $acc with FARM"
    call "$FARM" storage_deposit "{\"account_id\":\"$acc\"}" '0.0125 NEAR' "$MASTER"
  fi
  has=$(view "$FARM" ft_balance_of "{\"account_id\":\"$acc\"}" | tr -d '"')
  if [[ ${has:-0} == 0 ]]; then
    echo "• $MERCHANT_FARM FARM from $TREASURY to $acc"
    call "$FARM" ft_transfer "{\"receiver_id\":\"$acc\",\"amount\":\"${MERCHANT_FARM}000000000000000000\",\"memo\":\"merchant float\"}" '1 yoctoNEAR' "$TREASURY"
  fi
  key_file=$(ls "$CREDS/$acc.json" "$CREDS/$acc"/*.json 2>/dev/null | head -n1 || true)
  [[ -n $key_file ]] || { echo "no key for $acc in $CREDS" >&2; exit 1; }
  key=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["private_key"])' "$key_file")
  entries+=("{\"name\":\"$name\",\"account\":\"$acc\",\"key\":\"$key\"}")
done

json="[$(IFS=,; echo "${entries[*]}")]"
touch "$ENV_FILE"
if grep -qE '^MERCHANTS=' "$ENV_FILE"; then
  python3 - "$ENV_FILE" "$json" <<'PY'
import sys
path, value = sys.argv[1], sys.argv[2]
lines = open(path).read().splitlines()
lines = [f"MERCHANTS={value}" if l.startswith('MERCHANTS=') else l for l in lines]
open(path, 'w').write('\n'.join(lines) + '\n')
PY
else
  printf 'MERCHANTS=%s\n' "$json" >>"$ENV_FILE"
fi
echo "• MERCHANTS written to backend/server/.env (copy it to /etc/farmverse/server.env on the server)"
