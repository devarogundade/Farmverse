# 12 — Cosmetic in-app purchases (design, not yet built)

Status: **designed, not implemented.** Real receipt validation needs store
credentials (a Google Play service account with the Android Publisher API, an
App Store Connect API key) and products created in both consoles; none exist
yet, and shipping half of a payment path is worse than none. This is the
build plan for when they do.

## 1. Rules

- Cosmetics only (avatar frames, plot decorations, themes). Nothing sold for
  money changes the economy: no coins, items, speed-ups or slots (fairness,
  and the marketplace stays player-driven).
- The server grants; the client never unlocks anything from a local receipt.
- Every grant is idempotent per store transaction id and audited.

## 2. Flow

1. Client: Unity IAP (`com.unity.purchasing`, already referenced) buys a
   product id from the catalogue's `cosmetics[]` (new catalogue section:
   `{ id, productId: { android, ios }, kind: frame|decoration|theme }`).
2. Client calls `iap-verifyPurchase { meta, platform, productId, receipt }` and
   only then *confirms* the pending purchase (Unity IAP two-step), so a crash
   in between leaves the purchase pending and it is retried on next launch.
3. Server: a `ReceiptVerifier` port with `GooglePlayVerifier` (Android
   Publisher `purchases.products.get`, check `purchaseState = 0`, then
   `acknowledge`) and `AppStoreVerifier` (App Store Server API
   `GET /inApps/v1/transactions/{id}`, JWS verified with Apple's root). Fake
   verifier in tests.
4. In one transaction: `purchases/{platform}:{transactionId}` must not exist →
   write it, add the cosmetic to `estate.cosmetics`, write the audit row.
   A replay returns the original grant.
5. Refunds: Play voided-purchases API and App Store Server Notifications v2
   (`REFUND`) → a job revokes the cosmetic (never coins: none were granted).

## 3. Contract additions

`iap-verifyPurchase` request/response, `PurchaseDoc` (server-only), catalogue
`cosmetics[]`, estate `cosmetics: CatalogueId[]`, error codes
`RECEIPT_INVALID`, `PURCHASE_PENDING`, `ALREADY_GRANTED`.

## 4. Tests

Verifier fakes for: valid, pending, cancelled, wrong product, replayed
transaction id, refund revocation; rules test that `purchases/*` is server-only.
