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

### Fixed — soft bounces and spam holds are recorded and shown to the account owner

The email delivery webhook (`/api/email/events`) used to drop every transient report, so a
soft bounce such as a full mailbox (ACS `Failed`) was invisible. It is now written to the
account (`email_delivery_state = 2` plus the error text), and the existing banner and Settings
warning tell the owner to fix the address; a later `Delivered` clears it and a permanent
bounce is never downgraded. Mail the provider quarantined or filtered as spam (`Quarantined`,
`FilteredSpam`) gets its own state, `HeldAsSpam` (4), because the address is fine: the gate
and Settings ask the owner to check their spam folder and add `Info@companionation.com` to
their contacts, and never to change the address. The new strings exist in all six cultures.

- **Verify:** `EmailDeliveryEventParserTests` (Services test project) and `AuthGateTests`.
- **Risk:** affected users now see a delivery prompt. Old clients treat the unknown state 4 as
  "no warning", so they are unaffected. Not deployed until promoted.
- **Files:** `Core\CompanioNationAPI\EmailDeliveryEvents.cs`, `Core\CompanioNationAPI\CoreServiceExtensions.cs`,
  `Core\CompanioNation.Shared\SharedTypes.cs`, `Core\CompanioNationDB\dbo\Stored Procedures\cn_record_email_delivery.sql`,
  `Core\CompanioNationPWA\Layout\MainLayout.razor`, `Core\CompanioNationPWA\Pages\Settings.razor`,
  `Core\CompanioNationPWA\Resources\Layout\MainLayout*.resx`, `Core\CompanioNationPWA\Resources\Pages\Settings*.resx`,
  `Core\CompanioNationPWA.Tests\AuthGateTests.cs`
- **Commit:** pending

### Fixed — Facebook login diagnostics protect credentials

Facebook provider responses are no longer logged. A missing profile email is a localized,
soft sign-in outcome; provider failures are reported without credential-bearing payloads.
Client diagnostics omit navigation queries and fragments. See the Services test entry.

- **Verify:** `FacebookLoginTests` and `FacebookDiagnosticsTests`.
- **Risk:** OAuth login and client error diagnostics; existing login and session handling remain in place.
- **Files:** `Core\CompanioNationAPI\Database.cs`,
  `Core\CompanioNationPWA\CompanioNationSignalRClient.cs`,
  `Core\CompanioNationPWA\Pages\Auth\FacebookCallback.razor`,
  `Core\CompanioNationPWA\Resources\SharedStrings*.resx`,
  `Core\CompanioNationPWA.Tests\FacebookDiagnosticsTests.cs`
- **Commit:** pending

### Changed — message pushes summarize server-side unread messages

The existing message-send stored procedure returns the recipient's unread texts, excluding
ignored senders, as the last `@push_body_length` (300) characters of each message — `RIGHT()`
clips inside SQL, so a single enormous body is never materialized or shipped to the app. Only
the newest 20 unread rows are read (`TOP (20)` over `message_id DESC`): a banner cannot show
more, and a flood of one-character messages must not become a large read. The
server joins those texts oldest-first, so a collapsed notification reads top-down and ends
with the newest text, then keeps only the final 300 code points of that summary behind a
leading ellipsis. The same bounded body is sent through both Web Push and FCM, and the workers
display it unchanged; this supersedes the earlier client-side accumulation entries below.
Because the retained part is the tail, a summary that does not fit is cut at the front rather
than the end, and a long message contributes its ending. The shared payload builder may clip
further to leave room for Web Push encryption and metadata. If nothing remains unread by the
snapshot, no message push is sent. See the Services database entry.

- **Verify:** `PushNotificationTextTests`, `node Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js`,
  and the isolated LocalDB stored-procedure check.
- **Risk:** the body is a snapshot at send time; notification delivery may be delayed or reordered.
  The procedure gained a parameter, so the database must be published before (or together with)
  the app, or message pushes stay silent until it is.
- **Files:** `Core\CompanioNationDB\dbo\Stored Procedures\cn_send_message.sql`,
  `Core\CompanioNation.Shared\SharedTypes.cs`, `Core\CompanioNationAPI\Database.cs`,
  `Core\CompanioNationAPI\CompanioNationHub.cs`, `Core\CompanioNationAPI\PushNotificationText.cs`,
  `Core\CompanioNationAPI\PushService.cs`, `Core\CompanioNationAPI\FcmPushService.cs`,
  `Core\CompanioNationPWA\wwwroot\service-worker.js`, `Core\CompanioNationPWA\wwwroot\service-worker.published.js`,
  `Core\CompanioNation.Shared.Tests\PushNotificationTextTests.cs`,
  `Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js`
- **Commit:** pending

