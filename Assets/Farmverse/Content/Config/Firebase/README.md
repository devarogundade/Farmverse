# Firebase app configuration

| File | Platform | Project | App id |
|---|---|---|---|
| `google-services.json` | Android (`world.farmverse.mobile`) | `farmverse-8deb6` | `1:346186961940:android:53a318f2249d4674a5c301` |
| `GoogleService-Info.plist` | iOS (`world.farmverse.mobile`) | `farmverse-8deb6` | `1:346186961940:ios:d18490467cef1d08a5c301` |

The player's application id must be the same on every platform:
`Editor/FarmverseAppIdentity.cs` sets it (Farmverse ▸ Setup ▸ Apply App Identity).

The Firebase Unity editor tooling finds these files anywhere under `Assets/` and
generates the platform resources from them. Keep exactly one of each in the
project: a second copy makes the tooling pick one unpredictably.

These values are public by design (every installed app contains them). Protect
the project with App Check, security rules, and API-key restrictions in the
Google Cloud console (Android package + SHA fingerprints, iOS bundle id) — not by
hiding these files.

**One project (decided 21 Oct 2026).** `farmverse-8deb6` is the only Firebase
project: local development runs on the emulators (`Environment.dev`), every
installed build uses this project (`Environment.prod`). Test accounts and data are
deleted before launch.

**Still to do in the console** (files replaced 23 Oct 2026; Google sign-in is on — both files now carry the OAuth clients):
- Android: add the debug and release **SHA-1 and SHA-256** fingerprints to the
  Android app, then re-download `google-services.json`. Without them Google
  sign-in on Android fails (no Android OAuth client, `client_type` 1) and Play
  Integrity cannot attest the app.
- iOS: upload the APNs auth key for Cloud Messaging; register the App Attest
  provider for App Check.
- Link Google Analytics to the project (`IS_ANALYTICS_ENABLED` is false today).
