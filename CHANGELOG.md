# CompanioNation — Core changelog

Client (`CompanioNationPWA`), shared library (`CompanioNation.Shared`), and the open-source
pieces of the product. Server/database/infrastructure changes are recorded in
`Services\CHANGELOG.md`.

**Why this file exists:** when something breaks, the first question is "what changed
recently?" — and the second is "how do I check that it still works?". A commit subject
answers neither. Every entry here answers both, so a regression can be narrowed to a few
candidates instead of a whole release.

## How to add an entry

Write the entry **in the same change that makes it**, before you commit. Add it under
`## Unreleased`, newest first, using one of these headings:

- `Added` — new feature
- `Changed` — behaviour change to something that already worked
- `Fixed` — a bug fix
- `Removed` — something deliberately taken out (say why, and what replaces it)
- `Security` — auth, tokens, permissions, CSP, secrets

Every entry carries the fields that make it actionable:

| Field | Why it matters |
|---|---|
| `Verify:` | exactly how to confirm it works — test name, URL, or click path. This is what makes a change testable instead of "it seemed fine". |
| `Risk:` | what else shares the code path, so you know what to re-check if it breaks |
| `Files:` | the paths touched, so a bisect or revert is obvious |
| `Commit:` | short hash, filled in when committed (write `pending` if not yet committed) |

Rules:

- **A behaviour change with no `Verify:` line is not finished.** If it cannot be verified,
  say so explicitly and why.
- **Never delete or rewrite history.** Corrections get a new entry that points at the old one.
- **Never record secrets, tokens, connection strings, or customer data** — describe the change.
- On release, `## Unreleased` is retitled to the deployed build stamp (`yy.M.d.H`, UTC) and a
  fresh empty `## Unreleased` is added on top. The stamp is the same literal baked into the
  `cn_db_version` view and returned by `GetCurrentVersion`, so a changelog section can be
  matched to a deployed build.

---

## Unreleased

### Changed — the cache counter is byte-proportional (`Loading… 1.4/2.9 MB · 48%`)

The counter advanced one step per file, so it read `150/312` while a single 8 MB
`dotnet.native.wasm` was still downloading — file counts are a poor proxy for time. Progress
is now measured in bytes: `Loading… 1.4/2.9 MB · 48%`.

The precache manifest carries only `url` + `hash`, so the byte sizes are measured at build
time and emitted as a sidecar next to it:

- `CompanioNationPWA.csproj` runs an inline Roslyn task that stats every manifest asset and
  writes `service-worker-asset-sizes.js` (`self.cnAssetSizes = {"url": bytes, …}`). It runs
  right after the manifest is generated so the URLs match exactly, and the file is registered
  as a static web asset so it is served in dev and published with the app.
- The file is tagged `AssetTraitName=BlazorServiceWorker`, which keeps it OUT of the precache
  manifest — otherwise the client would need a size for the size file itself.
- A publish-side target re-measures against the *published* manifest, which lists a different
  (smaller, AOT/trimmed) asset set than the build manifest. Verified with a real Release
  publish: 165 assets / 38,968,887 bytes, and the published manifest cross-checks with **0**
  missing sizes.
- The asset is registered with `AssetKind=All`. This is load-bearing: a `Build`-only asset is
  dropped by the publish pipeline, so production would 404 the size list and silently fall
  back to per-file counts. Do not "tidy" it back to `Build`.
- The counter is **all-or-nothing**: if the size list is missing, stale, or does not cover
  every precached asset, it falls back to the file count instead of claiming 100% early.
- It is **invisible on localhost dev, by design**: dev serves `wwwroot\service-worker.js`, the
  deliberate no-op stub (no `fetch` handler, no `caches.open`, no precache), so no
  `offline-cache-*` ever exists and the counter has nothing to count. See it on a published
  build — staging, or `dotnet publish` + any static server on `wwwroot`.
- Units are shared when they match, so it reads `1.4/2.9 MB`, never `1.4 MB/2.9 MB`.

- **Verify:** `node Core\CompanioNationPWA.Tests\Js\pwa-cache-progress.test.js` (12 checks)
  covers byte rendering, growth, the incomplete-size fallback and KB formatting. A build logs
  `Service worker asset sizes: 316 assets, 34,733,482 bytes`; a Release publish logs
  `165 assets, 38,968,887 bytes` and writes `wwwroot\service-worker-asset-sizes.js` (+ `.br`/
  `.gz`) to the publish output.
