using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CompanioNation.Shared
{
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


}
