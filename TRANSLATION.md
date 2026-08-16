# Translation Status

> Track all user-facing text across CompanioNation and its translation status into supported languages.
> When adding new text to a `.resx` file, add the key to ALL language `.resx` files listed below.

## Supported Languages

| Code | Language | Native Name | Priority |
|------|----------|-------------|----------|
| `en` | English | English | 1 (neutral/default) |
| `es` | Spanish | Español | 2 |
| `pt` | Portuguese | Português | 3 |
| `fr` | French | Français | 4 |
| `zh` | Mandarin Chinese | 中文 | 5 |
| `ja` | Japanese | 日本語 | 6 |

## Legend

- `[x]` = Translated and reviewed
- `[ ]` = Pending (contains English fallback or TODO placeholder)
- `N/A` = Not applicable (neutral .resx is the English source)

---

## LandingPage (`Core/CompanioNationPWA/Resources/Pages/LandingPage`)

| Resource Key | en | es | pt | fr | zh | ja |
|---|---|---|---|---|---|---|
| `LandingPage_LoginButton` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_Headline` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_Feature_Free` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_Feature_Validation` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_Feature_AI` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_Feature_BuiltBy` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_Title` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_Free` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_Worldwide` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_NoLikeGame` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_NoLimits` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_CompanioNita` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_FreeAdvice` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_Scammers` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_Ratings` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_WhatIs_ComingSoon` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_EmailsDoNotMatch` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_GetItOnGooglePlay` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_DownloadOnTheAppStore` | N/A | [x] | [x] | [x] | [x] | [x] |
| `LandingPage_GetItFromMicrosoft` | N/A | [x] | [x] | [x] | [x] | [x] |

### LandingPage DataAnnotation Strings

Localized via `SharedStrings` (`ErrorMessageResourceType = typeof(SharedStrings)`).

---

## App Shell (`Core/CompanioNationPWA/Resources/App`)

The Blazor root `<head>`/`<body>` shell (`App.razor`), localized via `IStringLocalizer<App>`. All 6 language `.resx` files exist; machine-generated translations are pending human review.

