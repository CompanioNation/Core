# CompanioNation™ Google Analytics 4 Event Taxonomy

## Event Naming Convention
Events follow GA4 recommended event names where applicable, using snake_case.
Custom events use descriptive names that clearly indicate user actions.

## Standard Parameters (sent with all events)
- `app_shell`: Platform context
  - `ios` - Native iOS WKWebView wrapper
  - `android_twa` - Android Trusted Web Activity (Google Play)
  - `installed_pwa` - Installed/Microsoft Store PWA
  - `browser` - Standard web browser
- `user_id`: CompanioNation user UUID (when authenticated)

## Authentication & Onboarding Events

### `sign_up`
**Description:** User creates a new account  
**Parameters:**
- `method`: `email` | `google` | `facebook` | `apple`

### `login`
**Description:** User successfully logs in  
**Parameters:**
- `method`: `email` | `google` | `facebook` | `apple`

### `profile_created`
**Description:** User completes basic profile information entry  
**Parameters:** None

### `link_verified`
**Description:** User successfully verifies a social media link  
**Parameters:**
- `platform`: Social media platform name (e.g., `instagram`, `tiktok`, `youtube`)
- `link_count`: Total number of verified links after this action

## Discovery & Engagement Events

### `companion_viewed`
**Description:** User views a companion's full profile  
**Parameters:**
- `companion_id`: UUID of viewed companion

### `companion_liked`
**Description:** User likes/favorites a companion  
**Parameters:**
- `companion_id`: UUID of liked companion

### `search_initiated`
**Description:** User begins searching/browsing companions  
**Parameters:**
- `filter_type`: `browse` | `search` | `recommendations`

### `message_sent`
**Description:** User sends a message to a companion  
**Parameters:**
- `conversation_id`: Conversation UUID
- `is_first_message`: `true` | `false` - First message in conversation

### `connection_confirmed`
**Description:** User confirms a connection/date with a companion  
**Parameters:**
- `companion_id`: UUID of companion
- `connection_type`: Connection/meeting type if available

## Subscription & Monetization Events

### `subscription_viewed`
**Description:** User views the subscription/upgrade page  
**Parameters:** None

### `subscription_started`
**Description:** User initiates subscription purchase flow  
**Parameters:**
- `product_id`: Subscription product identifier
- `tier`: Subscription tier name (e.g., `premium`, `vip`)
- `value`: Subscription price (number)
- `currency`: Currency code (e.g., `USD`)

### `purchase`
**Description:** User completes a subscription purchase (GA4 recommended event)  
**Parameters:**
- `transaction_id`: Unique transaction identifier (client-generated GUID for GA4 de-duplication)
- `store_source`: `apple` | `google` | `microsoft` (native store that processed the purchase)
- `value`: Purchase amount (number)
- `currency`: Currency code (e.g., `CAD`)
- `items`: Array of purchased items
  - `item_id`: Product ID
  - `item_name`: Product display name
  - `item_category`: `subscription` | `in_app_purchase`
  - `price`: Item price
  - `quantity`: Item quantity

### `refund`
**Description:** Subscription refund processed (if tracked)  
**Parameters:**
- `transaction_id`: Original transaction identifier
- `value`: Refund amount (number)
- `currency`: Currency code

## Notification & Permission Events

### `notification_permission_granted`
**Description:** User grants push notification permission  
**Parameters:**
- `prompt_type`: `native_ios` | `browser_api`

### `notification_permission_denied`
**Description:** User denies push notification permission  
**Parameters:**
- `prompt_type`: `native_ios` | `browser_api`

### `notification_clicked`
**Description:** User clicks on a push notification  
**Parameters:**
- `notification_type`: Type of notification (e.g., `new_message`, `connection_confirmed`)

## App Installation Events

### `pwa_install_prompted`
**Description:** PWA install banner is shown to user  
**Parameters:**
- `platform`: `ios` | `android` | `desktop`

### `pwa_install_accepted`
**Description:** User accepts PWA installation  
**Parameters:**
- `platform`: `ios` | `android` | `desktop`

### `pwa_install_dismissed`
**Description:** User dismisses PWA installation prompt  
**Parameters:**
- `platform`: `ios` | `android` | `desktop`

## Error & Technical Events

### `error_occurred`
**Description:** Application error encountered  
**Parameters:**
- `error_type`: Error category (e.g., `network`, `authentication`, `validation`)
- `error_message`: Brief error description (sanitized, no PII)
- `page`: Current page/component where error occurred

### `page_view`
**Description:** Virtual page view (handled by GTM/GA4 auto-tracking + SPA routing)  
**Parameters:**
- `page_path`: Current route
- `page_title`: Page title

## Implementation Notes

1. All events should include `app_shell` parameter to segment by platform
2. Use `window.isNativeIosApp()`, `window.isWrapperApp()`, etc. to detect platform
3. Currency values must be numbers (not formatted strings)
4. User IDs should only be sent for authenticated users
5. Sanitize all user-generated content before sending to analytics
6. Never send PII (email addresses, real names, etc.) in event parameters
7. Use GA4 measurement protocol for server-side events when needed