- **Risk:** the inline task sits in the static-web-asset pipeline, so a failure fails the
  build loudly. The counter still degrades to file counts if the sidecar is unavailable.
- **Files:** `Core\CompanioNationPWA\CompanioNationPWA.csproj`,
  `Core\CompanioNationPWA\wwwroot\pwa-install.js`,
  `Core\CompanioNationPWA.Tests\Js\pwa-cache-progress.test.js`
- **Commit:** pending

### Fixed — "Loading…" button no longer showed the cache counter (`Loading… 145/307`)

The cold-boot cache progress counter disappeared from the sign-in button's busy label.
It was deleted wholesale by `5b6d35f` ("Improve PWA update activation and caching"), which
rewrote the precache flow around `CN_PRECACHE` / `window.cnActivateUpdate` and dropped the
counter block as collateral. Nothing else replaced it, so users saw only a spinner.

The counter is restored with the original semantics plus two hardening fixes:

- counts **only** assets the worker actually precaches (from `service-worker-assets.js`),
  so runtime-cached extras can no longer push the numerator past the total (`183/165`)
- reads the cache belonging to **this** build (`offline-cache-<version>`), never an older
  tab's cache
- degrades to a bare count if the manifest is blocked, instead of rendering `0/0`
- is removed once `cnBlazorReady` is set, so a later busy state cannot read a stale
  `159/159`
- still renders if the busy label appears *after* caching has settled (the counter no longer
  depends on the cached count changing to draw itself)
- `aria-hidden`, because the button's `aria-label` already announces "Loading…"

It is read-only (Cache API + DOM text) and sends no messages, so it adds no console noise.

- **Verify:** `node Core\CompanioNationPWA.Tests\Js\pwa-cache-progress.test.js`;
  manually, throttle to Slow 3G, load the site cold and watch the sign-in button after ~1s.
- **Risk:** `pwa-install.js` is also the PWA update/install flow — after touching it, re-run
  `pwa-update-flow.test.js` and check the update toast and install prompt still work.
- **Files:** `Core\CompanioNationPWA\wwwroot\pwa-install.js`,
  `Core\CompanioNationPWA.Tests\Js\pwa-cache-progress.test.js` (new)
- **Commit:** pending

### Added — regression test for the cache counter

`pwa-cache-progress.test.js` runs the real counter block from `pwa-install.js` in a `vm`
sandbox with a stubbed Cache API. It pins the properties above, so the counter cannot
be silently deleted again.

- **Verify:** the test above, wired into CI in `Services\.github\workflows\build-and-test.yml`.
- **Risk:** none at runtime — test-only.
- **Files:** `Core\CompanioNationPWA.Tests\Js\pwa-cache-progress.test.js`
- **Commit:** pending

### Added — this changelog

- **Verify:** n/a (documentation).
- **Files:** `Core\CHANGELOG.md`
- **Commit:** pending

---

## History (seeded from git, before this changelog existed)

Entries below are reconstructed from commit subjects only — they have no `Verify:` detail
because it was never recorded. Treat them as "what changed when", not as test instructions.

| Date | Commit | Change |
|---|---|---|
| 2026-11-23 | `5b6d35f` | Improve PWA update activation and caching — new precache/activation flow; **removed** the cold-boot cache counter (see Unreleased) |
| 2026-11-23 | `51344f3` | Replace Guarantee with LINK; queue email delivery |
| 2026-11-21 | `d73dfb4` | Optimize service worker precaching |
| 2026-11-20 | `5e0d156` | Fix localized validation resource access |
| 2026-11-19 | `1e2462d` | Bound yesterday stats to complete days |
| 2026-11-19 | `cfd8dea` | Guard against cleared `CurrentUser` (login/session-restore path — high risk) |
| 2026-11-19 | `9ac3635` | Fix PWA install cache progress (the counter's last working version) |
| 2026-11-19 | `27f2e91` | Fix events opt-in switch flex layout |
| 2026-11-19 | `f155a27` | Add local event notification preferences |
| 2026-11-19 | `66f2fac` | Harden push registration diagnostics |
| 2026-11-15 | `417c40e` | Add daily CompanioNita query cap for paid users |
| 2026-11-02 | `ae62dbe` | Harden DOB validation and request diagnostics |