| Resource Key | en | es | pt | fr | zh | ja |
|---|---|---|---|---|---|---|
| `App_Title` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_MetaDescription` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_NoscriptMessage` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_NoscriptCorner` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_NoscriptBrowse` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_ErrorHeading` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `App_ErrorReload` | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |

---

## SEO Pages (`Services/CompanioNationServices/Resources/SeoPages`)

Server-rendered `/s/browse/*` and `/s/join` pages (localized via `IStringLocalizer<SeoPages>`). All 6 language `.resx` files exist. Machine-generated translations are pending human review.

| Resource Group | en | es | pt | fr | zh | ja |
|---|---|---|---|---|---|---|
| `Browse_*` — browse chrome (titles, breadcrumbs, empty states, counts, gender labels, footer; 37 keys) | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |
| `Join_*` — join page (headings, labels, buttons, value props, JS fallbacks, no-JS notice; 28 keys) | N/A | [ ] | [ ] | [ ] | [ ] | [ ] |

> Note: user profile data (names, descriptions, reviews) is stored verbatim and is not part of this resource set.

---

## Pages Localized (Phase 2)

Localized into all 6 languages (neutral + es, pt, fr, zh, ja) with machine translations pending review:

- Layout: `Footer.razor`, `CompanioNationLogo.razor`, `MainLayout.razor`, `Login.razor`
- Pages: `Home.razor`, `FindCompanion.razor`, `Messages.razor`, `Settings.razor`, `EnterBasicInfo.razor`, `Link.razor`, `ViewCompanion.razor`, `Contact.razor`, `Terms.razor`, `ResetPassword.razor`, `CompanioNitasCorner.razor`, `Guarantee.razor`, `Test.razor`, `PrivacyPolicy.razor`
- Subscribe components: `Services/CompanioNation.Components.Full/SubscribeToCompanioNita.razor` and `Core/CompanioNation.Components.Stub/SubscribeToCompanioNita.razor` (each with its own `Resources/SubscribeToCompanioNita.*.resx`)

## Pages Not Yet Localized

- `Admin.razor` — admin-only panel, intentionally not localized (not user-facing)

## Static-HTML Shell (localized via `wwwroot/js/static-shell-i18n.js`)

`index.html` and `loading.html` are plain static files (no `IStringLocalizer`), so their pre-Bootstrap text is localized at runtime by `wwwroot/js/static-shell-i18n.js` (reads `?lang=`, the `culture` localStorage key, then `navigator.languages`).

> Production search-engine indexing does **not** rely on these static files: `CompanioNationPWA/App.razor` is server-prerendered and emits localized `<title>`, `<meta name="description">`, `<html lang>`, canonical, and `hreflang` for all 6 languages; `/s/browse/*` and `/s/join` do the same server-side.

### DataAnnotation Validation Strings

- `LandingPage.razor`, `ResetPassword.razor`, `Guarantee.razor`, `Link.razor` — localized via `SharedStrings` (`ErrorMessageResourceType`)
- `CompanioNation.Shared/SharedTypes.cs` (`UserDetails`) — localized via `SharedValidationStrings` (`ErrorMessageResourceType`)

### Shared Components Localized (via `Resources/SharedStrings.resx`)

`ActionButton`, `AdviceOfTheDay`, `AppleSignInButton`, `FacebookSignInButton`, `GoogleSignInButton`, `MicrosoftSignInButton`, `XSignInButton`, `FeedbackButton`, `ShareButton`, `ReportButton`, `CustomErrorBoundary`, `QRCodeComponent`, `CameraComponent`, `Routes`, `CitySelect`, `SearchableCities`, and the OAuth callback screens in `Pages/Auth`.

### Shared Components Still To Localize

_None — all shared components are now localized via `Resources/SharedStrings.resx`._

---

## Process: Adding New Text

1. Add the new `<data>` entry to the **neutral** `.resx` file (e.g., `LandingPage.resx`) with the English value.
2. Add the SAME `<data>` entry (same `name` attribute) to **every** culture-specific `.resx`:
   - `LandingPage.es.resx`
   - `LandingPage.pt.resx`
   - `LandingPage.fr.resx`
   - `LandingPage.zh.resx`
   - `LandingPage.ja.resx`
3. If you have the translation, use it. Otherwise use the English value with a `TODO: translate to [Language]` comment in the `.resx` file.
4. Update this `TRANSLATION.md` — add the new key row to the appropriate page table with `[ ]` for untranslated languages.

---

## Adding a New Language (Checklist)

Adding a language touches **both repositories** plus the iOS app. Do all of these:

### Core repo (Blazor side)
1. `Core/CompanioNationPWA/Services/CultureService.cs` — add the code to `SupportedCultures`
2. `Core/CompanioNationPWA/Components/LanguageSelector.razor` — add the native display name in `GetDisplayName`
3. Create the culture-specific `.resx` for **every** page in `Core/CompanioNationPWA/Resources/Pages/` (copy the neutral `.resx` keys)
4. `Core/TRANSLATION.md` — add the language column to every page table

### ios-app repo
5. `ios-app/pwa-shell/Info.plist` — add the code to `CFBundleLocalizations`
6. `ios-app/pwa-shell.xcodeproj/project.pbxproj` — add the region to `knownRegions` (use `zh-Hans` for Simplified Chinese, not `zh`)

> **Why the iOS steps matter:** WKWebView reports `navigator.language` based on the app's *declared* localizations, not the raw device language. Without `CFBundleLocalizations`, a Portuguese device can still report `"en"`.
>
> **Robust runtime fix (already implemented):** `ios-app/pwa-shell/WebView.swift` injects `Locale.preferredLanguages` (the raw OS language list) into the WebView at document start, overriding `navigator.language`/`navigator.languages`. This makes auto-detection work for the *actual* device language even if a language is not yet declared in `Info.plist`. Keep both: `CFBundleLocalizations` for the App Store/OS, and the injection for correct WebView detection.

### Android / MSIX / web
No equivalent config is required — their WebView engines already report the raw device language via `navigator.language`.

