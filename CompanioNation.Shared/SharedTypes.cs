using System.ComponentModel.DataAnnotations;
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


        // Authentication errors (100000 range)
        public const int InvalidCredentials = 100000;
        public const int SessionExpired = 100001;
        public const int AccountLocked = 100002;
        public const int EmailNotVerified = 100003;


        // Subscription errors (200000 range)
        public const int SubscriptionRequired = 200000;
        public const int SubscriptionExpired = 200001;
        public const int SubscriptionInactive = 200002;
        public const int UsageLimitExceeded = 200003;

        // CompanioNita AI Service errors (300000 range)
        public const int AIServiceUnavailable = 300000;
        public const int AIRequestTimeout = 300001;
        public const int AIRateLimitExceeded = 300002;

        // Admin errors (400000 range)
        public const int AdminUnauthorized = 400000;
        public const int AdminProfileNotFound = 400001;
        public const int AdminOperationFailed = 400002;
        public const int UserMuted = 400003;

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

        public static void InitializePhotoBaseUrl(string? photoBaseUrl)
        {
            _photoBaseUrl = string.IsNullOrWhiteSpace(photoBaseUrl) ? null : photoBaseUrl;
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

        public static string GetCurrentVersion()
        {
            // Return the current version of your application.

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "Version not found";
        }
        public static string GetPhotoUrl(Guid imageGuid)
        {
            var baseUrl = _photoBaseUrl;
            if (imageGuid == Guid.Empty || baseUrl == null)
                return "/images/generic-profile.jpg";

            return $"{baseUrl.TrimEnd('/')}/{imageGuid}.jpg";
        }
        public static string StripHtmlTags(string input)
        {
            string output = input;
            // Remove any stylesheets so they don't show up in the notification
            output = Regex.Replace(output, "<\\s*style[^>]*>.*<\\s*/\\s*style[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Remove tags
            output = Regex.Replace(output, "<.*?>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return output;
        }

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
    public class CheckEmailResult
    {
        public bool emailExists { get; set; }
        public bool oauthRequired { get; set; }
    }

    public class UserDetails
    {
        public Guid? LoginToken { get; set; }

        public int UserId { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(15, ErrorMessage = "Name cannot exceed 15 characters.")]
        public string Name { get; set; }

        public string Email { get; set; } // No validation since this field is read-only on the form

        public DateTime DateCreated { get; set; }
        public bool IsAdministrator { get; set; }

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(4096, ErrorMessage = "Description cannot exceed 4096 characters.")]
        public string Description { get; set; }

        [Required(ErrorMessage = "Gender is required.")]
        [Range(2, 32, ErrorMessage = "Gender is required.")]
        public int? Gender { get; set; }

        public bool Verified { get; set; }
        public Guid? VerificationCode { get; set; }
        public DateTime? VerificationCodeTimestamp { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }

        public bool Searchable { get; set; }

        public string IpAddress { get; set; }

        public int FailedLogins { get; set; }
        public int Ranking { get; set; }

        [Required(ErrorMessage = "Date of Birth is required.")]
        [DataType(DataType.Date, ErrorMessage = "Invalid date format.")]
        [MinimumAge(18, ErrorMessage = "You must be at least 18 years old.")]
        public DateTime? DateOfBirth { get; set; }

        [Required(ErrorMessage = "City is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "City is required.")]
        public int Geonameid { get; set; }
        public string CityDisplayName { get; set; }
        public int UnreadMessagesCount { get; set; }
        [Required(ErrorMessage = "You must upload a profile picture.")]
        public Guid Thumbnail {  get; set; }
        public List<UserImage> Photos { get; set; } = new();
        public int? AcceptedTermsVersion { get; set; }
        public bool IsMuted { get; set; }
        public int PendingReportsCount { get; set; }
    }
    public class MinimumAgeAttribute : ValidationAttribute
    {
        private readonly int _minimumAge;

        public MinimumAgeAttribute(int minimumAge)
        {
            _minimumAge = minimumAge;
            ErrorMessage = $"You must be at least {_minimumAge} years old.";
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
    }
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
    }

    public sealed record LinkPhoto
    {
        public int ImageId { get; init; }
        public Guid ImageGuid { get; init; }
        public int SubjectUserId { get; init; }
        public bool ImageVisible { get; init; }
        public bool IsUploader { get; init; }
        public DateTime DateCreated { get; init; }
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

    }
