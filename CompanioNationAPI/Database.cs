using Microsoft.Data.SqlClient;
using CompanioNation.Shared;
using System.Data;
using System.Text.Json;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CompanioNationAPI
{
    public class Database
    {
        private readonly string _connectionString;

        public Database()
        {
            _connectionString = Environment.GetEnvironmentVariable("COMPANIONATION_DATABASE") ?? string.Empty;
        }

        public async Task<ResponseWrapper<UserDetails>> GetUserAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<UserDetails>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_validate_login_token", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return ResponseWrapper<UserDetails>.Success(ReadUserDetails(reader));
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Propagate the specific error code for token timeout
                return ResponseWrapper<UserDetails>.Fail(100000, "Token has expired or is invalid.");
            }
            catch (Exception ex)
            {
                // Log the error for general exceptions
                ErrorLog.LogErrorException(ex, loginToken);
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, "Unknown error occurred.");
            }

            // Return the specific error if no rows were read or an error occurred.
            return ResponseWrapper<UserDetails>.Fail(100000, "Token has expired or is invalid.");
        }


        private UserDetails ReadUserDetails(SqlDataReader reader)
        {
            // Create the LoginResult with user details from the database
            return
                new UserDetails
                {
                    LoginToken = reader.IsDBNull(reader.GetOrdinal("login_token")) ? null : reader.GetGuid(reader.GetOrdinal("login_token")),
                    UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                    Email = reader.GetString(reader.GetOrdinal("email")),
                    IsAdministrator = reader.GetBoolean(reader.GetOrdinal("is_administrator")),
                    DateCreated = reader.GetDateTime(reader.GetOrdinal("date_created")),
                    LastLogin = reader.IsDBNull(reader.GetOrdinal("last_login")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("last_login")),
                    Description = reader.GetString(reader.GetOrdinal("description")),
                    Gender = reader.IsDBNull(reader.GetOrdinal("gender")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("gender")),
                    Searchable = reader.GetBoolean(reader.GetOrdinal("searchable")),
                    VerificationCode = reader.IsDBNull(reader.GetOrdinal("verification_code")) ? Guid.Empty : reader.GetGuid(reader.GetOrdinal("verification_code")),
                    Verified = reader.GetBoolean(reader.GetOrdinal("verified")),
                    VerificationCodeTimestamp = reader.IsDBNull(reader.GetOrdinal("verification_code_timestamp")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("verification_code_timestamp")),
                    Ranking = reader.GetInt32(reader.GetOrdinal("ranking")),
                    IpAddress = reader.IsDBNull(reader.GetOrdinal("ip_address")) ? string.Empty : reader.GetString(reader.GetOrdinal("ip_address")),
                    FailedLogins = reader.GetInt32(reader.GetOrdinal("failed_logins")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    DateOfBirth = reader.IsDBNull(reader.GetOrdinal("bday")) ? null : reader.GetDateTime(reader.GetOrdinal("bday")),
                    UnreadMessagesCount = reader.GetInt32(reader.GetOrdinal("unread_messages_count")),
                    Thumbnail = reader.IsDBNull("thumbnail") ? Guid.Empty : reader.GetGuid("thumbnail"),
                    Geonameid = reader.IsDBNull("geonameid") ? 0 : reader.GetInt32("geonameid"),
                    CityDisplayName = (reader.IsDBNull(reader.GetOrdinal("city_name")) ? string.Empty : reader.GetString("city_name")) +
                                     ", " +
                                     (reader.IsDBNull(reader.GetOrdinal("admin1_name")) ? string.Empty : reader.GetString("admin1_name")) +
                                     ", " +
                                     (reader.IsDBNull(reader.GetOrdinal("country_name")) ? string.Empty : reader.GetString("country_name"))
                };
        }
        public async Task<ResponseWrapper<UserDetails>> LoginAsync(string email, string password, string ipAddress, bool oauthLogin)
        {
            try
            {
                if (string.IsNullOrEmpty(email) || (string.IsNullOrEmpty(password) && !oauthLogin)) return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Credentials");

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync(); // Open connection only when needed

                    using (var cmd = new SqlCommand("cn_login", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@email", email));
                        cmd.Parameters.Add(new SqlParameter("@password", password ?? (object)DBNull.Value));
                        cmd.Parameters.Add(new SqlParameter("@oauth_login", oauthLogin));
                        cmd.Parameters.Add(new SqlParameter("@ip_address", ipAddress));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                UserDetails details = ReadUserDetails(reader);

                                if (string.IsNullOrWhiteSpace(details.Name))
                                {
                                    // TODO get the name from Google OAUTH as well as the profile photo

                                }

                                return ResponseWrapper<UserDetails>.Success(details);
                            }
                            else
                            {
                                // No rows returned indicates failure
                                return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Credentials");
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle specific SQL error for invalid login credentials
                return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Credentials");
            }
            catch (SqlException ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<UserDetails>.Fail(ex.Number, "Unknown SQL Error");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, "Unexpected Error");
            }
        }


        sealed class GoogleTokenResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("access_token")]
            public string AccessToken { get; set; } // access_token

            [System.Text.Json.Serialization.JsonPropertyName("id_token")]
            public string IdToken { get; set; }     // id_token

            [System.Text.Json.Serialization.JsonPropertyName("token_type")]
            public string TokenType { get; set; }   // token_type

            [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }      // expires_in

            [System.Text.Json.Serialization.JsonPropertyName("scope")]
            public string Scope { get; set; }       // scope

            [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } // refresh_token (optional)
        }

        sealed class GoogleUserInfo
        {
            [System.Text.Json.Serialization.JsonPropertyName("email")]
            public string Email { get; set; }                 // email

            // v2 userinfo (deprecated) returns "verified_email"
            [System.Text.Json.Serialization.JsonPropertyName("verified_email")]
            public bool VerifiedEmail { get; set; }           // verified_email (v1)

            // OIDC userinfo returns "email_verified"
            [System.Text.Json.Serialization.JsonPropertyName("email_verified")]
            public bool EmailVerified { get; set; }           // email_verified (OIDC)

            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; }                  // name (optional)

            [System.Text.Json.Serialization.JsonPropertyName("picture")]
            public string Picture { get; set; }               // picture (optional)

            [System.Text.Json.Serialization.JsonPropertyName("given_name")]
            public string GivenName { get; set; }             // given_name (optional)

            [System.Text.Json.Serialization.JsonPropertyName("family_name")]
            public string FamilyName { get; set; }            // family_name (optional)

            [System.Text.Json.Serialization.JsonPropertyName("sub")]
            public string Sub { get; set; }                   // sub (subject)
        }
        public async Task<ResponseWrapper<UserDetails>> LoginWithGoogleAsync(string code, string code_verifier, string redirect_uri, string ipAddress)
        {
            try
            {
                if (string.IsNullOrEmpty(code) 
                    || string.IsNullOrEmpty(code_verifier) 
                    || string.IsNullOrEmpty(redirect_uri)) 
                    return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Google ID token.");

                // Read Google OAuth settings from environment (fallbacks to sensible defaults for endpoints)
                var tokenEndpoint = "https://oauth2.googleapis.com/token";
                var userInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
                var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");

                if (string.IsNullOrWhiteSpace(clientId))
                {
                    ErrorLog.LogErrorMessage("Google OAuth client configuration is missing. Please make sure the GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET environment variables are defined.");
                    return ResponseWrapper<UserDetails>.Fail(100000, "Google OAuth client configuration is missing.");
                }

                // 1) Exchange authorization code + PKCE verifier for tokens
                using var http = new HttpClient();

                var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirect_uri,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["code_verifier"] = code_verifier
                });

                var tokenResponse = await http.PostAsync(tokenEndpoint, tokenRequest);
                var tokenPayload = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode || string.IsNullOrWhiteSpace(tokenPayload))
                {
                    ErrorLog.LogErrorMessage("Google Login Error: " + tokenPayload);
                    return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Google ID token.");
                }

                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var tokenObj = JsonSerializer.Deserialize<GoogleTokenResponse>(tokenPayload, jsonOptions);

                if (tokenObj == null || string.IsNullOrWhiteSpace(tokenObj.AccessToken))
                {
                    ErrorLog.LogErrorMessage("Google Login Error: " + tokenPayload);
                    return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Google ID token.");
                }

                // 2) Retrieve user info (email, name, picture)
                string email = string.Empty;
                string? googleName = null;
                string? googlePictureUrl = null;
                GoogleUserInfo? userInfo = null;

                using (var userInfoRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, userInfoEndpoint))
                {
                    userInfoRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenObj.AccessToken);
                    userInfoRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    var userInfoResponse = await http.SendAsync(userInfoRequest);
                    var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();

                    if (userInfoResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(userInfoJson))
                    {
                        userInfo = JsonSerializer.Deserialize<GoogleUserInfo>(userInfoJson, jsonOptions);
                        email = userInfo?.Email ?? string.Empty;
                        googleName = userInfo?.Name;
                        googlePictureUrl = userInfo?.Picture;

                        // Prefer verified email if flags are present
                        var verified = userInfo?.VerifiedEmail == true || userInfo?.EmailVerified == true;
                        if (!string.IsNullOrWhiteSpace(email) && !verified)
                        {
                            // Not strictly required to reject, but safer to enforce
                            email = string.Empty;
                        }
                    }
                }

                // 3) Fallback: parse fields from ID token if needed
                if (string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(tokenObj.IdToken))
                {
                    email = TryGetEmailFromIdToken(tokenObj.IdToken) ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(googleName) && !string.IsNullOrWhiteSpace(tokenObj.IdToken))
                {
                    googleName = TryGetClaimFromIdToken(tokenObj.IdToken, "name");
                }
                if (string.IsNullOrWhiteSpace(googlePictureUrl) && !string.IsNullOrWhiteSpace(tokenObj.IdToken))
                {
                    googlePictureUrl = TryGetClaimFromIdToken(tokenObj.IdToken, "picture");
                }

                if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email)) {
                    ErrorLog.LogErrorMessage("Google Login Error: " + tokenPayload);
                    return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Google ID token.");
                }

                // 4) Log in (or create session) using email
                var loginResult = await LoginAsync(email, null, ipAddress, true);
                if (!loginResult.IsSuccess || loginResult.Data == null) return loginResult;

                var details = loginResult.Data;

                // 5) If name is empty, update it from Google profile
                try
                {
                    if (!string.IsNullOrWhiteSpace(googleName) && string.IsNullOrWhiteSpace(details.Name) && details.LoginToken.HasValue)
                    {
                        // TODO *** TEST THIS!!! make it more efficient too. like why is it creating a whole new userdetails object??
                        var trimmed = googleName.Trim();
                        if (trimmed.Length > 15) trimmed = trimmed.Substring(0, 15);

                        var updatePayload = new UserDetails
                        {
                            Name = trimmed,
                            Description = details.Description,
                            Searchable = details.Searchable,
                            Gender = details.Gender,
                            Geonameid = details.Geonameid,
                            DateOfBirth = details.DateOfBirth
                        };

                        var updateRes = await UpdateUserDetailsAsync(details.LoginToken.Value.ToString(), updatePayload);
                        if (!updateRes.IsSuccess)
                        {
                            ErrorLog.LogErrorMessage($"Failed to update user name from Google profile for user {details.UserId}. Error: {updateRes.Message}");
                        }
                        else
                        {
                            details.Name = trimmed; // keep in-memory result aligned
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.LogErrorException(ex, "Error updating user name from Google profile.");
                }

                // 6) If no thumbnail yet, try to pull Google profile picture and save as a photo
                try
                {
                    if (details.Thumbnail == Guid.Empty && !string.IsNullOrWhiteSpace(googlePictureUrl) && details.LoginToken.HasValue)
                    {
                        // TODO *** TEST THIS ***

                        // Local function to download an image safely
                        static async Task<byte[]?> DownloadAsync(HttpClient client, string url)
                        {
                            try
                            {
                                using var resp = await client.GetAsync(url);
                                if (!resp.IsSuccessStatusCode) return null;
                                var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                                // Basic check to ensure it's an image
                                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Some Google URLs may not return content-type; allow empty but keep it safe
                                }
                                var bytes = await resp.Content.ReadAsByteArrayAsync();
                                return (bytes != null && bytes.Length > 0) ? bytes : null;
                            }
                            catch
                            {
                                return null;
                            }
                        }

                        var imageBytes = await DownloadAsync(http, googlePictureUrl);
                        if (imageBytes != null)
                        {
                            var uploadRes = await UploadPhotoAsync(details.LoginToken.Value.ToString(), imageBytes, ipAddress);
                            if (!uploadRes.IsSuccess)
                            {
                                ErrorLog.LogErrorMessage($"Failed to save Google profile picture for user {details.UserId}. Error: {uploadRes.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.LogErrorException(ex, "Error saving Google profile picture.");
                }

                return loginResult;
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                ErrorLog.LogErrorException(ex, "SQL Error in LoginWithGoogleAsync method.");
                return ResponseWrapper<UserDetails>.Fail(100000, "Invalid Google ID token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithGoogleAsync method.");
                return ResponseWrapper<UserDetails>.Fail(ex.HResult, "Unexpected error occurred.");
            }
        }

        // Local DTOs and helpers
        static string? TryGetEmailFromIdToken(string idToken)
        {
            try
            {
                var parts = idToken.Split('.');
                if (parts.Length < 2) return null;

                var payloadJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String)
                    return emailProp.GetString();

                return null;
            }
            catch
            {
                return null;
            }
        }

        static string? TryGetClaimFromIdToken(string idToken, string claimName)
        {
            try
            {
                var parts = idToken.Split('.');
                if (parts.Length < 2) return null;

                var payloadJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty(claimName, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();

                return null;
            }
            catch
            {
                return null;
            }
        }

        static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 0: break;
                case 2: s += "=="; break;
                case 3: s += "="; break;
                default: throw new FormatException("Illegal base64url string!");
            }
            return Convert.FromBase64String(s);
        }




        public async Task<ResponseWrapper<List<Companion>>> GetContestLeaderBoard()
        {
            var companions = new List<Companion>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_contest_leaderboard", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var companion = new Companion
                                {
                                    UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    Gender = reader.GetInt32(reader.GetOrdinal("gender")),
                                    Birthday = reader.IsDBNull(reader.GetOrdinal("bday")) ? (DateTime?)null : (DateTime)reader.GetDateTime(reader.GetOrdinal("bday")), // Handle nulls here
                                    Description = reader.GetString(reader.GetOrdinal("description")),
                                    Ranking = reader.GetInt32(reader.GetOrdinal("ranking")),
                                    CityDisplayName = reader.GetString("city_name") + ", " + reader.GetString("admin1_name") + ", " + reader.GetString("country_name"),
                                    Referrals = reader.GetInt32("referrals")
                                };

                                // Ensure the imagesJson string is not empty or improperly formatted before deserialization
                                string imagesJson = reader.IsDBNull(reader.GetOrdinal("images"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("images"));

                                companion.Images = await ParseImages(imagesJson);

                                companions.Add(companion);
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<Companion>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching leaderboard");
                return ResponseWrapper<List<Companion>>.Fail(ex.HResult, "Error fetching companions");
            }

            return ResponseWrapper<List<Companion>>.Success(companions);
        }

        public async Task<ResponseWrapper<CompanioNitaAdvice>> GetCompanitaAdvice(int adviceId)
        {
            try
            {
                CompanioNitaAdvice advice = null;

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_companionita_advice_by_id", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@advice_id", adviceId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                advice = new CompanioNitaAdvice();
                                advice.AdviceId = reader.GetInt32("advice_id");
                                advice.DateCreated = reader.GetDateTime("date_created");
                                advice.Advice = reader.GetString("advice_text");
                            }
                        }
                    }
                }

                return ResponseWrapper<CompanioNitaAdvice>.Success(advice);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<CompanioNitaAdvice>.Fail(ex.HResult, ex.Message);
            }
        }
        public async Task<ResponseWrapper<List<CompanioNitaAdvice>>> GetCompanitaAdvice(int start, int count)
        {
            try
            {
                List<CompanioNitaAdvice> advice = new List<CompanioNitaAdvice>(count);

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_companionita_advice", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@start", start);
                        cmd.Parameters.AddWithValue("@count", count);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                CompanioNitaAdvice a = new CompanioNitaAdvice();
                                a.AdviceId = reader.GetInt32("advice_id");
                                a.DateCreated = reader.GetDateTime("date_created");
                                a.Advice = reader.GetString("advice_text");

                                advice.Add(a);
                            }
                        }
                    }
                }

                return ResponseWrapper<List<CompanioNitaAdvice>>.Success(advice);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<List<CompanioNitaAdvice>>.Fail(ex.HResult, ex.Message);
            }
        }
        public async Task<bool> SaveCompanionitaAdvice(string advice_text)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_add_companionita_advice", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Add parameters for each setting to be saved
                        cmd.Parameters.AddWithValue("@advice_text", advice_text);
                        await cmd.ExecuteNonQueryAsync();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error saving all settings.");
                return false;
            }
        }

        // Method to get all settings from the database (assuming a single-row settings table)
        public async Task<Settings> GetAllSettingsAsync()
        {
            Settings settings = new Settings();

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_getsettings", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Populate settings object with values from the single row
                                settings.DailyAdvice = reader.GetString("daily_advice");
                                settings.PreviousDailyAdvice = reader.GetString("previous_daily_advice");
                                settings.LastMaintenanceRun = reader.IsDBNull("last_maintenance_run")
                                    ? DateTime.MinValue : reader.GetDateTime("last_maintenance_run");
                                // Add more settings as needed
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error retrieving all settings.");
                return null;
            }

            return settings;
        }

        // Method to save all settings to the database (assuming a single-row settings table)
        public async Task<ResponseWrapper<bool>> SaveAllSettingsAsync(Settings settings)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_savesettings", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Add parameters for each setting to be saved
                        cmd.Parameters.AddWithValue("@daily_advice", settings.DailyAdvice);
                        cmd.Parameters.AddWithValue("@last_maintenance_run", settings.LastMaintenanceRun);
                        cmd.Parameters.AddWithValue("@previous_daily_advice", settings.PreviousDailyAdvice);
                        // Add more settings as needed

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error saving all settings.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error saving all settings.");
            }
        }

        public async Task<ResponseWrapper<bool>> RunDatabaseMaintenance()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_maintenance", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in database maintenance.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error in database maintenance.");
            }

            return ResponseWrapper<bool>.Success(true);
        }


        public bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var mailAddress = new MailAddress(email);
                return mailAddress.Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }


        // Returns verification_code if successful
        public async Task<ResponseWrapper<string>> GuaranteeUserAsync(string loginToken, string email, byte[] imageData)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<string>.Fail(100000, "Login token expired.");

            email = email.Trim();
            if (!IsValidEmail(email)) return ResponseWrapper<string>.Fail(50000, "Not a valid email address.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Start a transaction
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var cmd = new SqlCommand("[dbo].[cn_guarantee]", conn, transaction))
                            {
                                cmd.CommandType = CommandType.StoredProcedure;

                                // Set up parameters
                                cmd.Parameters.AddWithValue("@login_token", loginToken);
                                cmd.Parameters.AddWithValue("@email", email);

                                // Execute the command and process the results
                                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                                {
                                    if (!await reader.ReadAsync())
                                    {
                                        transaction.Rollback();
                                        return ResponseWrapper<string>.Fail(50000, "Could not read the cn_guarantee result set");
                                    }

                                    Guid imageGuid = reader.GetGuid(reader.GetOrdinal("image_guid"));
                                    string verificationCode = reader.IsDBNull(reader.GetOrdinal("verification_code"))
                                        ? null
                                        : reader.GetGuid(reader.GetOrdinal("verification_code")).ToString();

                                    // Close the reader before proceeding
                                    reader.Close();

                                    // Store the blob into Azure
                                    bool uploadResult = await UploadBlobToAzureAsync(imageGuid, imageData);
                                    if (!uploadResult)
                                    {
                                        // Rollback the transaction if blob upload fails
                                        transaction.Rollback();
                                        return ResponseWrapper<string>.Fail(200001, "Could not upload BLOB to Azure");
                                    }

                                    // Commit the transaction
                                    transaction.Commit();

                                    return ResponseWrapper<string>.Success(verificationCode);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            ErrorLog.LogErrorException(ex, "Transaction rolled back due to an error.");
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<string>.Fail(100000, "Login token expired.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<string>.Fail(ex.HResult, "Unexpected error.");
            }
        }

        internal async Task<ResponseWrapper<bool>> GuaranteeConfirm(string verificationCode)
        {
            if (string.IsNullOrWhiteSpace(verificationCode))
                return ResponseWrapper<bool>.Fail(50001, "Invalid or expired verification code.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_guarantee_confirm", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@verification_code", verificationCode);
                        try
                        {
                            await cmd.ExecuteNonQueryAsync();
                            return ResponseWrapper<bool>.Success(true);
                        }
                        catch (SqlException ex) when (ex.Number == 50001)
                        {
                            // The stored procedure doesn't actually return whether the verification code was valid or not
                            // This is to ensure that malicious users can't phish for valid verification codes
                            return ResponseWrapper<bool>.Fail(50001, "Invalid or expired verification code.");
                        }
                        catch (SqlException ex)
                        {
                            ErrorLog.LogErrorException(ex);
                            return ResponseWrapper<bool>.Fail(ex.Number, "Unknown SQL Error Occurred");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<bool>.Fail(ex.HResult, "Unknown OTHER Error Occurred");
            }
        }

        // Build a new Database Structure for the Guarantee Links
        internal async Task<ResponseWrapper<string>> GuaranteeEmailAsync(string loginToken, string email)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<string>.Fail(100000, "Login token expired.");

            string verificationCode = null;
            email = email.Trim();
            if (!IsValidEmail(email)) return ResponseWrapper<string>.Fail(50000, "Not a valid email address.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_guarantee_email", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Set up parameters
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@email", email);

                        object? result = await cmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value) verificationCode = ((Guid)result).ToString();
                        return ResponseWrapper<string>.Success(verificationCode);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<string>.Fail(100000, "Login token expired.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<string>.Fail(ex.HResult, "Unexpected error.");
            }
        }


        public async Task<ResponseWrapper<List<GuaranteedUser>>> GetGuaranteedUsersAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<GuaranteedUser>>.Fail(100000, "Login token expired.");

            try
            {
                var guaranteedUsers = new List<GuaranteedUser>();
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_guaranteed_users", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                // Read the exact image associated with each guarantee
                                guaranteedUsers.Add(new GuaranteedUser
                                {
                                    UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                                    Email = reader.GetString(reader.GetOrdinal("email")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    ImageId = reader.GetInt32(reader.GetOrdinal("image_id")),
                                    ImageGuid = reader.GetGuid(reader.GetOrdinal("image_guid")), // Specific image for the guarantee

                                    Rating = reader.IsDBNull("rating") ? 0 : reader.GetInt32(reader.GetOrdinal("rating")),
                                    Review = reader.IsDBNull("review") ? "" : reader.GetString(reader.GetOrdinal("review"))
                                });
                            }
                        }
                    }
                }
                return ResponseWrapper<List<GuaranteedUser>>.Success(guaranteedUsers);
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<List<GuaranteedUser>>.Fail(100000, "Login token expired.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching guaranteed users.");
                return ResponseWrapper<List<GuaranteedUser>>.Fail(ex.HResult, "Error fetching guaranteed users.");
            }
        }


        // TODO - see if I even use this and remove it if need be
        // This is currently used in the messages page, but I might need to revamp some stuff about that
        // I probably want to have a method to get user details which is publicly accessible and includes only
        // public fields, and public images, reviews, etc
        public async Task<ResponseWrapper<UserConversation>> StartUserConversationAsync(string loginToken, int userId)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<UserConversation>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_start_user_conversation", conn)) // Ensure the stored procedure exists
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@user_id", userId));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                UserConversation convo = new UserConversation();
                                convo.UserId = reader.GetInt32(reader.GetOrdinal("user_id"));
                                convo.Name = reader.GetString(reader.GetOrdinal("name"));
                                convo.Description = reader.GetString(reader.GetOrdinal("description"));
                                convo.Ranking = reader.GetInt32(reader.GetOrdinal("ranking"));
                                convo.Gender = reader.GetInt32(reader.GetOrdinal("gender"));
                                convo.Birthday = reader.IsDBNull(reader.GetOrdinal("bday")) ? (DateTime?)null : (DateTime)reader.GetDateTime(reader.GetOrdinal("bday"));

                                // Create and populate the Location object
                                convo.Location = new City
                                {
                                    Geonameid = reader.GetInt32(reader.GetOrdinal("geonameid")),
                                    ContinentCode = reader.IsDBNull(reader.GetOrdinal("continent_code")) ? string.Empty : reader.GetString(reader.GetOrdinal("continent_code")),
                                    CountryCode = reader.IsDBNull(reader.GetOrdinal("country_code")) ? string.Empty : reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.IsDBNull(reader.GetOrdinal("country_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("country_name")),
                                    Admin1Name = reader.IsDBNull(reader.GetOrdinal("admin1_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("admin1_name")),
                                    CityName = reader.IsDBNull(reader.GetOrdinal("city_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("city_name"))
                                };

                                convo.IgnoredByMe = reader.GetBoolean(reader.GetOrdinal("ignored_by_me"));
                                convo.IsIgnored = reader.GetBoolean(reader.GetOrdinal("is_ignored"));
                                convo.UnreadMessageCount = 0;
                                convo.NewestMessage = 0;

                                // Ensure the imagesJson string is not empty or improperly formatted before deserialization
                                string imagesJson = reader.IsDBNull(reader.GetOrdinal("images"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("images"));

                                convo.Photos = await ParseImages(imagesJson);

                                // Read the reviews Json string
                                string reviewsJson = reader.IsDBNull(reader.GetOrdinal("reviews"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("reviews"));

                                convo.Reviews = await ParseReviews(reviewsJson);

                                return ResponseWrapper<UserConversation>.Success(convo);
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<UserConversation>.Fail(100000, "Login token expired.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching user details.");
                return ResponseWrapper<UserConversation>.Fail(50000, "Unknown error");
            }
            return ResponseWrapper<UserConversation>.Fail(50000, "Unknown error");
        }



        private async Task<bool> UploadBlobToAzureAsync(Guid imageGuid, byte[] imageData)
        {
            try
            {
                string containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME");

                // Create a BlobServiceClient object
                BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING"));

                // Get a reference to the container
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                // Ensure that the container exists
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                // Get a reference to the blob
                BlobClient blobClient = containerClient.GetBlobClient($"{imageGuid}.jpg");

                // Upload the image data to the blob
                using (var memoryStream = new MemoryStream(imageData))
                {
                    // TODO - do i want to do this asynchronously??? what if an error happens??
                    await blobClient.UploadAsync(memoryStream, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error uploading blob to Azure");
                return false;
            }
        }

        public async Task<ResponseWrapper<object>> CheckVerificationCode(string verificationCode)
        {
            if (string.IsNullOrWhiteSpace(verificationCode))
                return ResponseWrapper<object>.Fail(50001, "Invalid or expired verification code.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_check_verification_code", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@verification_code", verificationCode);

                        try
                        {
                            await cmd.ExecuteNonQueryAsync();
                            return ResponseWrapper<object>.Success(null);
                        }
                        catch (SqlException ex) when (ex.Number == 50001)
                        {
                            return ResponseWrapper<object>.Fail(50001, "Invalid or expired verification code.");
                        }
                        catch (SqlException ex)
                        {
                            ErrorLog.LogErrorException(ex);
                            return ResponseWrapper<object>.Fail(ex.Number, "Unknown SQL Error Occurred");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<object>.Fail(ex.HResult, "Unknown OTHER Error Occurred");
            }
        }
        public async Task<ResponseWrapper<object>> ResetPasswordAsync(string verificationCode, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(verificationCode) || string.IsNullOrWhiteSpace(newPassword))
                return ResponseWrapper<object>.Fail(50001, "Invalid or expired verification code.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_reset_password", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@verification_code", verificationCode);
                        cmd.Parameters.AddWithValue("@new_password", newPassword);

                        try
                        {
                            await cmd.ExecuteNonQueryAsync();
                            return ResponseWrapper<object>.Success(null);
                        }
                        catch (SqlException ex) when (ex.Number == 50001)
                        {
                            return ResponseWrapper<object>.Fail(50001, "Invalid or expired verification code.");
                        }
                        catch (SqlException ex)
                        {
                            ErrorLog.LogErrorException(ex);
                            return ResponseWrapper<object>.Fail(ex.Number, "Unknown SQL Error Occurred");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex);
                return ResponseWrapper<object>.Fail(ex.HResult, "Unknown OTHER Error Occurred");
            }
        }


        public async Task<ResponseWrapper<List<UserImage>>> GetUserImagesAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<UserImage>>.Fail(100000, "Login token expired.");

            var images = new List<UserImage>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_user_images", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                images.Add(new UserImage
                                {
                                    ImageId = reader.GetInt32(reader.GetOrdinal("image_id")),
                                    ImageGuid = reader.GetGuid(reader.GetOrdinal("image_guid")),
                                    //GuarantorUserId = reader.GetInt32(reader.GetOrdinal("guarantor_user_id")),
                                    DateCreated = reader.GetDateTime(reader.GetOrdinal("date_created")),
                                    //Rating = reader.IsDBNull(reader.GetOrdinal("rating")) ? null : reader.GetInt32(reader.GetOrdinal("rating")),
                                    //Review = reader.IsDBNull(reader.GetOrdinal("review")) ? null : reader.GetString(reader.GetOrdinal("review")),
                                    ImageVisible = reader.GetBoolean(reader.GetOrdinal("image_visible")),
                                    //ReviewVisible = reader.GetBoolean(reader.GetOrdinal("review_visible"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<UserImage>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching user images.");
                return ResponseWrapper<List<UserImage>>.Fail(ex.HResult, "Error fetching user images.");
            }

            return ResponseWrapper<List<UserImage>>.Success(images);
        }


        public async Task<ResponseWrapper<List<UserConversation>>> GetUserConversationsAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<UserConversation>>.Fail(100000, "Login token expired.");

            var conversations = new List<UserConversation>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_user_conversations", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                UserConversation convo = new UserConversation();

                                convo.UserId = reader.GetInt32(reader.GetOrdinal("user_id"));
                                convo.Name = reader.GetString(reader.GetOrdinal("name"));
                                convo.Description = reader.GetString(reader.GetOrdinal("description"));
                                convo.Gender = reader.GetInt32(reader.GetOrdinal("gender"));
                                convo.Birthday = reader.IsDBNull(reader.GetOrdinal("bday")) ? (DateTime?)null : (DateTime)reader.GetDateTime(reader.GetOrdinal("bday"));
                                convo.Ranking = reader.GetInt32(reader.GetOrdinal("ranking"));
                                convo.UnreadMessageCount = reader.GetInt32(reader.GetOrdinal("unread_message_count"));
                                convo.NewestMessage = reader.GetInt32(reader.GetOrdinal("newest_message"));
                                convo.IsIgnored = reader.GetBoolean(reader.GetOrdinal("is_ignored"));
                                convo.IgnoredByMe = reader.GetBoolean(reader.GetOrdinal("ignored_by_me"));
                                
                                // Create and populate the Location object
                                convo.Location = new City
                                {
                                    Geonameid = reader.IsDBNull("geonameid") ? 0 : reader.GetInt32(reader.GetOrdinal("geonameid")),
                                    ContinentCode = reader.IsDBNull(reader.GetOrdinal("continent_code")) ? string.Empty : reader.GetString(reader.GetOrdinal("continent_code")),
                                    CountryCode = reader.IsDBNull(reader.GetOrdinal("country_code")) ? string.Empty : reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.IsDBNull(reader.GetOrdinal("country_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("country_name")),
                                    Admin1Name = reader.IsDBNull(reader.GetOrdinal("admin1_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("admin1_name")),
                                    CityName = reader.IsDBNull(reader.GetOrdinal("city_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("city_name"))
                                };

                                // Ensure the imagesJson string is not empty or improperly formatted before deserialization
                                string imagesJson = reader.IsDBNull(reader.GetOrdinal("images"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("images"));

                                convo.Photos = await ParseImages(imagesJson);

                                // Read the reviews Json string
                                string reviewsJson = reader.IsDBNull(reader.GetOrdinal("reviews"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("reviews"));

                                convo.Reviews = await ParseReviews(reviewsJson);

                                conversations.Add(convo);
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<UserConversation>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching user conversations.");
                return ResponseWrapper<List<UserConversation>>.Fail(ex.HResult, "Error fetching user conversation.");
            }

            return ResponseWrapper<List<UserConversation>>.Success(conversations);
        }

        private string StripHTML(string html)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html)) return "";

                // Include DOCTYPE in tags to remove
                string[] tagsToRemove = { "html", "head", "body", "meta", "script", "title", "!DOCTYPE" };

                // Remove all specified opening and closing tags
                foreach (var tag in tagsToRemove)
                {
                    html = Regex.Replace(html, $"<\\s*/?\\s*{tag}[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                }

                return html.Trim();
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error stripping HTML.");
                return html;
            }
        }

        public async Task<ResponseWrapper<List<UserMessage>>> GetMessagesWithUserAsync(string loginToken, int userId)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<UserMessage>>.Fail(100000, "Login token expired.");

            var messages = new List<UserMessage>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_messages_with_user", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@user_id", userId));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                messages.Add(new UserMessage
                                {
                                    MessageId = reader.GetInt32(reader.GetOrdinal("message_id")),
                                    FromUserId = reader.GetInt32(reader.GetOrdinal("from_user_id")),
                                    ToUserId = reader.GetInt32(reader.GetOrdinal("to_user_id")),
                                    MessageText = reader.GetString(reader.GetOrdinal("message_text")),
                                    IsRead = reader.GetBoolean(reader.GetOrdinal("isread")),
                                    DateCreated = reader.GetDateTime(reader.GetOrdinal("date_created")),
                                    FromUserName = reader.GetString(reader.GetOrdinal("from_user_name")),
                                    ToUserName = reader.GetString(reader.GetOrdinal("to_user_name")),
                                    IsCompanioNitaAdvice = reader.GetBoolean(reader.GetOrdinal("companionita"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<UserMessage>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching messages with user.");
                return ResponseWrapper<List<UserMessage>>.Fail(ex.HResult, "Error fetcing messages with user.");
            }

            return ResponseWrapper<List<UserMessage>>.Success(messages);
        }

        public async Task<string> GetRecentMessages()
        {
            string messages = "";
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_recent_messages", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string name_from = reader.GetString(reader.GetOrdinal("name_from"));
                                string name_to = reader.GetString(reader.GetOrdinal("name_to"));
                                string message_text = reader.GetString(reader.GetOrdinal("message_text"));
                                DateTime date_created = reader.GetDateTime(reader.GetOrdinal("date_created"));
                                messages = date_created.ToString("D") + " (" + name_from + " > " + name_to + ") " + message_text + "\n" + messages;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error sending message.");
                return "";
            }
            return messages;
        }

        public async Task<ResponseWrapper<SendMessageResult>> SendMessageAsync(string loginToken, int userId, string messageText, bool isCompanioNita = false)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<SendMessageResult>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_send_message", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@user_id", userId));
                        cmd.Parameters.Add(new SqlParameter("@message_text", messageText));
                        cmd.Parameters.Add(new SqlParameter("@is_companionita", isCompanioNita));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync()) return ResponseWrapper<SendMessageResult>.Fail(50000, "database error");

                            SendMessageResult msg = new SendMessageResult();
                            // Assign just the parameters that we need
                            msg.LoginToken = loginToken;
                            msg.ToUserId = userId;
                            msg.MessageText = messageText;
                            msg.MessageId = reader.GetInt32(reader.GetOrdinal("message_id"));
                            msg.FromUserName = reader.GetString(reader.GetOrdinal("name"));
                            msg.FromUserId = reader.GetInt32(reader.GetOrdinal("user_id"));
                            msg.IsCompanioNitaAdvice = reader.GetBoolean(reader.GetOrdinal("companionita"));
                            msg.IgnoredSince = reader.IsDBNull(reader.GetOrdinal("ignored_since")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("ignored_since"));
                            msg.PushToken = reader.GetString("push_token");
                            return ResponseWrapper<SendMessageResult>.Success(msg);
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<SendMessageResult>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error sending message.");
                return ResponseWrapper<SendMessageResult>.Fail(ex.HResult, "Error sending message.");
            }
        }

        public async Task<ResponseWrapper<bool>> SaveAdvice(string loginToken, string prompt, string advice)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_add_advice", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@advice", advice));
                        cmd.Parameters.Add(new SqlParameter("@prompt", prompt));

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error sending message.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error sending message.");
            }
        }


        public async Task<ResponseWrapper<List<Advice>>> GetAdvice(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<Advice>>.Fail(100000, "Login token expired.");

            List<Advice> adviceList = new List<Advice>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_advice", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                Advice advice = new Advice();
                                advice.Prompt = reader.GetString(reader.GetOrdinal("prompt"));
                                advice.Response = reader.GetString(reader.GetOrdinal("advice"));
                                adviceList.Add(advice);
                            }
                        }
                        return ResponseWrapper<List<Advice>>.Success(adviceList);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<Advice>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error sending message.");
                return ResponseWrapper<List<Advice>>.Fail(ex.HResult, "Error sending message.");
            }
        }

        public async Task<ResponseWrapper<bool>> AddIgnore(string loginToken, int userId)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_add_ignore", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@user_id_to_ignore", userId));
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error adding ignore.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error adding ignore.");
            }

            return ResponseWrapper<bool>.Success(true);            
        }
        public async Task<ResponseWrapper<bool>> RemoveIgnore(string loginToken, int userId)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_remove_ignore", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@user_id_to_ignore", userId));
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error removing ignore.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error removing ignore.");
            }

            return ResponseWrapper<bool>.Success(true);
        }


        public async Task<ResponseWrapper<List<UserMessage>>> GetIgnoredMessagesAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<UserMessage>>.Fail(100000, "Login token expired.");

            var ignoredMessages = new List<UserMessage>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_ignored_messages", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken)); // Correctly pass the login token

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                ignoredMessages.Add(new UserMessage
                                {
                                    MessageId = reader.GetInt32(reader.GetOrdinal("message_id")),
                                    FromUserId = reader.GetInt32(reader.GetOrdinal("from_user_id")),
                                    ToUserId = reader.GetInt32(reader.GetOrdinal("to_user_id")),
                                    MessageText = reader.GetString(reader.GetOrdinal("message_text")),
                                    IsRead = reader.GetBoolean(reader.GetOrdinal("isread")),
                                    DateCreated = reader.GetDateTime(reader.GetOrdinal("date_created"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<UserMessage>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching ignored messages.");
                return ResponseWrapper<List<UserMessage>>.Fail(ex.HResult, "Error fetching ignored messages.");
            }

            return ResponseWrapper<List<UserMessage>>.Success(ignoredMessages);
        }



        private async Task<List<Guid>> ParseImages(string imagesJson)
        {
            if (!string.IsNullOrWhiteSpace(imagesJson))
            {
                try
                {
                    // Attempt to deserialize only if the string contains valid JSON data
                    var imageList = JsonSerializer.Deserialize<List<ImageItem>>(imagesJson);
                    return imageList?.Select(i => i.image_guid).ToList() ?? new List<Guid>();
                }
                catch (JsonException jsonEx)
                {
                    // Log the deserialization error and the offending JSON content for debugging
                    ErrorLog.LogErrorException(jsonEx, $"Failed to parse image GUIDs for user. Raw JSON: '{imagesJson}'");
                    return new List<Guid>(); // Assign an empty list if parsing fails
                }
            }
            else
            {
                // Handle cases where imagesJson is empty or contains invalid data
                return new List<Guid>();
            }
        }

        private async Task<List<Review>> ParseReviews(string reviewsJson)
        {
            if (!string.IsNullOrWhiteSpace(reviewsJson))
            {
                try
                {
                    // Attempt to deserialize only if the string contains valid JSON data
                    var reviewList = JsonSerializer.Deserialize<List<ReviewItem>>(reviewsJson);
                    return reviewList?.Select(r => new Review() { Text = r.review, Date = r.date_created }).ToList() ?? new List<Review>();
                }
                catch (JsonException jsonEx)
                {
                    // Log the deserialization error and the offending JSON content for debugging
                    ErrorLog.LogErrorException(jsonEx, $"Failed to parse reviews for user. Raw JSON: '{reviewsJson}'");
                    return new List<Review>(); // Assign an empty list if parsing fails
                }
            }
            else
            {
                // Handle cases where reviewsJson is empty or contains invalid data
                return new List<Review>();
            }
        }
        public async Task<ResponseWrapper<List<Companion>>> FindCompanionsAsync(string loginToken, bool cisMale, bool cisFemale, bool other, bool transMale, bool transFemale, List<int> cities, int ageMin, int ageMax, bool showIgnoredUsers)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<Companion>>.Fail(100000, "Login token expired.");

            // Make sure the age is within the limits, so that the sql doesn't error out with overflow mainly
            if (ageMin < 18) ageMin = 18;
            if (ageMin > 100) ageMin = 100;
            if (ageMax < 18) ageMax = 18;
            if (ageMax > 100) ageMax = 100;

            var companions = new List<Companion>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_find_companion", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Create and add the TVP parameter for cities
                        var citiesTable = new DataTable();
                        citiesTable.Columns.Add("geonameid", typeof(int));
                        foreach (var id in cities)
                        {
                            citiesTable.Rows.Add(id);
                        }
                        var citiesParam = new SqlParameter("@cities", SqlDbType.Structured)
                        {
                            TypeName = "dbo.cn_cities_type",
                            Value = citiesTable
                        };
                        cmd.Parameters.Add(citiesParam);

                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@cismale", cisMale));
                        cmd.Parameters.Add(new SqlParameter("@cisfemale", cisFemale));
                        cmd.Parameters.Add(new SqlParameter("@other", other));
                        cmd.Parameters.Add(new SqlParameter("@transmale", transMale));
                        cmd.Parameters.Add(new SqlParameter("@transfemale", transFemale));
                        cmd.Parameters.Add(new SqlParameter("@agemin", ageMin));
                        cmd.Parameters.Add(new SqlParameter("@agemax", ageMax));
                        cmd.Parameters.Add(new SqlParameter("@include_ignored_users", showIgnoredUsers));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var companion = new Companion
                                {
                                    UserId = reader.GetInt32(reader.GetOrdinal("user_id")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    Gender = reader.GetInt32(reader.GetOrdinal("gender")),
                                    Birthday = reader.IsDBNull(reader.GetOrdinal("bday")) ? (DateTime?)null : (DateTime)reader.GetDateTime(reader.GetOrdinal("bday")), // Handle nulls here
                                    Description = reader.GetString(reader.GetOrdinal("description")),
                                    Ranking = reader.GetInt32(reader.GetOrdinal("ranking")),
                                    IsIgnored = reader.GetBoolean(reader.GetOrdinal("is_ignored")),
                                    CityDisplayName = reader.GetString("city_name") + ", " + reader.GetString("admin1_name") + ", " + reader.GetString("country_name"),
                                };

                                // Ensure the imagesJson string is not empty or improperly formatted before deserialization
                                string imagesJson = reader.IsDBNull(reader.GetOrdinal("images"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("images"));
                                
                                companion.Images = await ParseImages(imagesJson);

                                // Read the reviews Json string
                                string reviewsJson = reader.IsDBNull(reader.GetOrdinal("reviews"))
                                    ? string.Empty
                                    : reader.GetString(reader.GetOrdinal("reviews"));

                                companion.Reviews = await ParseReviews(reviewsJson);

                                companions.Add(companion);
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<Companion>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching companions");
                return ResponseWrapper<List<Companion>>.Fail(ex.HResult, "Error fetching companions");
            }

            return ResponseWrapper<List<Companion>>.Success(companions);
        }

        // Helper class to map the JSON structure
        private class ImageItem
        {
            public Guid image_guid { get; set; }
        }

        private class ReviewItem
        {
            public string review { get; set; }
            public DateTime date_created { get; set; }
        }

        public async Task<string> GenerateNewVerificationCodeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "";

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_generate_verification_code", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@Email", email);

                        // Execute the command and get the new verification code
                        var verificationCode = (string)await cmd.ExecuteScalarAsync();

                        // If no verification code is returned, handle it internally without leaking info
                        if (string.IsNullOrWhiteSpace(verificationCode))
                        {
                            throw new Exception("Failed to generate a new verification code.");
                        }

                        return verificationCode;
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 50001)
            {
                // Email not found in the system
                return "";
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error generating new verification code.");
                throw; // Ensure exceptions are logged but not leaked back to the client
            }
        }


        public async Task<ResponseWrapper<bool>> RemoveGuaranteeAsync(string loginToken, int imageId)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _)) 
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                Guid imageGuid;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_remove_guarantee", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@image_id", imageId));

                        // Add the output parameter for the image GUID
                        var outputParam = new SqlParameter("@image_guid", SqlDbType.UniqueIdentifier)
                        {
                            Direction = ParameterDirection.Output
                        };
                        cmd.Parameters.Add(outputParam);

                        await cmd.ExecuteNonQueryAsync();

                        // Retrieve the GUID from the output parameter
                        imageGuid = (Guid)outputParam.Value;
                    }
                }

                // Step 3: Delete the blob from Azure Blob Storage
                bool blobDeleted = await DeleteBlobFromAzureAsync(imageGuid);
                if (!blobDeleted)
                {
                    return ResponseWrapper<bool>.Fail(200002, "Failed to delete the image from Azure Blob Storage.");
                }

                return ResponseWrapper<bool>.Success(true);
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<bool>.Fail(100000, "Invalid Credentials");
            }
            catch (SqlException ex) when (ex.Number == 100001)
            {
                return ResponseWrapper<bool>.Fail(100001, "You do not have permission to remove this guarantee.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "General error during RemoveGuarantee.");
                return ResponseWrapper<bool>.Fail(50000, "An unexpected error occurred.");
            }
        }

        private async Task<bool> DeleteBlobFromAzureAsync(Guid imageGuid)
        {
            try
            {
                string containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME");
                    
                // Create a BlobServiceClient object
                BlobServiceClient blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING"));

                // Get a reference to the container
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                // Get a reference to the blob
                BlobClient blobClient = containerClient.GetBlobClient($"{imageGuid}.jpg");

                // Delete the blob
                await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
                return true;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error deleting blob from Azure");
                return false;
            }
        }


        public async Task<ResponseWrapper<bool>> UpdateUserDetailsAsync(string loginToken, UserDetails userDetails)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                if (userDetails.Name.Length > 15)
                {
                    userDetails.Name = userDetails.Name.Substring(0, 15);
                }
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_update_user_info", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@name", userDetails.Name);
                        cmd.Parameters.AddWithValue("@description", userDetails.Description);
                        cmd.Parameters.AddWithValue("@searchable", userDetails.Searchable);
                        cmd.Parameters.AddWithValue("@gender", userDetails.Gender);
                        cmd.Parameters.AddWithValue("@geonameid", userDetails.Geonameid == 0 ? DBNull.Value : userDetails.Geonameid);
                        cmd.Parameters.AddWithValue("@dob", (object?)userDetails.DateOfBirth ?? DBNull.Value);


                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error updating user details.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error updating user details.");
            }
        }



        // Method to update image visibility
        public async Task<ResponseWrapper<bool>> UpdateImageVisibilityAsync(string loginToken, int imageId, bool isVisible)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_update_image_visibility", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@image_id", imageId);
                        cmd.Parameters.AddWithValue("@is_visible", isVisible);

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true);
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error updating image visibility.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error updating image visibility.");
            }
        }


        public async Task<ResponseWrapper<bool>> UpdateReviewVisibilityAsync(string loginToken, int imageId, bool isPublic)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_update_review_visibility", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@image_id", imageId);
                        cmd.Parameters.AddWithValue("@is_public", isPublic);

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true, "Visibility updated successfully.");
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (SqlException ex)
            {
                ErrorLog.LogErrorException(ex, "Error updating review visibility.");
                return ResponseWrapper<bool>.Fail(ex.Number, "Failed to update visibility.");
            }
        }



        public async Task<ResponseWrapper<bool>> UpdateImageReviewAsync(string loginToken, int imageId, int rating, string review)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_update_image_review", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@image_id", imageId);
                        cmd.Parameters.AddWithValue("@rating", rating);
                        cmd.Parameters.AddWithValue("@review", review);

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true, "Review updated successfully.");
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (SqlException ex)
            {
                ErrorLog.LogErrorException(ex, "Error updating image review.");
                return ResponseWrapper<bool>.Fail(ex.Number, "Failed to update review.");
            }
        }

        public async Task<ResponseWrapper<bool>> UpdatePushTokenAsync(string loginToken, string pushToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");

            if (pushToken == null) pushToken = "";
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_update_push_token", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        // Add parameters
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@push_token", pushToken);

                        await cmd.ExecuteNonQueryAsync();
                        return ResponseWrapper<bool>.Success(true, "Push token updated successfully.");
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid login token error
                return ResponseWrapper<bool>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                // Log the error and return a failure response
                ErrorLog.LogErrorException(ex, "Error updating push token.");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Failed to update push token.");
            }
        }



        public async Task<ResponseWrapper<List<Country>>> GetCountriesAsync(string continent)
        {
            var countries = new List<Country>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_countries", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@continent", continent));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                countries.Add(new Country
                                {
                                    CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.GetString(reader.GetOrdinal("country_name"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<Country>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching countries.");
                return ResponseWrapper<List<Country>>.Fail(ex.HResult, "Error fetching countries.");
            }

            return ResponseWrapper<List<Country>>.Success(countries);
        }

        public async Task<ResponseWrapper<List<City>>> GetCitiesAsync(string country, string searchTerm)
        {
            var cities = new List<City>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_cities", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@country", country));
                        cmd.Parameters.Add(new SqlParameter("@search_term", searchTerm));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                cities.Add(new City
                                {
                                    Geonameid = reader.GetInt32(reader.GetOrdinal("geonameid")),
                                    ContinentCode = reader.GetString(reader.GetOrdinal("continent_code")),
                                    CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.GetString("country_name"),
                                    Admin1Name = reader.GetString(reader.GetOrdinal("admin1_name")),
                                    CityName = reader.GetString(reader.GetOrdinal("city_name"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<City>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching cities.");
                return ResponseWrapper<List<City>>.Fail(ex.HResult, "Error fetching cities.");
            }

            return ResponseWrapper<List<City>>.Success(cities);
        }
        public async Task<ResponseWrapper<List<City>>> GetNearbyCitiesAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<List<City>>.Fail(100000, "Login token expired.");

            var cities = new List<City>();
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_get_nearby_cities", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                cities.Add(new City
                                {
                                    Geonameid = reader.GetInt32(reader.GetOrdinal("geonameid")),
                                    ContinentCode = reader.GetString(reader.GetOrdinal("continent_code")),
                                    CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.GetString("country_name"),
                                    Admin1Name = reader.GetString(reader.GetOrdinal("admin1_name")),
                                    CityName = reader.GetString(reader.GetOrdinal("city_name"))
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                // Handle invalid credentials error / expired login token
                return ResponseWrapper<List<City>>.Fail(100000, "Invalid or expired login token.");
            }
            catch (Exception ex)
            {
                // Log the error asynchronously so that it doesn't bog down the server
                ErrorLog.LogErrorException(ex, "Error fetching searchable cities.");
                return ResponseWrapper<List<City>>.Fail(ex.HResult, "An unexpected error occurred while fetching searchable cities.");
            }

            return ResponseWrapper<List<City>>.Success(cities);
        }

        internal async Task<ResponseWrapper<City>> GetCityAsync(string loginToken, int geonameid)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<City>.Fail(100000, "Login token expired.");

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("cn_get_city", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@login_token", loginToken));
                        cmd.Parameters.Add(new SqlParameter("@geonameid", geonameid));
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return ResponseWrapper<City>.Success(new City
                                {
                                    Geonameid = reader.GetInt32(reader.GetOrdinal("geonameid")),
                                    ContinentCode = reader.GetString(reader.GetOrdinal("continent_code")),
                                    CountryCode = reader.GetString(reader.GetOrdinal("country_code")),
                                    CountryName = reader.GetString("country_name"),
                                    Admin1Name = reader.GetString(reader.GetOrdinal("admin1_name")),
                                    CityName = reader.GetString(reader.GetOrdinal("city_name"))
                                });
                            }
                            else return ResponseWrapper<City>.Fail(50002, "City not found.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error fetching city.");
                return ResponseWrapper<City>.Fail(ex.HResult, "Error fetching city.");
            }
        }



        // Returns (bool emailExists, bool oauthRequired)
        internal async Task<CheckEmailResult> CheckEmailExistsAsync(string email)
        {
            CheckEmailResult emailResult = new CheckEmailResult();
            emailResult.emailExists = false;
            emailResult.oauthRequired = false;

            email = email.Trim();
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email)) return emailResult;

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_check_email_exists", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@email", email);

                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null)
                        {
                            emailResult.emailExists = true;
                            emailResult.oauthRequired = (bool)result;
                        }
                        return emailResult;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error checking if email exists.");
                throw;
            }
        }

        // Returns validation code if successful
        internal async Task<string> CreateNewUserAsync(string email, string password, string ipAddress, bool oauthLogin = false)
        {
            email = email.Trim();
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email)) return null;
            if (string.IsNullOrWhiteSpace(password)) return null;

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_create_new_user", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@password", password);
                        cmd.Parameters.AddWithValue("@ip_address", ipAddress);
                        cmd.Parameters.AddWithValue("@oauth_login", oauthLogin);

                        string validation_code = ((Guid)await cmd.ExecuteScalarAsync()).ToString();
                        return validation_code;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error creating new user.");
                throw;
            }
        }

        public async Task<ResponseWrapper<Guid>> UploadPhotoAsync(string loginToken, byte[] imageData, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<Guid>.Fail(100000, "Login token expired.");

            if (imageData == null || imageData.Length == 0)
                return ResponseWrapper<Guid>.Fail(50000, "Invalid image data.");

            try
            {
                Guid imageGuid = Guid.NewGuid();

                // Upload the image to Azure Blob Storage
                bool uploadResult = await UploadBlobToAzureAsync(imageGuid, imageData);
                if (!uploadResult)
                {
                    return ResponseWrapper<Guid>.Fail(200001, "Could not upload BLOB to Azure");
                }

                // Save the image details to the database
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_save_image", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@login_token", loginToken);
                        cmd.Parameters.AddWithValue("@image_guid", imageGuid);
                        cmd.Parameters.AddWithValue("@ip_address", ipAddress);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                return ResponseWrapper<Guid>.Success(imageGuid);
            }
            catch (SqlException ex) when (ex.Number == 100000)
            {
                return ResponseWrapper<Guid>.Fail(100000, "Login token expired.");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error uploading photo.");
                return ResponseWrapper<Guid>.Fail(ex.HResult, "Unexpected error.");
            }
        }

        // =============================================
        // Subscription Management Methods
        // =============================================

        /// <summary>
        /// Update subscription expiry by email (called from Services project via Stripe webhook).
        /// </summary>
        public async Task<ResponseWrapper<bool>> UpdateSubscriptionExpiryByEmailAsync(string email, DateTime expiryDate)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_update_subscription_expiry_by_email", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@expiry_date", expiryDate);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int rowsAffected = reader.GetInt32(0);
                                if (rowsAffected > 0)
                                {
                                    return ResponseWrapper<bool>.Success(true);
                                }
                                else
                                {
                                    return ResponseWrapper<bool>.Fail(50000, "No user found with that email.");
                                }
                            }
                        }
                    }
                }
                return ResponseWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, $"Error updating subscription expiry for email {email}");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error updating subscription expiry.");
            }
        }

        /// <summary>
        /// Update subscription expiry by user ID.
        /// </summary>
        public async Task<ResponseWrapper<bool>> UpdateSubscriptionExpiryByUserIdAsync(int userId, DateTime expiryDate)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_update_subscription_expiry_by_userid", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@user_id", userId);
                        cmd.Parameters.AddWithValue("@expiry_date", expiryDate);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int rowsAffected = reader.GetInt32(0);
                                if (rowsAffected > 0)
                                {
                                    return ResponseWrapper<bool>.Success(true);
                                }
                                else
                                {
                                    return ResponseWrapper<bool>.Fail(50000, "No user found with that ID.");
                                }
                            }
                        }
                    }
                }
                return ResponseWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, $"Error updating subscription expiry for user {userId}");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error updating subscription expiry.");
            }
        }

        /// <summary>
        /// Cancel subscription by email (clears expiry date).
        /// </summary>
        public async Task<ResponseWrapper<bool>> CancelSubscriptionByEmailAsync(string email)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_cancel_subscription_by_email", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@email", email);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                int rowsAffected = reader.GetInt32(0);
                                if (rowsAffected > 0)
                                {
                                    return ResponseWrapper<bool>.Success(true);
                                }
                            }
                        }
                    }
                }
                return ResponseWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, $"Error cancelling subscription for email {email}");
                return ResponseWrapper<bool>.Fail(ex.HResult, "Error cancelling subscription.");
            }
        }

        /// <summary>
        /// Check if a user has an active subscription by user ID.
        /// </summary>
        public async Task<bool> HasActiveSubscriptionAsync(int userId)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_check_subscription_by_userid", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@user_id", userId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return reader.GetInt32(0) == 1;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, $"Error checking subscription for user {userId}");
                return false;
            }
        }

        /// <summary>
        /// Check if an email has an active subscription.
        /// </summary>
        public async Task<bool> HasActiveSubscriptionByEmailAsync(string email)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new SqlCommand("cn_check_subscription_by_email", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@email", email);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return reader.GetInt32(0) == 1;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, $"Error checking subscription for email {email}");
                return false;
            }
        }
    }
}