### Fixed — push body truncation preserves emoji

Server-side truncation keeps the existing 300 UTF-16-unit limit and leading text, but does
not cut through a surrogate pair at the boundary. This applies to Web Push and FCM.

- **Verify:** `CompanioNation.Shared.Tests.PushNotificationTextTests`.
- **Risk:** only a boundary-straddling character is omitted; the existing ellipsis remains.
- **Files:** `Core\CompanioNationAPI\PushNotificationText.cs`,
  `Core\CompanioNation.Shared.Tests\PushNotificationTextTests.cs`
- **Commit:** pending

### Removed — empty SQL placeholders

Removed the two already-empty `cn_email_apply_delivery_report.sql` and
`cn_email_worker_beat.sql` files. They contained no schema definitions; see the Services
changelog for the database note.

- **Verify:** build `Core\CompanioNationDB\CompanioNationDB.csproj`.
- **Risk:** no database object definition was present in either file.
- **Files:** `Core\CompanioNationDB\dbo\Stored Procedures\cn_email_apply_delivery_report.sql`,
  `Core\CompanioNationDB\dbo\Stored Procedures\cn_email_worker_beat.sql`
- **Commit:** pending

### Fixed — overlapping message notifications retain their history

Message pushes in the same service worker now serialize their read-and-replace operations.
The notification also remembers when older messages were pruned, including across body-less
pushes. Removed the unused `SHOW_NOTIFICATION` message handler and its test; the live push
path is unchanged. This corrects the verification details in the accumulation entry below.

- **Verify:** `node Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js` (86 checks),
  including overlapping pushes, display failure recovery, and a body-less push after pruning.
- **Risk:** serialization applies only within one running worker; the stored history represents
  the visible notification, not a database query of unread messages. Native iOS FCM pushes
  do not pass through this service worker.
- **Files:** `Core\CompanioNationPWA\wwwroot\service-worker.published.js`,
  `Core\CompanioNationPWA\wwwroot\service-worker.js`,
  `Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js`
- **Commit:** pending

### Changed — a collapsed message notification keeps every unread message

Every message push uses the same notification tag (`new_message`), so the platform REPLACES
the visible notification instead of stacking them — a burst of messages left only the last
text readable. The service workers now accumulate:

- Each shown notification stores the texts it displays in `data.messages`; the next push reads
the visible notification with `self.registration.getNotifications({ tag })`, appends the new
body, and re-shows one notification with a multi-line body.
- The body is clipped to **300** code points and keeps the **newest** text with a leading `…`,
because the older messages are the ones most likely already read.
- Clipping slices by code point (`Array.from`), so an emoji is never split into half a
surrogate pair; pruning measures by code point too, so an emoji-dense history is not
clipped until its visible length genuinely exceeds 300.
- Stored history is bounded by *what the body can display*, not by a message count: only the
newest 300 characters are ever shown, so `pruneMessages` drops the oldest messages while the
remaining suffix still fills the body. The rendered body is byte-identical to keeping the whole
history (a test pins this), and `data.messages` stays around 600 characters instead of growing
without bound. An arbitrary count cap was rejected on purpose: it discarded short messages that
all fitted on screen (100 unread `hi` messages are 300 characters) while storing 6 KB of long
ones that could never be displayed.
- `data.messages` is a plain array of strings so it survives the structured clone into
`Notification.data`.
- Only `tag === 'new_message'` accumulates. Promotional/admin pushes keep their own tag and
are untouched.
- `getNotifications` is unavailable in older browsers; the failure is caught and degrades to a
single-message body rather than dropping the notification. A notification left by an older
build has no `data.messages`, so it simply restarts the list.

- **Verify:** `node Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js` (74 checks)
  runs both workers and pins the first message, the append, the sender/title change, the
  promotional bypass, the 300-code-point clip, the surrogate-pair safety, the emoji-under-cap
  retention, the legacy-notification reset, the missing-`getNotifications` fallback, the
  prune-is-invisible guarantee, bounded history, short-message retention, an oversized single
  message, a body-less push, and the in-page `SHOW_NOTIFICATION` path (same tag, same accumulation).
  Manually: send two messages from another account while the app is closed — one notification
  shows both texts.
- **Risk:** the push path is the only code touched; `notificationclick` navigation and the
  `message_received` postMessage to open windows are unchanged and still asserted. The body is
  capped at 300 characters because a Web Push payload is limited to ~4 KB.
- **Files:** `Core\CompanioNationPWA\wwwroot\service-worker.published.js`,
  `Core\CompanioNationPWA\wwwroot\service-worker.js`,
  `Core\CompanioNationPWA.Tests\Js\push-notification-body.test.js`
- **Commit:** pending

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
