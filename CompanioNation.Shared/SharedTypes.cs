using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CompanioNation.Shared
{
    public static class ErrorCodes
    {
        // Success Result
        public const int Success = 0;

        // General Errors (50000 range)
        public const int UnknownError = 50000;
        public const int InvalidInput = 50001;
        public const int ResourceNotFound = 50002;
        public const int OperationNotAllowed = 50003;
        public const int DatabaseError = 50004;
        public const int ExternalServiceError = 50005;
        public const int ContentViolation = 50006;
        public const int ReportDuplicate = 50007;
        public const int ReportSelfReport = 50008;
        public const int FaceNotDetected = 50009;
        public const int InvalidVerificationCode = 50010;


        // Authentication errors (100000 range)
        public const int InvalidCredentials = 100000;
        public const int SessionExpired = 100001;
        public const int AccountLocked = 100002;
        public const int EmailNotVerified = 100003;
        public const int RateLimited = 100004;
        public const int EmailAlreadyExists = 100005;
        public const int OAuthEmailUnverified = 100006;


        // Subscription errors (200000 range)
        public const int SubscriptionRequired = 200000;
        public const int SubscriptionExpired = 200001;
        public const int SubscriptionInactive = 200002;
        public const int UsageLimitExceeded = 200003;

        // CompanioNita AI Service errors (300000 range)
        public const int AIServiceUnavailable = 300000;
        public const int AIRequestTimeout = 300001;
        public const int AIRateLimitExceeded = 300002;
        public const int CompanioNitaNoNewMessages = 300003;
        public const int CompanioNitaStreamInProgress = 300004;

        // Admin errors (400000 range)
        public const int AdminUnauthorized = 400000;
        public const int AdminProfileNotFound = 400001;
        public const int AdminOperationFailed = 400002;
        public const int UserMuted = 400003;
        public const int AdminSelfModificationDenied = 400004;
        public const int BadgeNotFound = 400005;

        // LINK errors (500000 range)
        public const int LinkExpired = 500000;
        public const int LinkInvalid = 500001;
        public const int LinkSelfLink = 500002;
        public const int LinkAlreadyExists = 500003;
        public const int LinkFaceNotDetected = 500004;
        public const int LinkRateLimited = 500005;
        public const int LinkBlocked = 500006;
        public const int LinkNotFound = 500007;
        public const int LinkPhotoNotYours = 500008;

        // Client contract / upgrade errors (600000 range)
        // Returned when a connected client is too old for the method it invoked.
        // This is a first-class SOFT result, not a real error: the client shows the
        // "update available" prompt and never logs/buffers/emails it.
        public const int ClientUpgradeRequired = 600000;
    }

    public static class Util
    {
        private static string? _photoBaseUrl = null;

        // VAPID Public Key for Web Push Notifications
        // This key is PUBLIC and safe to expose - it's meant to be shared with clients
        // The corresponding private key MUST be stored in environment variable VAPID_PRIVATE_KEY
        // ⚠️ Also used in: CompanioNationPWA/wwwroot/pwa-install.js (passed as parameter)
        // If you rotate this key, update both the constant here and the VAPID_PRIVATE_KEY env var
        public const string VapidPublicKey = "BAEB8xOGLlEfy3LA9ZVg_VaZ_noyG5pX8wgwIcU82mR5HdUiMZVE4cLg9jm71dBE_L10ww7ph-Y_Zlq9Q7ZHo-I";

        /// <summary>
        /// Number of new non-CompanioNita messages required after the most recent
        /// CompanioNita advice before another insight request is allowed. Shared by
        /// the Blazor client (friendly UI guard) and the server (DOS guard).
        /// </summary>
        public const int CompanioNitaRequiredNewMessagesAfterAdvice = 2;

        public static void InitializePhotoBaseUrl(string? photoBaseUrl)
        {
            _photoBaseUrl = string.IsNullOrWhiteSpace(photoBaseUrl) ? null : photoBaseUrl;
        }

        private static string _siteBaseUrl = "https://companionation.com";

        /// <summary>
        /// The public origin used when building absolute links in outbound emails
        /// (password reset, email verification, LINK invites, subscription pages)
        /// and the sitemap. Configured per environment via COMPANIONATION_SITE_BASE_URL
        /// so the staging/alt slot links to its own host instead of production.
        /// Defaults to the production origin.
        /// </summary>
        public static string SiteBaseUrl => _siteBaseUrl;

        public static void InitializeSiteBaseUrl(string? siteBaseUrl)
        {
            _siteBaseUrl = string.IsNullOrWhiteSpace(siteBaseUrl)
                ? "https://companionation.com"
                : siteBaseUrl.TrimEnd('/');
        }

        public static string GetGenderString(int? gender)
        {
            if (gender == null) return "Unknown";
            if (gender == 0) return "Unknown";
            if (gender == 2) return "Male";
            if (gender == 4) return "Female";
            if (gender == 8) return "Other";
            if (gender == 16) return "Trans Male";
            if (gender == 32) return "Trans Female";
            return "Invalid Gender";
        }

        /// <summary>Returns an abbreviated gender label suitable for compact UI (e.g., "M", "F", "TM").</summary>
        public static string GetGenderShortString(int? gender) => gender switch
        {
            2 => "M",
            4 => "F",
            8 => "O",
            16 => "TM",
            32 => "TF",
            _ => "?"
        };

        /// <summary>
        /// Wire-contract tag for client→server log submissions (hub LogError/LogClientError).
        /// Passed as a REQUIRED first argument so that whenever the log contract changes,
        /// every client bundle built before the change fails at SignalR argument dispatch
        /// and cannot reach the server error/email pipeline at all. Bump the value whenever
        /// the log payload shape changes; keep in sync with CompanioNationAPI's hub constant.
        /// </summary>
        public const string ClientLogSchema = "cn-log-v2";

        public static string GetCurrentVersion()
        {
            // Return the current version of your application.

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "Version not found";
        }

        /// <summary>
        /// Formats a UTC timestamp as the equivalent time in Vancouver
        /// (America/Vancouver, DST-aware), e.g. "2026-08-14 16:36:54 -07:00".
        /// Falls back to a plain UTC string if the time zone is unavailable.
        /// </summary>
        public static string FormatVancouverTime(DateTime utc)
        {
            DateTime utcNormalized = utc.Kind switch
            {
                DateTimeKind.Local => utc.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(utc, DateTimeKind.Utc),
                _ => utc
            };

            var utcOffset = new DateTimeOffset(utcNormalized.Ticks, TimeSpan.Zero);

            foreach (var timeZoneId in new[] { "America/Vancouver", "Pacific Standard Time" })
            {
                try
                {
                    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    var vancouver = TimeZoneInfo.ConvertTime(utcOffset, timeZone);
                    return vancouver.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                {
                    // Try the next identifier.
                }
            }

            return utcNormalized.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }
        public static string GetPhotoUrl(Guid imageGuid)
        {
            var baseUrl = _photoBaseUrl;
            if (imageGuid == Guid.Empty || baseUrl == null)
                return "/images/generic-profile.jpg";

            return $"{baseUrl.TrimEnd('/')}/{imageGuid}.jpg";
        }

        /// <summary>
        /// Builds the self-referential canonical URL for a page so each localized version
        /// (?lang=xx) is canonical to itself and Google indexes every language instead of
        /// collapsing them into the language-neutral URL. English is served at the bare URL
        /// (no ?lang=); a ?page=N (N&gt;1) parameter is preserved for list paths (ending in "/").
        /// </summary>
        public static string GetCanonicalUrl(string absoluteUrl)
        {
            var uri = new Uri(absoluteUrl);
            var parts = GetMeaningfulQueryParts(uri, preserveLang: true);
            return BuildSeoUrl(uri, parts);
        }

        /// <summary>
        /// Builds the hreflang alternate URL for a given language of the page at
        /// <paramref name="absoluteUrl"/>. English lives at the bare URL; other languages get
        /// ?lang=xx. A ?page=N (N&gt;1) parameter is preserved for list paths (ending in "/")
        /// so paginated versions stay inside the same language cluster.
        /// </summary>
        public static string GetHreflangUrl(string absoluteUrl, string languageCode)
        {
            var uri = new Uri(absoluteUrl);
            var parts = GetMeaningfulQueryParts(uri, preserveLang: false);
            string normalized = SupportedLanguages.Normalize(languageCode);
            if (normalized != "en")
                parts.Add($"lang={normalized}");
            return BuildSeoUrl(uri, parts);
        }

        private static List<string> GetMeaningfulQueryParts(Uri uri, bool preserveLang)
        {
            var parts = new List<string>();
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;

                if (kv[0].Equals("page", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(kv[1], out var page) && page > 1 &&
                    uri.AbsolutePath.EndsWith('/'))
                {
                    parts.Add($"page={page}");
                }
                else if (preserveLang && kv[0].Equals("lang", StringComparison.OrdinalIgnoreCase))
                {
                    string normalized = SupportedLanguages.Normalize(Uri.UnescapeDataString(kv[1]));
                    if (normalized != "en")
                        parts.Add($"lang={normalized}");
                }
            }
            return parts;
        }

        private static string BuildSeoUrl(Uri uri, List<string> parts)
        {
            string baseUrl = $"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath}";
            return parts.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", parts)}";
        }

        /// <summary>
        /// Parses an image blob name of the form "{guid}.jpg" and returns the image GUID.
        /// Returns false for any name that is not a valid image blob name.
        /// </summary>
        public static bool TryGetImageGuidFromBlobName(string? blobName, out Guid imageGuid)
        {
            imageGuid = Guid.Empty;

            if (string.IsNullOrWhiteSpace(blobName) ||
                !blobName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string guidPart = blobName[..^4];
            return Guid.TryParse(guidPart, out imageGuid);
        }
        public static string StripHtmlTags(string input)
        {
            string output = input;
            // Remove any stylesheets so they don't show up in the notification
            output = Regex.Replace(output, "<\\s*style[^>]*>.*<\\s*/\\s*style[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Handle a truncated/unclosed <style> block: strip from the opening tag to end
            // of string so raw CSS never leaks into notification/share text.
            output = Regex.Replace(output, "<\\s*style[^>]*>.*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove tags
            output = Regex.Replace(output, "<.*?>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return output;
        }

        /// <summary>
        /// Strips markdown code fences (```html...```) that some AI models wrap around
        /// HTML responses despite being told not to.
        /// </summary>
        public static string StripMarkdownCodeFences(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return html ?? string.Empty;
            string trimmed = html.Trim();

            // Strip leading ```html or ```language
            if (trimmed.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[7..];
            else if (trimmed.StartsWith("```"))
            {
                int newlineIdx = trimmed.IndexOf('\n');
                trimmed = newlineIdx >= 0 ? trimmed[(newlineIdx + 1)..] : trimmed[3..];
            }

            // Strip trailing ```
            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];

            return trimmed.Trim();
        }

        /// <summary>
        /// The CompanioNita design system: a curated, class-based stylesheet for all
        /// AI-authored CompanioNita content (daily advice columns, personalized advice,
        /// and conversation insights in the messages pane). Every selector is scoped
        /// under the <c>.companionita</c> wrapper (or a <c>companionita-*</c> class), so
        /// the styles can be reused verbatim in the shadow-DOM (JS-on) and inline
        /// (SSR/no-JS) render paths without leaking to the surrounding page.
        ///
        /// The class names listed here are the ONLY styling vocabulary exposed to the
        /// model via <see cref="CompanioNitaStyleGuide"/>. Keep the two in sync.
        /// </summary>
        public const string CompanioNitaStyles = """
.companionita { font-family: Georgia, 'Times New Roman', serif; line-height: 1.65; color: #2b2b2b; }
.companionita h1, .companionita-heading1 { font-size: 1.6rem; line-height: 1.25; margin: 0 0 14px; color: #1a1a1a; }
.companionita h2, .companionita-heading2 { font-size: 1.2rem; margin: 1.5em 0 0.3em; color: #3d2e1e; border-bottom: 1px solid #d9cfc0; padding-bottom: 4px; }
.companionita h3, .companionita-heading3 { font-size: 1.05rem; margin: 1.3em 0 0.3em; color: #3d2e1e; }
.companionita p { margin: 0.7em 0; }
.companionita a { color: #1565c0; }
.companionita ul, .companionita ol { margin: 0.6em 0 0.6em 1.4em; padding: 0; }
.companionita li { margin: 0.35em 0; }
.companionita blockquote, .companionita-quote { margin: 0.8em 0; padding: 8px 14px; border-left: 3px solid #b8a88a; background: #f6f1e9; font-style: italic; color: #4b3f2e; }
.companionita img { max-width: 100%; height: auto; }
.companionita hr { border: 0; border-top: 1px solid #d9cfc0; margin: 1.4em 0; }
.companionita-lead { font-size: 1.05rem; color: #3a3a3a; }
.companionita-dateline { font-size: 0.85rem; text-transform: uppercase; letter-spacing: 0.06em; color: #7a6f5d; margin-bottom: 8px; }
.companionita-example, .companionita-takeaway { background: #f6f1e9; padding: 10px 14px; border-left: 3px solid #b8a88a; border-radius: 0 4px 4px 0; margin: 0.6em 0 1em; }
.companionita-takeaway { background: #fff7e8; border-color: #e3d5bb; }
.companionita-note { font-size: 0.9rem; color: #6a6152; }
.companionita-closing { margin-top: 1.4em; color: #2f281c; }
.companionita-icon { margin-right: 6px; }
.companionita strong { color: #3b2e1a; }
""";

        /// <summary>
        /// Compact variant of <see cref="CompanioNitaStyles"/> for tight chat bubbles
        /// (e.g. the main-page CompanioNita chat). Overrides the generous editorial
        /// spacing with tighter margins so responses use less vertical space without
        /// changing any other CompanioNita output surface (daily advice columns, past
        /// advice archive, or messages-pane insights). Appended after the base styles
        /// when a component opts in via <see cref="RenderAdviceContent"/>'s compact flag.
        /// </summary>
        public const string CompanioNitaCompactStyles = """
.companionita { line-height: 1.45; }
.companionita p { margin: 0.35em 0; }
.companionita > :first-child, .companionita-heading1:first-child, .companionita-heading2:first-child, .companionita-heading3:first-child { margin-top: 0; }
.companionita > :last-child, .companionita-closing:last-child { margin-bottom: 0; }
.companionita p:empty { display: none; }
.companionita h1, .companionita-heading1 { margin: 0 0 6px; }
.companionita h2, .companionita-heading2 { margin: 0.6em 0 0.2em; }
.companionita h3, .companionita-heading3 { margin: 0.55em 0 0.2em; }
.companionita ul, .companionita ol { margin: 0.35em 0 0.35em 1.3em; }
.companionita li { margin: 0.15em 0; }
.companionita blockquote, .companionita-quote { margin: 0.4em 0; padding: 6px 12px; }
.companionita hr { margin: 0.6em 0; }
.companionita-example, .companionita-takeaway { margin: 0.4em 0 0.6em; padding: 8px 12px; }
.companionita-closing { margin-top: 0.6em; }
""";

        /// <summary>
        /// System-prompt instruction that tells the model how to style its HTML output
        /// using the <see cref="CompanioNitaStyles"/> design system. Restricts the model
        /// to semantic tags plus the <c>companionita-*</c> class vocabulary, and forbids
        /// <c>&lt;style&gt;</c>, inline styles, and <c>&lt;html&gt;/&lt;body&gt;</c>
        /// wrappers so its output can be rendered safely inside a shadow DOM.
        /// </summary>
        public const string CompanioNitaStyleGuide =
            "Format your answer as HTML using only semantic tags (p, h1-h3, ul, ol, li, strong, em, a, blockquote, hr) "
            + "and, for special blocks, only these class names: companionita-heading1, companionita-heading2, companionita-heading3, companionita-lead, companionita-dateline, companionita-example, companionita-takeaway, companionita-quote, companionita-note, companionita-closing, companionita-icon. "
            + "Do NOT emit a <style> block, do NOT use inline style=\"\" attributes, do NOT wrap the output in <html>/<head>/<body>, and do NOT invent any other class names or CSS. "
            + "Output only the visible content as a fragment.";

        /// <summary>
        /// Extracts the visible body content from an AI-authored HTML document and
        /// discards its own <c>&lt;style&gt;</c>/<c>&lt;script&gt;</c> blocks. AI output
        /// is free-form and unpredictable, so its embedded stylesheet must never leak
        /// onto the host page; the curated <see cref="CompanioNitaStyles"/> is applied
        /// instead. Falls back to the whole (cleaned) string when there are no
        /// <c>&lt;body&gt;</c> tags (e.g. a chat fragment).
        /// </summary>
        public static string ExtractAdviceBody(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string output = input;

            // Never allow AI-authored content to execute script.
            output = Regex.Replace(output, "<\\s*script[^>]*>.*?<\\s*/\\s*script[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Discard the AI's own stylesheet(s) so its CSS cannot restyle the host page.
            output = Regex.Replace(output, "<\\s*style[^>]*>.*?<\\s*/\\s*style[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            output = Regex.Replace(output, "<\\s*style[^>]*>.*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Prefer the <body> content when present (full-document advice).
            var body = Regex.Match(output, "<\\s*body[^>]*>(?<content>.*?)<\\s*/\\s*body[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (body.Success)
                return body.Groups["content"].Value.Trim();

            // No body wrapper (e.g. a chat fragment): strip head and the html/body shells.
            output = Regex.Replace(output, "<\\s*head[^>]*>.*?<\\s*/\\s*head[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            output = Regex.Replace(output, "<\\s*/?\\s*(html|head|body|title|meta|link)[^>]*>", string.Empty, RegexOptions.IgnoreCase);

            return output.Trim();
        }

        /// <summary>
        /// Produces self-contained, style-isolated markup for AI-authored advice/chat
        /// content: the curated <see cref="CompanioNitaStyles"/> plus the extracted
        /// body wrapped in a <c>.companionita</c> element. Identical markup is used for
        /// the shadow-DOM and inline (SSR) render paths.
        /// </summary>
        public static string RenderAdviceContent(string? html, bool compact = false)
            => $"<style>{CompanioNitaStyles}{(compact ? CompanioNitaCompactStyles : "")}</style><div class=\"companionita\">{ExtractAdviceBody(html)}</div>";

        /// <summary>Calculates age from a birthday relative to UTC today.</summary>
        public static int CalculateAge(DateTime birthday)
        {
            var today = DateTime.UtcNow.Date;
            var age = today.Year - birthday.Year;
            if (birthday > today.AddYears(-age)) age--;
            return age;
        }

        /// <summary>Returns the Google Tag Manager &lt;script&gt; snippet for the &lt;head&gt;, or empty string if <paramref name="gtmId"/> is null.</summary>
        public static string GtmHeadScript(string? gtmId = null)
        {
            if (string.IsNullOrWhiteSpace(gtmId)) return "";
            var encoded = WebUtility.HtmlEncode(gtmId);
            return $"<script>(function(w,d,s,l,i){{w[l]=w[l]||[];w[l].push({{'gtm.start':new Date().getTime(),event:'gtm.js'}});var f=d.getElementsByTagName(s)[0],j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';j.async=true;j.src='https://www.googletagmanager.com/gtm.js?id='+i+dl;f.parentNode.insertBefore(j,f);}})(window,document,'script','dataLayer','{encoded}');</script>";
        }

        /// <summary>Returns the Google Tag Manager &lt;noscript&gt; snippet for the &lt;body&gt;, or empty string if <paramref name="gtmId"/> is null.</summary>
        public static string GtmBodyNoscript(string? gtmId = null)
        {
            if (string.IsNullOrWhiteSpace(gtmId)) return "";
            var encoded = WebUtility.HtmlEncode(gtmId);
            return $"<noscript><iframe src=\"https://www.googletagmanager.com/ns.html?id={encoded}\" height=\"0\" width=\"0\" style=\"display:none;visibility:hidden\"></iframe></noscript>";
        }

        /// <summary>Renders the "fruit loopy" 500 error page HTML with an optional GTM tag.</summary>
        public static string RenderFruitLoopyErrorHtml(string? gtmId = null)
        {
            var gtmHead = GtmHeadScript(gtmId);
            var gtmBody = GtmBodyNoscript(gtmId);

            return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>CompanioNation - Error</title>
  {{gtmHead}}
  <style>
    body{font-family:Arial,Helvetica,sans-serif;max-width:900px;margin:0 auto;padding:24px;line-height:1.6;}
    header{display:flex;align-items:center;gap:12px;margin-bottom:16px;}
    header img{height:256px;width:256px;}
    .card{border:1px solid #e0e0e0;border-radius:8px;padding:16px;box-shadow:0 2px 4px rgba(0,0,0,0.05);}
    a{color:#1565c0;text-decoration:none;font-weight:700;}
    a:hover{text-decoration:underline;}
    footer{margin-top:24px;font-size:0.9em;color:#666;}
  </style>
</head>
<body>
  {{gtmBody}}
  <header>
    <img src="/images/CompanioNita.png" alt="CompanioNita" />
    <div>
      <h1>Well… that went fruit loopy 🍍</h1>
      <p style="margin-top:4px;color:#555;">CompanioNita tripped over a server-side banana peel.</p>
    </div>
  </header>

  <div class="card">
    <p>Try again, or head back home.</p>
    <p><a href="/">Return to CompanioNation</a></p>
  </div>

  <footer>This error has been logged.</footer>
</body>
</html>
""";
        }

    }

    public class ResponseWrapper<T>
    {
        public string Version { get; set; }
        public bool IsSuccess { get; set; }
        public T Data { get; set; }
        public string Message { get; set; }
        public int ErrorCode { get; set; } // Use error codes to specify failure types
        // Factory method for a successful response
        public static ResponseWrapper<T> Success(T data, string message = "")
        {
            return new ResponseWrapper<T>
            {
                Version = Util.GetCurrentVersion(),
                IsSuccess = true,
                Data = data,
                Message = message,
                ErrorCode = 0 // No error
            };
        }

        // Factory method for a failed response with an error code
        public static ResponseWrapper<T> Fail(int errorCode, string message)
        {
            return new ResponseWrapper<T>
            {
                Version = Util.GetCurrentVersion(),
                IsSuccess = false,
                Data = default,
                Message = message,
                ErrorCode = errorCode
            };
        }
    }

    public sealed record ClientErrorReport
    {
        public string? CorrelationId { get; init; }
        public string? Route { get; set; }
        public string? AppVersion { get; set; }
        /// <summary>Runtime platform the report came from: "web", "android", "apple", or "ms_store_app".</summary>
        public string? Platform { get; set; }
        /// <summary>Version of the native app shell (iOS/Android/MS Store), when running inside one; null on the web.</summary>
        public string? NativeAppVersion { get; set; }
        public int? UserId { get; set; }
        public string? Source { get; init; }
        public string? Message { get; init; }
        public string? Filename { get; init; }
        public int? LineNumber { get; init; }
        public int? ColumnNumber { get; init; }
        public string? Stack { get; init; }
        public string? EventType { get; init; }
        public bool? IsTrusted { get; init; }
        public string? UserAgent { get; init; }
        public string? Url { get; init; }
        public string? Referrer { get; init; }
        public string? TagName { get; init; }
    }

    public class ConnectResult
    {
        public string PhotosBaseUrl { get; set; }
        public ResponseWrapper<UserDetails> CurrentUser { get; set; }
    }

    public class OAuthConfig
    {
        public string GoogleClientId { get; set; }
        public string AppleServiceId { get; set; }
        public string FacebookAppId { get; set; }
        public string TwitterClientId { get; set; }
        public string MicrosoftClientId { get; set; }
    }

    public class CheckEmailResult
    {
        public bool emailExists { get; set; }
        public bool oauthRequired { get; set; }
    }

    public class UserDetails
    {
        public Guid? LoginToken { get; set; }

        public int UserId { get; set; }

        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_NameRequired")]
        [StringLength(15, ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_NameTooLong")]
        public string Name { get; set; }

        public string Email { get; set; } // No validation since this field is read-only on the form

        public string? NewEmail { get; set; }

        public string? OldEmail { get; set; }

        public DateTime DateCreated { get; set; }
        public bool IsAdministrator { get; set; }

        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_DescriptionRequired")]
        [StringLength(4096, ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_DescriptionTooLong")]
        public string Description { get; set; }

        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_GenderRequired")]
        [Range(2, 32, ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_GenderRequired")]
        public int? Gender { get; set; }

        public bool Verified { get; set; }
        public bool OAuthLogin { get; set; }
        public Guid? VerificationCode { get; set; }
        public DateTime? VerificationCodeTimestamp { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }
        public string? PaymentSystem { get; set; }

        public bool Searchable { get; set; }

        public string IpAddress { get; set; }

        public int FailedLogins { get; set; }
        public int Ranking { get; set; }

        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_DateOfBirthRequired")]
        [DataType(DataType.Date, ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_InvalidDate")]
        [MinimumAge(18)]
        public DateTime? DateOfBirth { get; set; }

        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_CityRequired")]
        [Range(1, int.MaxValue, ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_CityRequired")]
        public int Geonameid { get; set; }
        public string CityDisplayName { get; set; }
        public int UnreadMessagesCount { get; set; }
        [Required(ErrorMessageResourceType = typeof(SharedValidationStrings), ErrorMessageResourceName = "Validation_ProfilePictureRequired")]
        public Guid Thumbnail {  get; set; }
        public List<UserImage> Photos { get; set; } = new();
        public int? AcceptedTermsVersion { get; set; }
        public bool IsMuted { get; set; }
        public int PendingReportsCount { get; set; }
        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// Admin-editable user attributes that are not part of the public profile edit flow:
    /// subscription expiry, administrator status, verification, mute state, and an
    /// optional admin-set password.
    /// </summary>
    public class AdminUserAttributes
    {
        public int UserId { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }
        public bool IsAdministrator { get; set; }
        public bool Verified { get; set; }
        public bool IsMuted { get; set; }

        /// <summary>Optional. When empty/null, the existing password is left unchanged.</summary>
        public string? NewPassword { get; set; }
    }

    /// <summary>A single bucket on a time-series chart (e.g., one day's signup count).</summary>
    public class StatBucket
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    /// <summary>Aggregated site-wide statistics for the admin dashboard.</summary>
    public class SiteStats
    {
        // Headline totals
        public int TotalUsers { get; set; }
        public int VerifiedUsers { get; set; }
        public int UsersWithActiveSubscription { get; set; }
        public int Administrators { get; set; }
        public int MutedUsers { get; set; }
        public int UsersWithPhotos { get; set; }
        public int TotalPhotos { get; set; }
        public int TotalMessages { get; set; }
        public int TotalConnections { get; set; }

        // Signup snapshots
        public int SignupsToday { get; set; }
        public int SignupsLast7Days { get; set; }
        public int SignupsLast30Days { get; set; }

        // Recent-activity snapshots (based on cn_users.last_login = most recent login per user)
        public int ActiveToday { get; set; }
        public int ActiveLast7Days { get; set; }
        public int ActiveLast30Days { get; set; }

        // Time-series buckets
        public List<StatBucket> SignupsByDay { get; set; } = new();      // last 30 days
        public List<StatBucket> SignupsByMonth { get; set; } = new();    // last 12 months
        public List<StatBucket> SignupsByYear { get; set; } = new();     // all years
        public List<StatBucket> ActiveUsersByDay { get; set; } = new();  // last 30 days (by last_login)

        public DateTime GeneratedAtUtc { get; set; }
    }

    public class MinimumAgeAttribute : ValidationAttribute
    {
        private readonly int _minimumAge;

        public MinimumAgeAttribute(int minimumAge)
        {
            _minimumAge = minimumAge;
            ErrorMessageResourceType = typeof(SharedValidationStrings);
            ErrorMessageResourceName = "Validation_MinimumAge";
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value is DateTime dateOfBirth)
            {
                var age = DateTime.Today.Year - dateOfBirth.Year;
                if (dateOfBirth > DateTime.Today.AddYears(-age)) age--;

                if (age < _minimumAge)
                {
                    return new ValidationResult(ErrorMessage);
                }
            }
            return ValidationResult.Success;
        }
    }
    public class Companion
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public int Gender { get; set; }
        public DateTime? Birthday { get; set; }
        public string Description { get; set; }
        public int Ranking { get; set; }
        public List<Guid> Images { get; set; } = new List<Guid>();
        public string CityDisplayName { get; set; }
        public List<Review> Reviews { get; set; }
        public bool IsIgnored { get; set; }
        public int Referrals { get; set; }
    }
    public class Settings
    {
        public string DailyAdvice { get; set; }
        public string PreviousDailyAdvice { get; set; }
        public DateTime LastMaintenanceRun { get; set; }
        // Add more properties corresponding to columns in the settings table
    }
    public class Advice
    {
        public string Prompt { get; set; }
        public string Response { get; set; }
    }

    /// <summary>
    /// A single CompanioNita advice thread (one row in cn_advice_threads), as listed in the
    /// Home page sidebar. Named "thread" to avoid confusion with user-to-user "conversations".
    /// </summary>
    public sealed record AdviceThread
    {
        public int ThreadId { get; init; }
        public string? Title { get; init; }
        public string? LastPrompt { get; init; }
        public int ExchangeCount { get; init; }
        public DateTime DateCreated { get; init; }
        public DateTime LastUpdated { get; init; }
    }

    /// <summary>
    /// A single CompanioNita question-and-answer exchange (one row in cn_advice_exchanges).
    /// <see cref="Prompt"/> is the member's question; <see cref="Response"/> is CompanioNita's
    /// reply (null while the reply is still being generated).
    /// </summary>
    public sealed record AdviceExchange
    {
        public int ExchangeId { get; init; }
        public string Prompt { get; init; } = string.Empty;
        public string? Response { get; init; }
        public DateTime DateCreated { get; init; }
    }
    public class OrphanedImage
    {
        public Guid ImageGuid { get; set; }
        public string BlobName { get; set; }
    }
    public class UserImage
    {
        public int ImageId { get; set; }
        public int UserId { get; set; }
        public bool ImageVisible { get; set; }
        public Guid ImageGuid { get; set; }
        public int GuarantorUserId { get; set; }
        public DateTime DateCreated { get; set; }
        public int? Rating { get; set; } // Nullable because it can be NULL in the database
        public string? Review { get; set; } // Nullable because it can be NULL in the database
        public bool ReviewVisible { get; set; }
        
    }
    public class GuaranteedUser
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public int ImageId { get; set; } // Add ImageId for database reference
        public Guid ImageGuid { get; set; } // Specific image associated with the guarantee
        public int Rating { get; set; }
        public string Review { get; set; }
    }
    public class Review
    {
        public string Text { get; set; }
        public DateTime Date { get; set; }
    }
    public class UserConversation
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Gender { get; set; }
        public DateTime? Birthday { get; set; }
        public City Location { get; set; }
        public int Ranking { get; set; }
        public int UnreadMessageCount { get; set; }

        // Add this property to handle the last message timestamp
        public int NewestMessage { get; set; }
        public List<Guid> Photos { get; set; }
        public List<Review> Reviews { get; set; }
        public bool IsIgnored {  get; set; }
        public bool IgnoredByMe { get; set; }
    }

    public class UserMessage
    {
        public int MessageId { get; set; }
        public int FromUserId { get; set; }
        public int ToUserId { get; set; }
        public string MessageText { get; set; }
        public bool IsRead { get; set; }
        public DateTime DateCreated { get; set; }
        public string FromUserName { get; set; }
        public string ToUserName { get; set; }
        public bool IsCompanioNitaAdvice { get; set; }
    }

    public class CompanioNitaAdvice
    {
        public int AdviceId { get; set; }
        public DateTime DateCreated { get; set; }
        public string Advice { get; set; }
    }

    public class SendMessageResult
    {
        public string LoginToken { get; set; }
        public int ToUserId { get; set; }
        public int MessageId { get; set; }
        public int FromUserId { get; set; }
        public string FromUserName { get; set; }
        public string MessageText { get; set; }
        public bool IsCompanioNitaAdvice { get; set; }
        public DateTime? IgnoredSince { get; set; }
        public string PushToken { get; set; }

        /// <summary>
        /// The user ID that owns <see cref="PushToken"/> (the message recipient).
        /// Used for stale-token cleanup so the sender's token is never cleared.
        /// </summary>
        public int PushTokenUserId { get; set; }

        /// <summary>
        /// The recipient's current unread (non-CompanioNita) message count, used to
        /// set a numeric iOS app icon badge instead of a hardcoded 1.
        /// </summary>
        public int RecipientUnreadCount { get; set; }
    }
    /// <summary>
    /// Generic push notification payload used for admin broadcast/targeted sends.
    /// Decouples the transport from the message-specific <see cref="SendMessageResult"/>.
    /// </summary>
    public sealed record PushPayload
    {
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string Url { get; init; } = "/";
        public int? Badge { get; init; }
        public string Tag { get; init; } = "promotional";
        public int? UserId { get; init; }
    }

    /// <summary>Summary returned after an admin broadcast/targeted push send.</summary>
    public sealed record BroadcastResult(int Total, int Sent, int Failed);

    /// <summary>A user id plus their non-empty push token, for admin broadcast sends.</summary>
    public sealed record PushTokenRow(int UserId, string PushToken);

    public class PushSubscriptionModel
    {
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; }
        [JsonPropertyName("expirationTime")]
        public long? ExpirationTime { get; set; }
        [JsonPropertyName("keys")]
        public Keys Keys { get; set; }
    }

    public class Keys
    {
        [JsonPropertyName("p256dh")]
        public string P256dh { get; set; }
        [JsonPropertyName("auth")]
        public string Auth { get; set; }
    }

    public class Country
    {
        public string CountryCode { get; set; }
        public string CountryName { get; set; }
    }
    public class City
    {
        public int Geonameid { get; set; }
        public string ContinentCode { get; set; }
        public string CountryCode { get; set; }
        public string CountryName { get; set; }
        public string Admin1Name { get; set; }
        public string CityName { get; set; }
    }

    // SEO browse DTOs
    public sealed record BrowseCountry
    {
        public string CountryCode { get; init; }
        public string CountryName { get; init; }
        public string ContinentCode { get; init; }
        public int ProfileCount { get; init; }
    }

    public sealed record BrowseProvince
    {
        public string Admin1Code { get; init; }
        public string Admin1Name { get; init; }
        public int ProfileCount { get; init; }
    }

    public sealed record BrowseCity
    {
        public int Geonameid { get; init; }
        public string CityName { get; init; }
        public int ProfileCount { get; init; }
    }

    public sealed record BrowseProfileSummary
    {
        public int UserId { get; init; }
        public string Name { get; init; }
        public int Gender { get; init; }
        public string Description { get; init; }
        public int Ranking { get; init; }
        public int SeoClicks { get; init; }
        public DateTime? Birthday { get; init; }
        public Guid Thumbnail { get; init; }
        public string CityDisplayName { get; init; }
    }

    public sealed record BrowseProfileDetail
    {
        public int UserId { get; init; }
        public string Name { get; init; }
        public int Gender { get; init; }
        public string Description { get; init; }
        public int Ranking { get; init; }
        public int SeoClicks { get; init; }
        public DateTime? Birthday { get; init; }
        public string CityDisplayName { get; init; }
        public List<Guid> Images { get; init; } = [];
        public List<Review> Reviews { get; init; } = [];
    }

    public sealed record BrowseProfilesResult
    {
        public int TotalCount { get; init; }
        public List<BrowseProfileSummary> Profiles { get; init; } = [];
    }

    public sealed record SitemapUrls
    {
        public List<string> CountryCodes { get; init; } = [];
        public List<SitemapProvince> Provinces { get; init; } = [];
        public List<SitemapCity> Cities { get; init; } = [];
        public List<int> ProfileUserIds { get; init; } = [];
        public List<SitemapAdvice> Advice { get; init; } = [];
    }

    public sealed record SitemapAdvice
    {
        public int AdviceId { get; init; }
        public DateTime DateCreated { get; init; }
    }

    public sealed record SitemapProvince
    {
        public string CountryCode { get; init; }
        public string Admin1Code { get; init; }
    }

    public sealed record SitemapCity
    {
        public string CountryCode { get; init; }
        public string Admin1Code { get; init; }
        public int Geonameid { get; init; }
    }

    public sealed record LinkedUser
    {
        public int UserId { get; init; }
        public string Name { get; init; }
        public int ConnectionId { get; init; }
        public int LinkType { get; init; }
        public DateTime DateLinked { get; init; }
        public List<LinkPhoto> Photos { get; init; } = [];
        public Guid Thumbnail { get; init; }
        public int KarmaEarned { get; init; }

        // Caller's review OF the linked user (editable on the LINK tab)
        public int? MyRating { get; init; }
        public string? MyReview { get; init; }
        public bool MyReviewVisible { get; init; }

        // Linked user's review OF the caller (read-only; surfaced when visible)
        public int? TheirRating { get; init; }
        public string? TheirReview { get; init; }
        public bool TheirReviewVisible { get; init; }
    }

    public sealed record LinkPhoto
    {
        public int ImageId { get; init; }
        public Guid ImageGuid { get; init; }
        public int SubjectUserId { get; init; }
        public bool ImageVisible { get; init; }
        public bool SubjectConfirmed { get; init; }
        public bool IsUploader { get; init; }
        public bool IsSubject { get; init; }
        public DateTime DateCreated { get; init; }
        public string UploaderName { get; init; } = "";
        public int? Rating { get; init; }
        public string? Review { get; init; }
        public bool ReviewVisible { get; init; }
    }

    public sealed record KarmaDesync
    {
        public int UserId { get; init; }
        public string Name { get; init; }
        public int StoredRanking { get; init; }
        public int CalculatedRanking { get; init; }
        public int Delta { get; init; }
    }

    public sealed record LinkPayload
    {
        [JsonPropertyName("u")]
        public int UserId { get; init; }
        [JsonPropertyName("t")]
        public long Timestamp { get; init; }
        [JsonPropertyName("s")]
        public string Signature { get; init; }
    }

    /// <summary>
    /// Result of the guarantor_user_id → connection_id data migration.
    /// </summary>
    public sealed record GuarantorMigrationResult
    {
        public int TotalImages { get; init; }
        public int Migrated { get; init; }
        public int Orphaned { get; init; }
        public int AlreadyMigrated { get; init; }
    }

    /// <summary>Report type constants for cn_reports.</summary>
    public static class ReportTypes
    {
        public const int Profile = 1;
        public const int Message = 2;
        public const int Photo = 3;
    }

    /// <summary>Report reason constants for cn_reports.</summary>
    public static class ReportReasons
    {
        public const int Harassment = 1;
        public const int Spam = 2;
        public const int HateSpeech = 3;
        public const int ExplicitContent = 4;
        public const int Impersonation = 5;
        public const int Other = 6;
    }

    /// <summary>Report status constants for cn_reports.</summary>
    public static class ReportStatuses
    {
        public const int Pending = 0;
        public const int Reviewed = 1;
        public const int ActionTaken = 2;
        public const int Dismissed = 3;
    }

    public sealed record ReportRequest(int ReportedUserId, int ReportType, int ReportReason, string? ReportDetail, int? ReferenceId);

    public sealed record ReportResult(int ReportId);

    public sealed record PendingReport
    {
        public int ReportId { get; init; }
        public int ReporterUserId { get; init; }
        public string ReporterName { get; init; }
        public int ReportedUserId { get; init; }
        public string ReportedName { get; init; }
        public int ReportType { get; init; }
        public int ReportReason { get; init; }
        public string? ReportDetail { get; init; }
        public int? ReferenceId { get; init; }
        public int Status { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>
    /// An event badge definition or a badge awarded to a user.
    /// <see cref="DateAwarded"/> is null for badge definitions returned by the admin list.
    /// </summary>
    public sealed record EventBadge
    {
        public int BadgeId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Icon { get; init; } = "🏅";
        public DateTime? DateAwarded { get; init; }
    }

    }
