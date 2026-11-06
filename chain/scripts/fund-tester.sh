#!/usr/bin/env bash
# Gives a testnet account FARM from the treasury, registering it with FARM first if needed.
#
#   MASTER=farmverse.testnet chain/scripts/fund-tester.sh friend.testnet 500
#
# MASTER pays the registration (0.0125 NEAR); treasury.MASTER signs the transfer.
set -euo pipefail
: "${MASTER:?set MASTER, e.g. farmverse.testnet}"
TO=${1:?account to fund}
AMOUNT=${2:?whole FARM to send}
[[ $AMOUNT =~ ^[0-9]+$ ]] || { echo "amount must be whole FARM" >&2; exit 1; }
FARM=farm.$MASTER
TREASURY=treasury.$MASTER
RPC=${RPC:-https://rpc.testnet.near.org}
# Where near CLI keeps keys. The file keychain (~/.near-credentials) works everywhere, WSL included;
# set SIGN_WITH=sign-with-keychain SAVE_TO=save-to-keychain to use the OS keychain instead.
SIGN_WITH=${SIGN_WITH:-sign-with-legacy-keychain}
SAVE_TO=${SAVE_TO:-save-to-legacy-keychain}

args=$(printf '{"account_id":"%s"}' "$TO" | base64 -w0)
registered=$(curl -fsS "$RPC" -H 'content-type: application/json' \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"query\",\"params\":{\"request_type\":\"call_function\",\"finality\":\"final\",\"account_id\":\"$FARM\",\"method_name\":\"storage_balance_of\",\"args_base64\":\"$args\"}}" |
  python3 -c 'import json,sys; print(bytes(json.load(sys.stdin)["result"]["result"]).decode())')
if [[ $registered == null ]]; then
  echo "• registering $TO with FARM"
  near contract call-function as-transaction "$FARM" storage_deposit json-args "{\"account_id\":\"$TO\"}" \
    prepaid-gas '30.0 Tgas' attached-deposit '0.0125 NEAR' sign-as "$MASTER" network-config testnet "$SIGN_WITH" send
fi
echo "• sending $AMOUNT FARM to $TO"
near contract call-function as-transaction "$FARM" ft_transfer json-args "{\"receiver_id\":\"$TO\",\"amount\":\"${AMOUNT}000000000000000000\",\"memo\":\"testnet FARM\"}" \
  prepaid-gas '30.0 Tgas' attached-deposit '1 yoctoNEAR' sign-as "$TREASURY" network-config testnet "$SIGN_WITH" send
