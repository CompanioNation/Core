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

### LandingPage DataAnnotation Strings (hardcoded in C#, not yet localized)

| Location | English Text | Status |
|---|---|---|
| `NewAccountModel.Email` | `"Email is required."` | Not localized |
| `NewAccountModel.Email` | `"Invalid email address."` | Not localized |
| `NewAccountModel.ConfirmEmail` | `"Confirm Email is required."` | Not localized |
| `NewAccountModel.ConfirmEmail` | `"The email addresses do not match."` | Not localized |

---

## Pages Not Yet Localized

These pages contain hardcoded English text and need `.resx` files created:

- `EnterBasicInfo.razor` — user profile setup form
- `Settings.razor` — user settings forms
- `FindCompanion.razor` — companion search
- `Messages.razor` — messaging UI
- `Home.razor` — main dashboard
- `Link.razor` — link/sharing feature
- `ViewCompanion.razor` — companion profile view
- `Contact.razor` — contact form
- `PrivacyPolicy.razor` — legal text
- `Terms.razor` — legal text
- `ResetPassword.razor` — password reset flow
- `Admin.razor` — admin panel
- `ConfirmConnection.razor` — connection confirmation
- `CompanioNitasCorner.razor` — CompanioNita AI chat
- `Guarantee.razor` — deprecated, superseded by Link
- `Test.razor` — testing page (low priority)

### Shared Components with Hardcoded English

- `MainLayout.razor` — navigation, menus, alerts
- `Login.razor` — login/signup forms
- `Footer.razor` — footer links and text
- `AdviceOfTheDay.razor` — daily advice widget
- `Information.razor` — info tooltips
- `ContestLeaderBoard.razor` — contest rankings
- `ActionButton.razor` — button labels
- `FeedbackButton.razor` — feedback UI
- `ShareButton.razor` — share prompts
- `ReportButton.razor` — report UI
- `HubStatusMessage.razor` — connection status messages
- `CustomErrorBoundary.razor` — error messages
- `QRCodeComponent.razor` — QR code UI
- `CameraComponent.razor` — camera UI
- `ThumbnailComponent.razor` — thumbnail UI
- `AppleSignInButton.razor` — "Sign in with Apple"
- `FacebookSignInButton.razor` — "Sign in with Facebook"
- `GoogleSignInButton.razor` — "Sign in with Google"
- `MicrosoftSignInButton.razor` — "Sign in with Microsoft"
- `XSignInButton.razor` — "Sign in with X"

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
