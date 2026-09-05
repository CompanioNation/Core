using CompanioNation.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;


using System.Security.Cryptography;
using System.Text;
using System.Net;

namespace CompanioNationAPI
{


    public class CompanioNationHub : Hub
    {
        private readonly Database _database;
        private readonly CompanioNita _companioNita;
        private readonly MaintenanceEventService _maintenanceEventService;
        private readonly IPushService _pushService;

        public CompanioNationHub(Database database, CompanioNita companioNita, MaintenanceEventService maintenanceEventService, IPushService pushService)
        {
            _database = database;
            _companioNita = companioNita;
            _maintenanceEventService = maintenanceEventService;
            _pushService = pushService;
        }

        // ──── Rate limiting (shared across all hub instances) ────
        // Implemented in LoginRateLimiter so the REST auth endpoints apply the
        // exact same sliding-window rules as the hub, and so the shared
        // dictionaries are pruned of expired IP entries.
        private static bool IsLoginRateLimited(string ip)
            => LoginRateLimiter.IsLoginRateLimited(ip);

        private static bool IsUnauthRateLimited(string ip)
            => LoginRateLimiter.IsUnauthRateLimited(ip);

        private static bool IsSignupRateLimited(string ip)
            => LoginRateLimiter.IsSignupRateLimited(ip);

        private async Task SetSignalRGroupId(int userId)
        {
            // I'm using SignalR groups because sending to specific User functionality wasn't working
            // I didn't figure out precisely why this is, but I assume it has to do with the fact that I
            //   ripped out all of the user authentication stuff because i'm using custom db users
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.ToString());
        }

        /// <summary>
        /// Returns a failure response when the caller's account is not yet email-verified.
        /// Unverified accounts may only set up their profile (UpdateUserDetails, UploadPhoto,
        /// photo management) and complete the verification flow. Returns null when the caller
        /// may proceed.
        /// </summary>
        private async Task<ResponseWrapper<bool>?> CheckVerifiedAsync(string loginToken)
        {
            if (string.IsNullOrWhiteSpace(loginToken) || !Guid.TryParse(loginToken, out _))
                return ResponseWrapper<bool>.Fail(ErrorCodes.InvalidCredentials, "Login token expired.");

            ResponseWrapper<UserDetails> user = await _database.GetUserAsync(loginToken);
            if (!user.IsSuccess)
                return ResponseWrapper<bool>.Fail(user.ErrorCode, user.Message);

            if (!user.Data.Verified)
                return ResponseWrapper<bool>.Fail(ErrorCodes.EmailNotVerified, "Please verify your email address before continuing.");

            return null;
        }
        /// <summary>
        /// Returns the client IP from the current HTTP context.
        /// NOTE: When the app runs behind a reverse proxy (Azure App Service, IIS ARR, nginx,
        /// Cloudflare, etc.), <c>RemoteIpAddress</c> returns the proxy's IP unless
        /// Forwarded Headers Middleware is configured. Without it every user behind the same
        /// proxy shares a single rate-limit bucket. Enable <c>UseForwardedHeaders</c> with
        /// trusted proxy ranges in production if this is an issue.
        /// </summary>
        private string GetClientIpAddress()
        {
            return Context.GetHttpContext()?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Sends a welcome email to newly created OAuth users (Apple/Google Sign In).
        /// Detects new users by checking if DateCreated is within the last 60 seconds.
        /// </summary>
        private static void SendOAuthWelcomeEmailIfNew(UserDetails details)
        {
            if (details == null || string.IsNullOrWhiteSpace(details.Email)) return;

            // A user created within the last 60 seconds is considered new
            if ((DateTime.UtcNow - details.DateCreated).TotalSeconds > 60) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var assembly = typeof(CompanioNationHub).Assembly;

                    static string LoadTemplate(Assembly asm, string name)
                    {
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream == null) return "";
                        using var sr = new StreamReader(stream);
                        return sr.ReadToEnd();
                    }

                    var textBody = LoadTemplate(assembly, "CompanioNationAPI.EmailTemplates.WelcomeEmailOAuth.txt")
                        .Replace("{BaseUrl}", Util.SiteBaseUrl);
                    var htmlBody = LoadTemplate(assembly, "CompanioNationAPI.EmailTemplates.WelcomeEmailOAuth.html")
                        .Replace("{BaseUrl}", Util.SiteBaseUrl);

                    if (!string.IsNullOrWhiteSpace(textBody) || !string.IsNullOrWhiteSpace(htmlBody))
                    {
                        await Email.SendEmailAsync(details.Email, "Welcome to CompanioNation™!", textBody, htmlBody);
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.LogErrorException(ex, "Error sending OAuth welcome email.");
                }
            });
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithGoogle(LoginWithGoogleRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            try
            {
                // Validate the Google ID token and retrieve user details
                ResponseWrapper<UserDetails> result = await _database.LoginWithGoogleAsync(request.Code, request.CodeVerifier, request.RedirectUri, GetClientIpAddress(), _companioNita);

                if (result.IsSuccess)
                {
                    // Set the SignalR group ID for the user
                    await SetSignalRGroupId(result.Data.UserId);
                    SendOAuthWelcomeEmailIfNew(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithGoogle method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with Google.");
            }
        }
        public async Task<ResponseWrapper<UserDetails>> Login(LoginRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            ResponseWrapper<UserDetails> result = await _database.LoginAsync(request.Email, request.Password, GetClientIpAddress(), false);
            // At this point we know what the UserId is, so we should set the SignalR user id to be the same
            if (result.IsSuccess) 
            {
                await SetSignalRGroupId(result.Data.UserId);
            }

            return result;
        }

        public async Task<ResponseWrapper<bool>> AcceptTerms(AcceptTermsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.AcceptTermsAsync(request.LoginToken, request.Version);
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithApple(LoginWithAppleRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            try
            {
                ResponseWrapper<UserDetails> result = await _database.LoginWithAppleAsync(
                    request.Code, request.RedirectUri, request.FirstName, request.LastName, GetClientIpAddress(), _companioNita);

                if (result.IsSuccess)
                {
                    await SetSignalRGroupId(result.Data.UserId);
                    SendOAuthWelcomeEmailIfNew(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithApple method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with Apple.");
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithFacebook(LoginWithFacebookRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            try
            {
                ResponseWrapper<UserDetails> result = await _database.LoginWithFacebookAsync(request.Code, request.CodeVerifier, request.RedirectUri, GetClientIpAddress(), _companioNita);

                if (result.IsSuccess)
                {
                    await SetSignalRGroupId(result.Data.UserId);
                    SendOAuthWelcomeEmailIfNew(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithFacebook method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with Facebook.");
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithTwitter(LoginWithTwitterRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            try
            {
                ResponseWrapper<UserDetails> result = await _database.LoginWithTwitterAsync(request.Code, request.CodeVerifier, request.RedirectUri, GetClientIpAddress(), _companioNita);

                if (result.IsSuccess)
                {
                    await SetSignalRGroupId(result.Data.UserId);
                    SendOAuthWelcomeEmailIfNew(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithTwitter method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with X.");
            }
        }

        public async Task<ResponseWrapper<UserDetails>> LoginWithMicrosoft(LoginWithMicrosoftRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsLoginRateLimited(ip))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.RateLimited, "Too many login attempts. Please try again in a minute.");

            try
            {
                ResponseWrapper<UserDetails> result = await _database.LoginWithMicrosoftAsync(request.Code, request.CodeVerifier, request.RedirectUri, GetClientIpAddress(), _companioNita);

                if (result.IsSuccess)
                {
                    await SetSignalRGroupId(result.Data.UserId);
                    SendOAuthWelcomeEmailIfNew(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LoginWithMicrosoft method.");
                return ResponseWrapper<UserDetails>.Fail(50000, "An unexpected error occurred while logging in with Microsoft.");
            }
        }

        public async Task<ResponseWrapper<OAuthConfig>> GetOAuthConfig(GetOAuthConfigRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<OAuthConfig>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                var config = new OAuthConfig
                {
                    GoogleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty,
                    AppleServiceId = Environment.GetEnvironmentVariable("APPLE_SERVICE_ID") ?? string.Empty,
                    FacebookAppId = Environment.GetEnvironmentVariable("FACEBOOK_APP_ID") ?? string.Empty,
                    TwitterClientId = Environment.GetEnvironmentVariable("TWITTER_CLIENT_ID") ?? string.Empty,
                    MicrosoftClientId = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID") ?? string.Empty
                };
                return ResponseWrapper<OAuthConfig>.Success(config);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetOAuthConfig method.");
                return ResponseWrapper<OAuthConfig>.Fail(50000, "Failed to retrieve OAuth configuration.");
            }
        }

        public async Task<ResponseWrapper<ConnectResult>> Connect(ConnectRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<ConnectResult>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                string loginToken = request?.LoginToken ?? string.Empty;

                // Directly fetching the login result from the stored procedure
                ConnectResult result = new ConnectResult();
                result.PhotosBaseUrl = Environment.GetEnvironmentVariable("COMPANIONATION_PHOTO_BASE_URL") ?? string.Empty;

                ResponseWrapper<UserDetails> userDetails = null;
                if (!string.IsNullOrWhiteSpace(loginToken))
                {
                    userDetails = await _database.GetUserAsync(loginToken);
                    // At this point we know what the UserId is, so we should set the SignalR user id to be the same
                    if (userDetails.IsSuccess)
                    {
                        await SetSignalRGroupId(userDetails.Data.UserId);
                    }
                }
                result.CurrentUser = userDetails;
                return ResponseWrapper<ConnectResult>.Success(result);
            } catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in Connect method.");
                return ResponseWrapper<ConnectResult>.Fail(50000, "Unknown error occurred while connecting");
            }
        }

        // First-class SOFT result: an older-but-still-DTO client gets this instead of a
        // SignalR dispatch error. The client shows the update prompt and never logs it.
        private const string ClientUpgradeRequiredMessage =
            "A new version of CompanioNation is available. Please refresh to continue.";

        private static bool RequiresUpgrade(HubRequest? request) =>
            ClientVersion.IsOlderThan(request?.ClientVersion, HubContract.MinimumClientVersion);

        // No version guard here: this is the probe the client uses to DISCOVER versions.
        public async Task<string> GetCurrentVersion(GetCurrentVersionRequest request)
        {
            return Util.GetCurrentVersion();
        }

        public async Task LogError(LogErrorRequest request)
        {
            try
            {
                // The request DTO carries ClientVersion (formerly the schema tag). Arity is
                // always one, so old clients bind cleanly; version-guard them here instead.
                if (request == null || RequiresUpgrade(request))
                    return;

                // Client-supplied log content is untrusted input: a stale or hostile client can
                // invoke this in a tight loop, so every submission passes through ClientLogGate
                // before reaching the shared error pipeline (and its admin email budget).
                string safeMessage = request.Message is null ? string.Empty
                    : request.Message.Length <= ClientLogGate.MaxPayloadLength ? request.Message : request.Message[..ClientLogGate.MaxPayloadLength];
                string safeVersion = request.Version is null ? string.Empty
                    : request.Version.Length <= 64 ? request.Version : request.Version[..64];

                ClientLogGate.Decision decision = ClientLogGate.Evaluate(
                    Context.ConnectionId, ClientLogGate.BuildPayloadKey(safeMessage));
                if (decision != ClientLogGate.Decision.Accept)
                {
                    ClientLogGate.LogDropSummaryIfWarranted(
                        decision == ClientLogGate.Decision.RateLimited ? "rate limit exceeded" : "duplicate content");
                    return;
                }

                // Reject implausible timestamps (forged or severe clock skew) so log ordering
                // stays meaningful; anything older than a day or in the future is clamped.
                DateTime now = DateTime.UtcNow;
                DateTime safeTimestamp =
                    request.Timestamp >= now.AddDays(-1) && request.Timestamp <= now.AddMinutes(5) ? request.Timestamp : now;

                await ErrorLog.LogError(safeTimestamp, $"CLIENT [IP: {GetClientIpAddress()}]: {safeMessage}", safeVersion);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error logging client error report.");
            }
        }

        public async Task LogClientError(LogClientErrorRequest request)
        {
            try
            {
                if (request?.Report == null || RequiresUpgrade(request))
                    return;

                var report = request.Report;
                var payload = JsonSerializer.Serialize(report);
                if (payload.Length > ClientLogGate.MaxPayloadLength)
                    payload = payload[..ClientLogGate.MaxPayloadLength];

                // Same flood/duplicate protection as LogError above.
                ClientLogGate.Decision decision = ClientLogGate.Evaluate(
                    Context.ConnectionId, ClientLogGate.BuildPayloadKey(payload));
                if (decision != ClientLogGate.Decision.Accept)
                {
                    ClientLogGate.LogDropSummaryIfWarranted(
                        decision == ClientLogGate.Decision.RateLimited ? "rate limit exceeded" : "duplicate content");
                    return;
                }

                var version = string.IsNullOrWhiteSpace(report.AppVersion) ? Util.GetCurrentVersion() : report.AppVersion;
                if (version.Length > 64) version = version[..64];
                await ErrorLog.LogError(DateTime.UtcNow, $"CLIENT-JS [IP: {GetClientIpAddress()}]: {payload}", version);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error logging client JS error report.");
            }
        }

        /// <summary>
        /// Accepts informational (non-error, non-emailing) client events. Expected
        /// user-flow outcomes (e.g. expired OAuth state) should go here, NOT through
        /// <see cref="LogError"/>, so they never page the developer or consume the
        /// admin error-email budget. Same flood/duplicate protection as LogError.
        /// </summary>
        public async Task LogInfo(LogInfoRequest request)
        {
            try
            {
                if (request == null || RequiresUpgrade(request))
                    return;

                string safeMessage = string.IsNullOrWhiteSpace(request.Message) ? "(empty)" : request.Message;
                if (safeMessage.Length > ClientLogGate.MaxPayloadLength)
                    safeMessage = safeMessage[..ClientLogGate.MaxPayloadLength];

                ClientLogGate.Decision decision = ClientLogGate.Evaluate(
                    Context.ConnectionId, ClientLogGate.BuildPayloadKey(safeMessage));
                if (decision != ClientLogGate.Decision.Accept)
                {
                    ClientLogGate.LogDropSummaryIfWarranted(
                        decision == ClientLogGate.Decision.RateLimited ? "rate limit exceeded" : "duplicate content");
                    return;
                }

                await ErrorLog.LogInfo($"CLIENT-INFO [IP: {GetClientIpAddress()}]: {safeMessage}");
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error logging client info event.");
            }
        }

        public async Task<ResponseWrapper<List<Companion>>> GetContestLeaderBoard(GetContestLeaderBoardRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<Companion>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.GetContestLeaderBoard();
        }

        public async Task<ResponseWrapper<CompanioNitaAdvice>> GetCompanioNitaAdviceById(GetCompanioNitaAdviceByIdRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<CompanioNitaAdvice>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.GetCompanitaAdvice(request.AdviceId, request.LanguageCode ?? "en");
        }
        public async Task<ResponseWrapper<List<CompanioNitaAdvice>>> GetCompanioNitaAdvice(GetCompanioNitaAdviceRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<CompanioNitaAdvice>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.GetCompanitaAdvice(request.Start, request.Count, request.LanguageCode ?? "en");
        }

        public async Task<ResponseWrapper<string>> AskCompanioNita(AskCompanioNitaRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string message = request.Message ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<string>.Fail(notVerified.ErrorCode, notVerified.Message);

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Please provide me with some creative advice of your choosing.";
            }

            if (ContentFilter.ContainsProhibitedContent(message))
                return ResponseWrapper<string>.Fail(ErrorCodes.ContentViolation, "Your message contains content that violates our terms of use.");

            // Persistence (prompt + response) happens inside CompanioNitaBase so the
            // advice thread always stays in sync.
            return await _companioNita.AskCompanioNitaAsync(loginToken, request.ThreadId, message);
        }

        /// <summary>
        /// Streams CompanioNita's response token-by-token to the client via SignalR server streaming.
        /// </summary>
        public async IAsyncEnumerable<string> StreamAskCompanioNita(
            StreamAskCompanioNitaRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (RequiresUpgrade(request))
            {
                yield return $"\u0001{ErrorCodes.ClientUpgradeRequired}:{ClientUpgradeRequiredMessage}";
                yield break;
            }

            string loginToken = request.LoginToken ?? string.Empty;
            string message = request.Message ?? string.Empty;
            int threadId = request.ThreadId;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
            {
                yield return $"\u0001{notVerified.ErrorCode}:{notVerified.Message}";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(message))
                message = "Please provide me with some creative advice of your choosing.";

            // Apply the same content filter as the non-streaming AskCompanioNita path.
            // This path now persists prompts, so untrusted input must be rejected before
            // it reaches the database or the model.
            if (ContentFilter.ContainsProhibitedContent(message))
            {
                yield return $"\u0001{ErrorCodes.ContentViolation}:Your message contains content that violates our terms of use.";
                yield break;
            }

            await foreach (string chunk in _companioNita.StreamAskCompanioNitaAsync(loginToken, threadId, message, cancellationToken))
            {
                yield return chunk;
            }
        }

        public async Task<ResponseWrapper<List<Advice>>> GetAdvice(GetAdviceRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<Advice>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<Advice>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetAdvice(request.LoginToken);
        }

        /// <summary>Creates a new CompanioNita advice thread and returns its id.</summary>
        public async Task<ResponseWrapper<int>> StartAdviceThread(StartAdviceThreadRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<int>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<int>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.StartAdviceThreadAsync(request.LoginToken);
        }

        /// <summary>Lists the caller's CompanioNita advice threads, newest first.</summary>
        public async Task<ResponseWrapper<List<AdviceThread>>> GetAdviceThreads(GetAdviceThreadsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<AdviceThread>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<AdviceThread>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetAdviceThreadsAsync(request.LoginToken);
        }

        /// <summary>Returns the question/answer exchanges of one of the caller's advice threads, oldest first.</summary>
        public async Task<ResponseWrapper<List<AdviceExchange>>> GetAdviceExchanges(GetAdviceExchangesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<AdviceExchange>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<AdviceExchange>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetAdviceExchangesAsync(request.LoginToken, request.ThreadId);
        }

        /// <summary>
        /// Streams CompanioNita's insight into a conversation. Accumulates the streamed
        /// response, persists the final (markdown-fence-stripped) message once via
        /// <see cref="Database.SendMessageAsync"/>, and yields the accumulated text so
        /// the client can render it live without blocking.
        /// </summary>
        public async IAsyncEnumerable<string> StreamAskCompanioNitaAboutConversation(
            StreamAskCompanioNitaAboutConversationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (RequiresUpgrade(request))
            {
                yield return $"\u0001{ErrorCodes.ClientUpgradeRequired}:{ClientUpgradeRequiredMessage}";
                yield break;
            }

            string loginToken = request.LoginToken ?? string.Empty;
            int userId = request.UserId;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
            {
                yield return $"\u0001{notVerified.ErrorCode}:{notVerified.Message}";
                yield break;
            }

            string guardKey = $"{loginToken}|{userId}";
            if (!CompanioNitaStreamGuard.TryStart(guardKey))
            {
                yield return $"\u0001{ErrorCodes.CompanioNitaStreamInProgress}:CompanioNita is already reading this conversation.";
                yield break;
            }

            try
            {
                var fullResponse = new StringBuilder();
                bool sawErrorMarker = false;

                await foreach (string chunk in _companioNita.StreamAskCompanioNitaAboutConversationAsync(loginToken, userId, cancellationToken))
                {
                    if (chunk.Length > 0 && chunk[0] == '\u0001')
                    {
                        sawErrorMarker = true;
                        yield return chunk;
                        continue;
                    }

                    // Reasoning chunks (\u0002-prefixed) are display-only; never
                    // append them to the persisted insight message body.
                    if (chunk.Length > 0 && chunk[0] == '\u0002')
                    {
                        yield return chunk;
                        continue;
                    }

                    fullResponse.Append(chunk);
                    yield return chunk;
                }

                // Error markers (invalid credentials, subscription, content violation) are
                // control signals for the client — never persist them as a message.
                if (sawErrorMarker) yield break;

                string messageText = Util.StripMarkdownCodeFences(fullResponse.ToString());
                if (string.IsNullOrWhiteSpace(messageText)) yield break;

                // Persist the finished insight message exactly like the non-streaming path.
                ResponseWrapper<SendMessageResult> result = await _database.SendMessageAsync(loginToken, userId, messageText, true);
                if (!result.IsSuccess)
                {
                    await ErrorLog.LogError(DateTime.UtcNow, $"Failed to persist streamed CompanioNita insight: {result.Message}", Util.GetCurrentVersion());
                    yield break;
                }
                if (result.Data.IgnoredSince == null)
                {
                    // Send a PUSH notification to the client about a new message waiting
                    await PushNotification(result.Data);
                }
            }
            finally
            {
                CompanioNitaStreamGuard.Finish(guardKey);
            }
        }

        public async Task<ResponseWrapper<bool>> AddIgnore(AddIgnoreRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AddIgnore(request.LoginToken, request.UserId);
        }
        public async Task<ResponseWrapper<bool>> RemoveIgnore(RemoveIgnoreRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.RemoveIgnore(request.LoginToken, request.UserId);
        }

        /// <summary>Reports a user for objectionable content.</summary>
        public async Task<ResponseWrapper<ReportResult>> ReportUser(ReportUserRequest hubRequest)
        {
            if (RequiresUpgrade(hubRequest))
                return ResponseWrapper<ReportResult>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = hubRequest.LoginToken ?? string.Empty;
            ReportRequest? request = hubRequest.Report;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<ReportResult>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                if (request == null)
                    return ResponseWrapper<ReportResult>.Fail(ErrorCodes.InvalidInput, "Report is required.");

                // cn_report_user caps @report_detail at NVARCHAR(500); truncate here so
                // an over-long detail doesn't trigger a SQL truncation error and drop
                // the report entirely. Report text is NOT content-filtered on purpose —
                // it may legitimately quote the offensive content being reported.
                if (request.ReportDetail?.Length > 500)
                    request = request with { ReportDetail = request.ReportDetail.Substring(0, 500) };

                return await _database.ReportUserAsync(loginToken, request);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in ReportUser method.");
                return ResponseWrapper<ReportResult>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred while reporting.");
            }
        }

        /// <summary>Gets all pending reports (admin only).</summary>
        public async Task<ResponseWrapper<List<PendingReport>>> GetPendingReports(GetPendingReportsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<PendingReport>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<PendingReport>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetPendingReportsAsync(request.LoginToken);
        }

        /// <summary>Resolves a report (admin only).</summary>
        public async Task<ResponseWrapper<bool>> ResolveReport(ResolveReportRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.ResolveReportAsync(request.LoginToken, request.ReportId, request.Status);
        }

        /// <summary>Sets a user's mute status: muted users cannot send messages (admin only).</summary>
        public async Task<ResponseWrapper<bool>> SetMuteStatus(SetMuteStatusRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.SetMuteStatusAsync(request.LoginToken, request.TargetUserId, request.IsMuted);
        }

        public async Task<ResponseWrapper<bool>> GuaranteeConfirm(GuaranteeConfirmRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            try
            {
                return await _database.GuaranteeConfirm(request.VerificationCode);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeConfirm method.");
                return ResponseWrapper<bool>.Fail(50000, "An unexpected error occurred while confirming the guarantee.");
            }
        }
        public async Task<ResponseWrapper<object>> GuaranteeEmail(GuaranteeEmailRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string email = request.Email ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                ResponseWrapper<string> verificationCode = await _database.GuaranteeEmailAsync(loginToken, email);
                if (verificationCode.IsSuccess)
                {
                    if (verificationCode.Data != null)  // returns null if the connection already exists
                    {
                        // Send an email to the user to confirm that they actually know the person who is doing the guarantee
                        await SendConfirmationEmailAsync(email, verificationCode.Data, currentUser);
                    }

                    return ResponseWrapper<object>.Success(null);
                }
                else
                {
                    return ResponseWrapper<object>.Fail(verificationCode.ErrorCode, verificationCode.Message);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeEmail method.");
                return ResponseWrapper<object>.Fail(50000, "An unexpected error occurred while sending the guarantee email.");
            }
        }


        public async Task<ResponseWrapper<object>> GuaranteeUser(GuaranteeUserRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string email = request.Email ?? string.Empty;
            byte[] imageData = request.ImageData ?? Array.Empty<byte>();

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                // I probably don't want to do this in the long run
                ErrorLog.LogInfo("Detecting Face for userid = " + currentUser.Data.UserId + "(" + currentUser.Data.Email + "), trying to guarantee email = " + email);

                // Contact OpenAI to make sure the image contains a person's face
                ResponseWrapper<bool> faceDetectionResult = await _companioNita.DetectFaceAsync(imageData);
                
                // Check if subscription is required
                if (!faceDetectionResult.IsSuccess)
                {
                    return ResponseWrapper<object>.Fail(faceDetectionResult.ErrorCode, faceDetectionResult.Message);
                }
                
                if (!faceDetectionResult.Data)
                    return ResponseWrapper<object>.Fail(ErrorCodes.FaceNotDetected, "No Face Detected");

                // The stored procedure will validate the token and perform the operation
                ResponseWrapper<string> verificationCode = await _database.GuaranteeUserAsync(loginToken, email, imageData);
                if (verificationCode.IsSuccess)
                {
                    if (verificationCode.Data != null)
                    {
                        // Send an email to the user to confirm that they actually know the person who is doing the guarantee
                        await SendConfirmationEmailAsync(email, verificationCode.Data, currentUser, imageData);
                        // OLD METHOD //await SendWelcomeEmailAsync(email, verificationCode.Data);
                    }
                    return ResponseWrapper<object>.Success(null);
                }
                else
                {
                    return ResponseWrapper<object>.Fail(verificationCode.ErrorCode, verificationCode.Message);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GuaranteeUser method.");
                return ResponseWrapper<object>.Fail(50000, "An unexpected error occurred while guaranteeing the user.");
            }
        }

        /// <summary>
        /// Server-side validation of uploaded photos. The SignalR API is publicly exposed so we
        /// must treat it as untrusted. Validates file size, JPEG format, aspect ratio, and pixel
        /// count WITHOUT depending on System.Drawing (which is Windows-only and deprecated).
        /// Dimensions are extracted by parsing the JPEG header directly.
        /// </summary>
        private static (int width, int height)? TryGetJpegDimensions(ReadOnlySpan<byte> data)
        {
            // JPEG must start with SOI marker FF D8
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
                return null;

            int i = 2;
            while (i + 4 <= data.Length)
            {
                // JPEG markers start with FF. Skip padding FF bytes.
                if (data[i] != 0xFF)
                    return null; // corrupt or unsupported segment

                byte marker = data[i + 1];

                // SOF markers: C0 (baseline), C1, C2 (progressive), C3
                if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3)
                {
                    if (i + 9 > data.Length) return null;
                    int height = (data[i + 5] << 8) | data[i + 6];
                    int width  = (data[i + 7] << 8) | data[i + 8];
                    if (width <= 0 || height <= 0) return null;
                    return (width, height);
                }

                // SOS marker (DA): start of scan — no more metadata after this
                if (marker == 0xDA)
                    return null; // dimensions not found before image data

                // All other markers have a 2-byte length field at offset +2
                if (i + 4 > data.Length) return null;
                int segmentLength = (data[i + 2] << 8) | data[i + 3];
                if (segmentLength < 2) return null;

                i += 2 + segmentLength; // skip marker FF xx + length field + segment data
            }

            return null;
        }

        public async Task<ResponseWrapper<Guid>> UploadPhoto(UploadPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<Guid>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            byte[] imageData = request.ImageData ?? Array.Empty<byte>();

            // Validate the logintoken first, so that we don't waste a call to the openAI API if the user isn't logged in
            ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
            if (!currentUser.IsSuccess) return ResponseWrapper<Guid>.Fail(currentUser.ErrorCode, currentUser.Message);

            // Validate file size (2MB max)
            if (imageData.Length > 2 * 1024 * 1024)
            {
                return ResponseWrapper<Guid>.Fail(ErrorCodes.InvalidInput, "File size exceeds the limit.");
            }

            // Parse JPEG header for dimension & format validation.
            // No dependency on System.Drawing — pure byte-level parsing, works cross-platform.
            var dimensions = TryGetJpegDimensions(imageData);
            if (dimensions == null)
            {
                return ResponseWrapper<Guid>.Fail(ErrorCodes.InvalidInput, "Invalid image format. Only baseline or progressive JPEG images are allowed.");
            }

            // Validate aspect ratio
            double aspectRatio = (double)dimensions.Value.width / dimensions.Value.height;
            if (aspectRatio < 0.4 || aspectRatio > 2.5)
            {
                return ResponseWrapper<Guid>.Fail(ErrorCodes.InvalidInput, "Invalid aspect ratio. The aspect ratio must be between 0.5 and 2.0.");
            }

            // Validate pixel count (1.5MP max)
            if (dimensions.Value.width * dimensions.Value.height > 1_500_000)
            {
                return ResponseWrapper<Guid>.Fail(ErrorCodes.InvalidInput, "Pixel count exceeds the limit.");
            }

            // Contact OpenAI to make sure the image contains a person's face
            ResponseWrapper<bool> faceDetectionResult = await _companioNita.DetectFaceAsync(imageData);
            
            // Check if subscription is required
            if (!faceDetectionResult.IsSuccess)
            {
                return ResponseWrapper<Guid>.Fail(faceDetectionResult.ErrorCode, faceDetectionResult.Message);
            }
            
            if (!faceDetectionResult.Data)
                return ResponseWrapper<Guid>.Fail(ErrorCodes.FaceNotDetected, "No Face Detected");

            // Validate the login token and upload the image to the database
            return await _database.UploadPhotoAsync(loginToken, imageData, GetClientIpAddress());
        }


        // Method to fetch users guaranteed by the logged-in user
        public async Task<ResponseWrapper<List<GuaranteedUser>>> GetGuaranteedUsers(GetGuaranteedUsersRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<GuaranteedUser>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<GuaranteedUser>>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Fetch the list of guaranteed users from the database
            return await _database.GetGuaranteedUsersAsync(request.LoginToken);
        }



        public async Task<ResponseWrapper<UserConversation>> StartUserConversation(StartUserConversationRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserConversation>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<UserConversation>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Fetch user details from th
            return await _database.StartUserConversationAsync(request.LoginToken, request.UserId);
        }

        public async Task<ResponseWrapper<bool>> RemoveGuarantee(RemoveGuaranteeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                // Call the database method to remove the guarantee using the ImageID
                var result = await _database.RemoveGuaranteeAsync(request.LoginToken, request.ImageId);

                if (!result.IsSuccess)
                {
                    // Log specific error code and message from the ResponseWrapper
                    ErrorLog.LogErrorMessage($"Error {result.ErrorCode}: {result.Message}");
                }

                return result; // Return the ResponseWrapper directly from the database method
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RemoveGuarantee method.");
                return ResponseWrapper<bool>.Fail(50000, "An unexpected error occurred while removing the guarantee.");
            }
        }


        public async Task<ResponseWrapper<object>> CheckVerificationCode(CheckVerificationCodeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.CheckVerificationCode(request.VerificationCode);
        }
        public async Task<ResponseWrapper<object>> ResetPassword(ResetPasswordRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.ResetPasswordAsync(request.VerificationCode, request.NewPassword);
        }

        private string LoadEmailTemplate(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    // Rebase absolute site links onto the environment's configured base URL.
                    return reader.ReadToEnd().Replace("{BaseUrl}", Util.SiteBaseUrl);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error loading email template.");
                return null;
            }
        }

        private async Task SendWelcomeEmailAsync(string email, string verificationCode)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.WelcomeEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.WelcomeEmail.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("SendWelcomeEmailAsync: failed to load one or both email templates.");
                return;
            }

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync(email, "Welcome to CompanioNation™!", textTemplate, htmlTemplate);
        }
        private async Task SendResetPasswordEmail(string email, string verificationCode)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ResetPasswordEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ResetPasswordEmail.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("SendResetPasswordEmail: failed to load one or both email templates.");
                return;
            }

            // Replace placeholders with the verification code
            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);
            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            // Send the email without confirming whether the email address exists
            await Email.SendEmailAsync(email, "Reset Password Request", textTemplate, htmlTemplate);
        }
        private async Task SendEmailChangeVerificationEmail(string email, string verificationCode)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ChangeEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ChangeEmail.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("SendEmailChangeVerificationEmail: failed to load one or both email templates.");
                return;
            }

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync(email, "Confirm your new CompanioNation™ email", textTemplate, htmlTemplate);
        }
        private async Task SendConfirmationEmailAsync(string email, string verificationCode, ResponseWrapper<UserDetails> currentUser)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmail.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmail.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("SendConfirmationEmailAsync: failed to load one or both email templates.");
                return;
            }

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync(email, "Confirmation Email", textTemplate, htmlTemplate);
        }

        private async Task SendConfirmationEmailAsync(string email, string verificationCode, ResponseWrapper<UserDetails> currentUser, byte[] imageData)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmailWithImage.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.ConfirmationEmailWithImage.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("SendConfirmationEmailAsync(image): failed to load one or both email templates.");
                return;
            }

            textTemplate = textTemplate.Replace("{Email}", email);
            textTemplate = textTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Email}", email);
            htmlTemplate = htmlTemplate.Replace("{RequestorEmail}", currentUser.Data.Email);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            string imageBase64 = Convert.ToBase64String(imageData);
            htmlTemplate = htmlTemplate.Replace("{Image}", $"<img src='data:image/png;base64,{imageBase64}' alt='User Image' />");

            await Email.SendEmailAsync(email, "Confirmation Email with Image", textTemplate, htmlTemplate);
        }


        public async Task<ResponseWrapper<Settings>> GetSettings(GetSettingsRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<Settings>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                Settings settings = await _database.GetAllSettingsAsync(request?.LanguageCode ?? "en");
                if (settings == null) return ResponseWrapper<Settings>.Fail(50000, "Error getting settings.");

                // Censor privileged system settings (PreviousDailyAdvice is
                // server-internal). LastMaintenanceRun is returned as stored.
                settings.PreviousDailyAdvice = null;

                return ResponseWrapper<Settings>.Success(settings);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, "Error in GetSettings method.");
                return ResponseWrapper<Settings>.Fail(50000, "Error getting settings.");
            }
        }

        public async Task<ResponseWrapper<List<UserImage>>> GetUserImages(GetUserImagesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<UserImage>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            // Validation handled within the stored procedure
            return await _database.GetUserImagesAsync(request.LoginToken);
        }


        public async Task<ResponseWrapper<List<UserConversation>>> GetUserConversations(GetUserConversationsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<UserConversation>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<UserConversation>>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Validate login token and fetch user conversations from the database
            return await _database.GetUserConversationsAsync(request.LoginToken);
        }

        public async Task<ResponseWrapper<List<UserMessage>>> GetMessagesWithUser(GetMessagesWithUserRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<UserMessage>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<UserMessage>>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Validate login token and fetch message history with the specified user
            return await _database.GetMessagesWithUserAsync(request.LoginToken, request.UserId);
        }

        public async Task<ResponseWrapper<int>> SendMessage(SendMessageRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<int>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string messageText = request.MessageText ?? string.Empty;
            int userId = request.UserId;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<int>.Fail(notVerified.ErrorCode, notVerified.Message);

            if (string.IsNullOrWhiteSpace(messageText)) return ResponseWrapper<int>.Fail(50000, "message is blank");
            // Limit the size of a message users may send
            if (messageText.Length > 1024) messageText = messageText.Substring(0, 1024);

            if (ContentFilter.ContainsProhibitedContent(messageText))
                return ResponseWrapper<int>.Fail(ErrorCodes.ContentViolation, "Your message contains content that violates our terms of use.");

            // Check if the sender is muted
            ResponseWrapper<UserDetails> sender = await _database.GetUserAsync(loginToken);
            if (!sender.IsSuccess) return ResponseWrapper<int>.Fail(sender.ErrorCode, sender.Message);
            if (sender.Data.IsMuted)
                return ResponseWrapper<int>.Fail(ErrorCodes.UserMuted, "Your account has been muted. You cannot send messages.");

            // Validate login token and send the message to the specified user
            ResponseWrapper<SendMessageResult> result = await _database.SendMessageAsync(loginToken, userId, messageText);
            if (!result.IsSuccess) return ResponseWrapper<int>.Fail(result.ErrorCode, result.Message);
            if (result.Data.IgnoredSince == null)
            {
                await PushNotification(result.Data);
            }
            return ResponseWrapper<int>.Success(result.Data.MessageId);
        }
        private async Task PushNotification(SendMessageResult parameters)
        {
            if (!string.IsNullOrEmpty(parameters.PushToken))
            {
                bool success = await _pushService.SendAsync(parameters.PushToken, parameters);
                if (!success)
                {
                    // Push delivery failed — remove the stale token belonging to the
                    // RECIPIENT (the token owner), never the sender's own token. The
                    // client re-registers automatically on next connect via
                    // ValidateAndRefreshPushSubscriptionAsync.
                    int userIdToClear = PushCleanup.GetUserIdToClear(parameters);
                    if (userIdToClear != 0)
                    {
                        await _database.ClearPushTokenByUserIdAsync(userIdToClear);
                    }
                }
            }
        }

        public async Task<ResponseWrapper<bool>> UpdatePushToken(UpdatePushTokenRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.UpdatePushTokenAsync(request.LoginToken, request.PushToken);
        }

        public async Task<ResponseWrapper<List<UserMessage>>> GetIgnoredMessages(GetIgnoredMessagesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<UserMessage>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<UserMessage>>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Validation handled within the stored procedure
            return await _database.GetIgnoredMessagesAsync(request.LoginToken);
        }

        public async Task<ResponseWrapper<List<Companion>>> FindCompanions(FindCompanionsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<Companion>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<Companion>>.Fail(notVerified.ErrorCode, notVerified.Message);

            ResponseWrapper<List<Companion>> result = await _database.FindCompanionsAsync(
                request.LoginToken, request.CisMale, request.CisFemale, request.Other, request.TransMale, request.TransFemale,
                request.Cities, request.AgeMin, request.AgeMax, request.ShowIgnoredUsers);
            return result;
        }


        public async Task RequestPasswordReset(RequestPasswordResetRequest request)
        {
            if (RequiresUpgrade(request))
                return;

            var ip = GetClientIpAddress();
            if (IsUnauthRateLimited(ip))
                return; // Silently return — do not reveal rate limit or email existence

            try
            {
                // Attempt to generate a new verification code and send it to the user
                string verificationCode = await _database.GenerateNewVerificationCodeAsync(request.Email);

                if (string.IsNullOrWhiteSpace(verificationCode)) return;

                // Send the verification email without revealing if the email exists in the database
                await SendResetPasswordEmail(request.Email, verificationCode);
            }
            catch (Exception ex)
            {
                // Log the error but do not expose any details to the caller
                ErrorLog.LogErrorException(ex, "RequestPasswordReset");
            }
        }

        /// <summary>
        /// Resends the signup email-verification link to the caller's address.
        /// Generates a fresh single-use code so a lost/expired link can always be
        /// replaced. Requires a logged-in, unverified account.
        /// </summary>
        public async Task<ResponseWrapper<bool>> ResendVerificationEmail(ResendVerificationEmailRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;

            ResponseWrapper<UserDetails> user = await _database.GetUserAsync(loginToken);
            if (!user.IsSuccess)
                return ResponseWrapper<bool>.Fail(user.ErrorCode, user.Message);

            if (user.Data.Verified)
                return ResponseWrapper<bool>.Success(true);

            string verificationCode = await _database.GenerateNewVerificationCodeAsync(user.Data.Email);
            if (string.IsNullOrWhiteSpace(verificationCode))
                return ResponseWrapper<bool>.Fail(ErrorCodes.UnknownError, "Could not generate a verification code.");

            await SendWelcomeEmailAsync(user.Data.Email, verificationCode);
            return ResponseWrapper<bool>.Success(true);
        }


        public async Task<ResponseWrapper<bool>> UpdateUserDetails(UpdateUserDetailsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            UserDetails userDetails = request.UserDetails ?? new UserDetails();

            if (ContentFilter.ContainsProhibitedContent(userDetails.Name))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ContentViolation, "Your name contains content that violates our terms of use.");
            if (ContentFilter.ContainsProhibitedContent(userDetails.Description))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ContentViolation, "Your description contains content that violates our terms of use.");

            // Validate the token and update user info using the stored procedure
            return await _database.UpdateUserDetailsAsync(loginToken, userDetails);
        }

        /// <summary>
        /// Stages an email change and sends a verification code to the new address.
        /// </summary>
        public async Task<ResponseWrapper<string>> RequestEmailChange(RequestEmailChangeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string newEmail = request.NewEmail ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<string>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess)
                    return ResponseWrapper<string>.Fail(currentUser.ErrorCode, currentUser.Message);

                if (currentUser.Data.OAuthLogin)
                    return ResponseWrapper<string>.Fail(ErrorCodes.OperationNotAllowed, "Email changes are managed by your sign-in provider.");

                ResponseWrapper<string> result = await _database.RequestEmailChangeAsync(loginToken, newEmail);
                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data))
                {
                    await SendEmailChangeVerificationEmail(newEmail?.Trim() ?? string.Empty, result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RequestEmailChange method.");
                return ResponseWrapper<string>.Fail(50000, "An unexpected error occurred while requesting the email change.");
            }
        }

        /// <summary>
        /// Confirms a staged email change using the verification code sent to the new address.
        /// </summary>
        public async Task<ResponseWrapper<bool>> ConfirmEmailChange(ConfirmEmailChangeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.ConfirmEmailChangeAsync(request.LoginToken, request.VerificationCode);
        }

        /// <summary>
        /// Soft-deletes the caller's profile. Clears personal data and invalidates the session.
        /// </summary>
        public async Task<ResponseWrapper<bool>> DeleteProfile(DeleteProfileRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            return await _database.DeleteProfileAsync(request.LoginToken);
        }



        // Method to update the visibility of an image
        public async Task<ResponseWrapper<bool>> UpdateImageVisibility(UpdateImageVisibilityRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            // Call the database method to update the image visibility
            ResponseWrapper<bool> result = await _database.UpdateImageVisibilityAsync(request.LoginToken, request.ImageId, request.IsVisible);
            return result;
        }

        public async Task<ResponseWrapper<bool>> UpdateReviewVisibility(UpdateReviewVisibilityRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.UpdateReviewVisibilityAsync(request.LoginToken, request.ImageId, request.IsPublic);
        }


        public async Task<ResponseWrapper<bool>> UpdateImageReview(UpdateImageReviewRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.UpdateImageReviewAsync(request.LoginToken, request.ImageId, request.Rating, request.Review);
        }

        /// <summary>
        /// Saves the caller's rating/review of the other party in a confirmed LINK.
        /// </summary>
        public async Task<ResponseWrapper<bool>> SetConnectionReview(SetConnectionReviewRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string review = request.Review ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Bound the review text (mirrors SendMessage's 1024-char cap, but a little
            // roomier since reviews are longer-form). Truncate, then filter, so a slur
            // can't hide beyond the cap.
            if (review?.Length > 2000) review = review.Substring(0, 2000);

            if (ContentFilter.ContainsProhibitedContent(review))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ContentViolation, "Your review contains content that violates our terms of use.");

            // Muted accounts can't post reviews either (mirrors SendMessage) —
            // otherwise a mute could be bypassed by writing an abusive review.
            ResponseWrapper<UserDetails> sender = await _database.GetUserAsync(loginToken);
            if (!sender.IsSuccess) return ResponseWrapper<bool>.Fail(sender.ErrorCode, sender.Message);
            if (sender.Data.IsMuted)
                return ResponseWrapper<bool>.Fail(ErrorCodes.UserMuted, "Your account has been muted. You cannot post reviews.");

            return await _database.SetConnectionReviewAsync(loginToken, request.ConnectionId, request.Rating, review);
        }

        /// <summary>
        /// Lets the SUBJECT of a review toggle whether it is publicly visible on
        /// their profile. Only the person the review is about may call this.
        /// </summary>
        public async Task<ResponseWrapper<bool>> SetConnectionReviewVisibility(SetConnectionReviewVisibilityRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.SetConnectionReviewVisibilityAsync(request.LoginToken, request.ConnectionId, request.IsVisible);
        }


        public async Task<ResponseWrapper<string>> TriggerMaintenanceManually(TriggerMaintenanceManuallyRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<string>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Validate the login token and check if the user is an administrator
            ResponseWrapper<UserDetails> result = await _database.GetUserAsync(loginToken);

            if (!result.IsSuccess) return ResponseWrapper<string>.Fail(result.ErrorCode, result.Message);
            if (!result.Data.IsAdministrator)
            {
                ErrorLog.LogErrorMessage($"SECURITY BREACH: Unauthorized access attempt to TriggerMaintenanceManually() by User ID: {result?.Data.UserId}, IP Address: {GetClientIpAddress()}");

                // Return an unauthorized message if the user is not an admin
                return ResponseWrapper<string>.Fail(ErrorCodes.AdminUnauthorized, "Unauthorized access. Only administrators can perform this action.");
            }

            try
            {
                // Run the maintenance task
                await _maintenanceEventService.RunDailyMaintenanceAsync(CancellationToken.None); // Make sure RunDailyMaintenanceAsync is public
                return ResponseWrapper<string>.Success("Daily maintenance executed successfully.");
            }
            catch (Exception ex)
            {
                // Log the error and return a failure message
                ErrorLog.LogErrorException(ex, "Error executing daily maintenance manually.");
                return ResponseWrapper<string>.Fail(ex.HResult, "An error occurred while executing maintenance.");
            }
        }


        public async Task<ResponseWrapper<List<Country>>> GetCountries(GetCountriesRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<List<Country>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                // Fetch the list of countries from the database based on the continent
                ResponseWrapper<List<Country>> result = await _database.GetCountriesAsync(request.Continent);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCountries method.");
                return ResponseWrapper<List<Country>>.Fail(50000, "An unexpected error occurred while fetching countries.");
            }
        }

        public async Task<ResponseWrapper<List<City>>> GetNearbyCities(GetNearbyCitiesRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<List<City>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                // Validate the login token
                ResponseWrapper<UserDetails> user = await _database.GetUserAsync(request.LoginToken);
                if (!user.IsSuccess) return ResponseWrapper<List<City>>.Fail(user.ErrorCode, user.Message);

                // Fetch the list of searchable cities from the database
                ResponseWrapper<List<City>> result = await _database.GetNearbyCitiesAsync(request.LoginToken);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetNearbyCities method.");
                return ResponseWrapper<List<City>>.Fail(50000, "An unexpected error occurred while fetching searchable cities.");
            }
        }
        public async Task<ResponseWrapper<List<City>>> GetCities(GetCitiesRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<List<City>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                // Fetch the list of cities from the database based on the continent, country, and search term
                ResponseWrapper<List<City>> result = await _database.GetCitiesAsync(request.Country, request.SearchTerm);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCities method.");
                return ResponseWrapper<List<City>>.Fail(50000, "An unexpected error occurred while fetching cities.");
            }
        }
        public async Task<ResponseWrapper<City>> GetCity(GetCityRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<City>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                // Fetch the city details from the database based on the geonameid
                ResponseWrapper<City> result = await _database.GetCityAsync(request.LoginToken, request.Geonameid);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetCity method.");
                return ResponseWrapper<City>.Fail(50000, "An unexpected error occurred while fetching city details.");
            }
        }

        // Returns up to the five closest cities to the supplied GPS coordinates,
        // ordered nearest-first, used to pre-fill the city selector and offer
        // nearby alternatives when completing or editing a profile.
        public async Task<ResponseWrapper<List<City>>> GetNearestCities(GetNearestCitiesRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return ResponseWrapper<List<City>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

                ResponseWrapper<List<City>> result = await _database.GetNearestCitiesAsync(request.LoginToken, request.Latitude, request.Longitude);
                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetNearestCities method.");
                return ResponseWrapper<List<City>>.Fail(50000, "An unexpected error occurred while fetching the nearest cities.");
            }
        }

        // Returns (emailExists, oauthRequired)
        public async Task<ResponseWrapper<CheckEmailResult>> CheckEmailExists(CheckEmailExistsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<CheckEmailResult>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsUnauthRateLimited(ip))
                return ResponseWrapper<CheckEmailResult>.Fail(ErrorCodes.RateLimited, "Too many requests. Please try again shortly.");

            try
            {
                CheckEmailResult result = await _database.CheckEmailExistsAsync(request.Email);
                return ResponseWrapper<CheckEmailResult>.Success(result);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<CheckEmailResult>.Fail(ex.HResult, ex.Message);
            }
        }

        public async Task<ResponseWrapper<bool>> CreateNewUser(CreateNewUserRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var ip = GetClientIpAddress();
            if (IsSignupRateLimited(ip))
                return ResponseWrapper<bool>.Fail(ErrorCodes.RateLimited, "Too many account creation attempts. Please try again later.");

            try
            {
                string email = (request.Email ?? string.Empty).Trim();
                string validation_code = await _database.CreateNewUserAsync(email, request.Password, GetClientIpAddress());
                if (string.IsNullOrWhiteSpace(validation_code)) return ResponseWrapper<bool>.Fail(50000, "Could not create new user");
                await SendWelcomeEmailAsync(email, validation_code);

                return ResponseWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return ResponseWrapper<bool>.Fail(ex.HResult, ex.Message);
            }
        }


#if DEBUG
        // TEST SUITE CODE, ONLY IN DEBUG VERSION, NOT FOR PRODUCTION
        public async Task<ResponseWrapper<string>> RunTestSuite(RunTestSuiteRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            // Validate the login token and check if the user is an administrator
            ResponseWrapper<UserDetails> user = await _database.GetUserAsync(request.LoginToken);

            if (!user.IsSuccess || !user.Data.IsAdministrator)
            {
                return ResponseWrapper<string>.Fail(user.ErrorCode, user.Message);
            }
            
            string result = "";
            // Add any specific test implementations here
            result += ",";

            bool success = await Email.SendTextEmailAsync("errors@companionation.com", "CompanioNation™ Email Test", "email sending test");
            result += success;

            return ResponseWrapper<string>.Success(result);
        }

        public byte[] GeneratePngImage(GeneratePngImageRequest request)
        {
            if (RequiresUpgrade(request))
                return Array.Empty<byte>();

            int width = request.Width;
            int height = request.Height;
            var backgroundColor = Color.FromArgb(request.BackgroundColorArgb);

            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(backgroundColor);
                    string text = "Hello, World!";
                    using (Font font = new Font("Arial", 20))
                    {
                        using (Brush brush = new SolidBrush(Color.Black))
                        {
                            graphics.DrawString(text, font, brush, new PointF(10, 10));
                        }
                    }
                }
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    bitmap.Save(memoryStream, ImageFormat.Png);
                    return memoryStream.ToArray();
                }
            }
        }
#endif

        public async Task ReceiveFeedback(ReceiveFeedbackRequest request)
        {
            try
            {
                if (RequiresUpgrade(request))
                    return;

                string loginToken = request.LoginToken ?? string.Empty;
                string feedbackText = request.FeedbackText ?? string.Empty;
                string feedbackDebugInfo = request.FeedbackDebugInfo ?? string.Empty;

                // Log the feedback or process it as needed
                Console.WriteLine($"Received feedback: {feedbackText}");

                // Build enriched plain-text and HTML bodies
                string textBody = feedbackText;
                string htmlBody = System.Net.WebUtility.HtmlEncode(feedbackText);

                // Try to resolve the user if a login token was provided
                string userHeaderPlain = string.Empty;
                string userHeaderHtml = string.Empty;

                if (!string.IsNullOrWhiteSpace(loginToken))
                {
                    var userResult = await _database.GetUserAsync(loginToken);
                    if (userResult.IsSuccess && userResult.Data != null)
                    {
                        var user = userResult.Data;
                        string adminUrl = $"{Util.SiteBaseUrl}/Admin?userId={user.UserId}";

                        userHeaderPlain =
                            $"--- USER INFO ---\n" +
                            $"Logged in: Yes\n" +
                            $"Email: {user.Email}\n" +
                            $"User ID: {user.UserId}\n" +
                            $"Admin link: {adminUrl}\n" +
                            $"------------------\n\n";

                        userHeaderHtml =
                            $"<div style=\"background:#f0f8ff;border-left:4px solid #2196F3;padding:12px 16px;margin-bottom:16px;font-family:Arial,sans-serif;\">" +
                            $"<strong style=\"color:#2196F3;\">Logged-in User</strong><br/>" +
                            $"<strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(user.Email)}<br/>" +
                            $"<strong>User ID:</strong> {user.UserId}<br/>" +
                            $"<a href=\"{adminUrl}\" style=\"color:#2196F3;\">View/Edit Profile in Admin</a>" +
                            $"</div>";
                    }
                    else
                    {
                        userHeaderPlain = "--- USER INFO ---\nLogged in: No (token invalid/expired)\n------------------\n\n";
                        userHeaderHtml = "<div style=\"background:#fff3cd;border-left:4px solid #ffc107;padding:12px 16px;margin-bottom:16px;font-family:Arial,sans-serif;\"><strong>User Status:</strong> Not logged in (or session expired)</div>";
                    }
                }
                else
                {
                    userHeaderPlain = "--- USER INFO ---\nLogged in: No\n------------------\n\n";
                    userHeaderHtml = "<div style=\"background:#fff3cd;border-left:4px solid #ffc107;padding:12px 16px;margin-bottom:16px;font-family:Arial,sans-serif;\"><strong>User Status:</strong> Not logged in</div>";
                }

                textBody = userHeaderPlain + textBody;

                if (!string.IsNullOrWhiteSpace(feedbackDebugInfo))
                {
                    textBody += $"\n\n{feedbackDebugInfo}\n";
                }

                htmlBody = userHeaderHtml +
                           $"<div style=\"font-family:Arial,sans-serif;padding:12px 16px;background:#fafafa;border:1px solid #e0e0e0;border-radius:4px;\">" +
                           $"<p style=\"white-space:pre-wrap;margin:0;\">{System.Net.WebUtility.HtmlEncode(feedbackText)}</p>" +
                           $"</div>";

                if (!string.IsNullOrWhiteSpace(feedbackDebugInfo))
                {
                    htmlBody +=
                        $"<div style=\"font-family:Consolas,Monaco,monospace;font-size:12px;padding:12px 16px;background:#f5f5f5;border:1px solid #ddd;border-radius:4px;margin-top:16px;\">" +
                        $"<strong style=\"font-family:Arial,sans-serif;font-size:13px;\">Client Debug Info</strong><br/>" +
                        $"<pre style=\"white-space:pre-wrap;margin:8px 0 0;font-family:inherit;\">{System.Net.WebUtility.HtmlEncode(feedbackDebugInfo)}</pre>" +
                        $"</div>";
                }

                // Optionally, save the feedback to the database
                //await _database.SaveFeedbackAsync(feedbackText);
                await Email.SendEmailAsync("feedback@companionation.com", "CompanioNation™ Feedback", textBody, htmlBody);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in ReceiveFeedback method.");
            }
        }



        // =============================================
        // Admin Profile Moderation Methods
        // Auth checks are performed by the stored procedures.
        // =============================================

        /// <summary>
        /// Returns a paginated list of profiles for admin triage review.
        /// Sorted by unresolved report count (most first). Supports optional search by name, email, or user ID.
        /// </summary>
        public async Task<ResponseWrapper<List<UserDetails>>> AdminGetFlaggedProfiles(AdminGetFlaggedProfilesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<UserDetails>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<UserDetails>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetFlaggedProfilesAsync(request.LoginToken, request.Offset, request.Count, request.SearchTerm);
        }

        /// <summary>
        /// Returns full profile details and photos for admin audit of a specific user.
        /// </summary>
        public async Task<ResponseWrapper<UserDetails>> AdminGetProfileForAudit(AdminGetProfileForAuditRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<UserDetails>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetProfileForAuditAsync(request.LoginToken, request.UserId);
        }

        /// <summary>
        /// Admin edits a user's profile fields. Reuses the existing UpdateUserDetails flow.
        /// userDetails.UserId identifies the target — the stored procedure checks admin-or-self.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminUpdateProfile(AdminUpdateProfileRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.UpdateUserDetailsAsync(request.LoginToken, request.UserDetails);
        }

        /// <summary>
        /// Admin updates account-level attributes (subscription expiry, admin status,
        /// verification, mute state, optional password) for a target user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminUpdateUserAttributes(AdminUpdateUserAttributesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminUpdateUserAttributesAsync(request.LoginToken, request.Attributes);
        }

        /// <summary>
        /// Admin deletes a photo from a user's profile.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDeletePhoto(AdminDeletePhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminDeletePhotoAsync(request.LoginToken, request.UserId, request.ImageId);
        }

        /// <summary>
        /// Returns the event badges awarded to a user.
        /// </summary>
        public async Task<ResponseWrapper<List<EventBadge>>> GetUserBadges(GetUserBadgesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<EventBadge>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<EventBadge>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetUserBadgesAsync(request.LoginToken, request.TargetUserId);
        }

        /// <summary>
        /// Returns all event badge definitions for the admin badge editor.
        /// </summary>
        public async Task<ResponseWrapper<List<EventBadge>>> AdminListEventBadges(AdminListEventBadgesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<EventBadge>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<EventBadge>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminListEventBadgesAsync(request.LoginToken);
        }

        /// <summary>
        /// Admin awards an event badge to a user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminAwardEventBadge(AdminAwardEventBadgeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminAwardEventBadgeAsync(request.LoginToken, request.TargetUserId, request.BadgeId);
        }

        /// <summary>
        /// Admin revokes an event badge from a user.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminRevokeEventBadge(AdminRevokeEventBadgeRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminRevokeEventBadgeAsync(request.LoginToken, request.TargetUserId, request.BadgeId);
        }

        /// <summary>
        /// Admin sends a broadcast notification (or a targeted notification to one
        /// user by email). Stale tokens are cleared as each send fails.
        /// </summary>
        public async Task<ResponseWrapper<BroadcastResult>> AdminSendBroadcastNotification(AdminSendBroadcastNotificationRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<BroadcastResult>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string title = request.Title ?? string.Empty;
            string body = request.Body ?? string.Empty;
            string? url = request.Url;
            string? targetEmail = request.TargetEmail;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
                return ResponseWrapper<BroadcastResult>.Fail(ErrorCodes.InvalidInput, "Title and body are required.");

            var normalizedUrl = string.IsNullOrWhiteSpace(url) ? "/" : url.Trim();

            // The stored procedure enforces admin access and, when targetEmail is
            // supplied, limits the result to that user's token.
            var tokensResult = await _database.AdminGetPushTokensAsync(loginToken, string.IsNullOrWhiteSpace(targetEmail) ? null : targetEmail.Trim());
            if (!tokensResult.IsSuccess)
                return ResponseWrapper<BroadcastResult>.Fail(tokensResult.ErrorCode, tokensResult.Message);

            var tokens = tokensResult.Data ?? new List<PushTokenRow>();
            if (tokens.Count == 0)
                return ResponseWrapper<BroadcastResult>.Success(new BroadcastResult(0, 0, 0));

            var trimmedTitle = title.Trim();
            var trimmedBody = body.Trim();

            int sent = 0;
            int failed = 0;

            // Fan out with bounded concurrency so a large audience doesn't run
            // serially (or hammer the push providers with an unbounded burst), and
            // stop promptly if the admin disconnects mid-send.
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 10,
                CancellationToken = Context.ConnectionAborted
            };

            try
            {
                await Parallel.ForEachAsync(tokens, parallelOptions, async (row, ct) =>
                {
                    ct.ThrowIfCancellationRequested();

                    var payload = new PushPayload
                    {
                        Title = trimmedTitle,
                        Body = trimmedBody,
                        Url = normalizedUrl,
                        Tag = "promotional"
                    };

                    bool keep = await _pushService.SendAsync(row.PushToken, payload);
                    if (keep)
                    {
                        Interlocked.Increment(ref sent);
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        await _database.ClearPushTokenByUserIdAsync(row.UserId);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // The caller disconnected (or the server is shutting down). Report
                // what completed so far; the remaining sends are simply abandoned.
            }

            return ResponseWrapper<BroadcastResult>.Success(new BroadcastResult(tokens.Count, sent, failed));
        }

        /// <summary>
        /// Finds Azure blobs with no matching cn_images record (orphaned images).
        /// </summary>
        public async Task<ResponseWrapper<List<OrphanedImage>>> AdminFindOrphanedImages(AdminFindOrphanedImagesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<OrphanedImage>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<OrphanedImage>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminFindOrphanedImagesAsync(request.LoginToken);
        }

        /// <summary>
        /// Deletes the confirmed list of orphaned blobs from Azure storage.
        /// </summary>
        public async Task<ResponseWrapper<int>> AdminDeleteOrphanedImages(AdminDeleteOrphanedImagesRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<int>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<int>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminDeleteOrphanedImagesAsync(request.LoginToken, request.ImageGuids);
        }

        /// <summary>
        /// Admin dismisses a profile from the triage queue.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDismissProfile(AdminDismissProfileRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminDismissProfileAsync(request.LoginToken, request.UserId);
        }

        /// <summary>
        /// Admin soft-deletes a target user's account. Admin authorization is enforced by the stored procedure.
        /// </summary>
        public async Task<ResponseWrapper<bool>> AdminDeleteProfile(AdminDeleteProfileRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<bool>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<bool>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.AdminDeleteProfileAsync(request.LoginToken, request.UserId);
        }

        /// <summary>
        /// Returns aggregated site-wide statistics (signups, activity, totals) for the admin dashboard.
        /// </summary>
        public async Task<ResponseWrapper<SiteStats>> AdminGetSiteStats(AdminGetSiteStatsRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<SiteStats>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<SiteStats>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetSiteStatsAsync(request.LoginToken);
        }

        /// <summary>
        /// Admin checks a single photo for compliance (face detection) without deleting it.
        /// Returns a descriptive result string.
        /// </summary>
        public async Task<ResponseWrapper<string>> AdminCheckPhoto(AdminCheckPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<string>.Fail(notVerified.ErrorCode, notVerified.Message);

            // Validate admin
            ResponseWrapper<UserDetails> callerResult = await _database.GetUserAsync(loginToken);
            if (!callerResult.IsSuccess)
                return ResponseWrapper<string>.Fail(callerResult.ErrorCode, callerResult.Message);
            if (!callerResult.Data.IsAdministrator)
                return ResponseWrapper<string>.Fail(ErrorCodes.AdminUnauthorized, "Admin access required.");

            byte[]? imageBytes = await _database.DownloadBlobFromAzureAsync(request.ImageGuid);
            if (imageBytes == null)
                return ResponseWrapper<string>.Success("Could not download image from storage.");

            ResponseWrapper<bool> faceResult = await _companioNita.DetectFaceAsync(imageBytes);
            if (!faceResult.IsSuccess)
                return ResponseWrapper<string>.Success($"Error during check: {faceResult.Message}");

            return ResponseWrapper<string>.Success(faceResult.Data
                ? "✅ PASS — Photo meets compliance requirements."
                : "❌ FAIL — Photo does not meet compliance requirements.");
        }

        /// <summary>
        /// Streams progress of a bulk photo compliance scan. Each yielded string is a JSON
        /// status update. Non-compliant photos are deleted automatically.
        /// </summary>
        public async IAsyncEnumerable<string> AdminCheckAllPhotos(
            AdminCheckAllPhotosRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (RequiresUpgrade(request))
            {
                yield return System.Text.Json.JsonSerializer.Serialize(new { error = ClientUpgradeRequiredMessage });
                yield break;
            }

            string loginToken = request.LoginToken ?? string.Empty;

            // Validate admin
            ResponseWrapper<UserDetails> callerResult = await _database.GetUserAsync(loginToken);
            if (!callerResult.IsSuccess)
            {
                yield return System.Text.Json.JsonSerializer.Serialize(new { error = callerResult.Message });
                yield break;
            }
            if (!callerResult.Data.IsAdministrator)
            {
                yield return System.Text.Json.JsonSerializer.Serialize(new { error = "Admin access required." });
                yield break;
            }

            // Get all photos
            ResponseWrapper<List<UserImage>> allPhotosResult = await _database.AdminGetAllPhotosAsync(loginToken);
            if (!allPhotosResult.IsSuccess)
            {
                yield return System.Text.Json.JsonSerializer.Serialize(new { error = allPhotosResult.Message });
                yield break;
            }

            var photos = allPhotosResult.Data;
            int total = photos.Count;
            int checked_ = 0;
            int passed = 0;
            int failed = 0;
            int errors = 0;

            yield return System.Text.Json.JsonSerializer.Serialize(new { total, status = "started" });

            foreach (var photo in photos)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return System.Text.Json.JsonSerializer.Serialize(new
                        { total, @checked = checked_, passed, failed, errors, status = "cancelled" });
                    yield break;
                }

                checked_++;
                byte[]? imageBytes = await _database.DownloadBlobFromAzureAsync(photo.ImageGuid);
                if (imageBytes == null)
                {
                    errors++;
                    yield return System.Text.Json.JsonSerializer.Serialize(new
                        { total, @checked = checked_, passed, failed, errors, status = "progress",
                          current = $"Image {photo.ImageId}: could not download" });
                    continue;
                }

                try
                {
                    ResponseWrapper<bool> faceResult = await _companioNita.DetectFaceAsync(imageBytes);
                    if (faceResult.IsSuccess && faceResult.Data)
                    {
                        passed++;
                    }
                    else
                    {
                        failed++;
                        // Delete the non-compliant photo
                        await _database.AdminDeletePhotoAsync(loginToken, photo.UserId, photo.ImageId);
                    }
                }
                catch
                {
                    errors++;
                }

                yield return System.Text.Json.JsonSerializer.Serialize(new
                    { total, @checked = checked_, passed, failed, errors, status = "progress" });
            }

            yield return System.Text.Json.JsonSerializer.Serialize(new
                { total, @checked = checked_, passed, failed, errors, status = "completed" });
        }

        // ──── LINK Methods ────

        /// <summary>
        /// Returns a server-signed QR payload for LINK.
        /// </summary>
        public async Task<ResponseWrapper<string>> GetLinkPayload(GetLinkPayloadRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<string>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<string>.Fail(currentUser.ErrorCode, currentUser.Message);

                string secret = Environment.GetEnvironmentVariable("COMPANIONATION_LINK_SECRET");
                if (string.IsNullOrWhiteSpace(secret))
                {
                    ErrorLog.LogErrorMessage("COMPANIONATION_LINK_SECRET is not configured.");
                    return ResponseWrapper<string>.Fail(ErrorCodes.ExternalServiceError, "LINK service is not configured.");
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int userId = currentUser.Data.UserId;

                // Sign: HMAC-SHA256(secret, "LINK|{userId}|{timestamp}")
                string dataToSign = $"LINK|{userId}|{timestamp}";
                byte[] keyBytes = Convert.FromBase64String(secret);
                using var hmac = new HMACSHA256(keyBytes);
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
                string signature = Convert.ToHexString(hash).ToLowerInvariant();

                var payload = new { u = userId, t = timestamp, s = signature };
                string json = JsonSerializer.Serialize(payload);
                string base64Url = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

                return ResponseWrapper<string>.Success(base64Url);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in GetLinkPayload.");
                return ResponseWrapper<string>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Validates a QR code payload and creates a LINK.
        /// </summary>
        public async Task<ResponseWrapper<LinkedUser>> RedeemQrLink(RedeemQrLinkRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string code = request.Code ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<LinkedUser>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<LinkedUser>.Fail(currentUser.ErrorCode, currentUser.Message);

                // Decode base64url
                string padded = code.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                var payload = JsonSerializer.Deserialize<LinkPayload>(json);

                if (payload == null)
                    return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.LinkInvalid, "Invalid QR code.");

                // Validate timestamp (3-minute window)
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - payload.Timestamp) > 180)
                    return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.LinkExpired, "This QR code has expired.");

                // Verify HMAC signature
                string secret = Environment.GetEnvironmentVariable("COMPANIONATION_LINK_SECRET");
                if (string.IsNullOrWhiteSpace(secret))
                    return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.ExternalServiceError, "LINK service is not configured.");

                string dataToSign = $"LINK|{payload.UserId}|{payload.Timestamp}";
                byte[] keyBytes = Convert.FromBase64String(secret);
                using var hmac = new HMACSHA256(keyBytes);
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
                string expectedSignature = Convert.ToHexString(hash).ToLowerInvariant();

                if (payload.Signature != expectedSignature)
                    return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.LinkInvalid, "Invalid QR code signature.");

                // Create the link
                return await _database.CreateQrLinkAsync(loginToken, payload.UserId);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RedeemQrLink.");
                return ResponseWrapper<LinkedUser>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Sends a LINK email invite.
        /// </summary>
        public async Task<ResponseWrapper<object>> LinkEmail(LinkEmailRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            string email = request.Email ?? string.Empty;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                ResponseWrapper<string> verificationCode = await _database.LinkEmailAsync(loginToken, email);
                if (verificationCode.IsSuccess)
                {
                    if (verificationCode.Data == null)
                    {
                        // SP returned NULL verification_code — link already exists
                        return ResponseWrapper<object>.Fail(ErrorCodes.LinkAlreadyExists, "You're already LINKed with this person.");
                    }

                    await SendLinkInviteEmailAsync(email, verificationCode.Data, currentUser.Data.Name);
                    return ResponseWrapper<object>.Success(null);
                }
                return ResponseWrapper<object>.Fail(verificationCode.ErrorCode, verificationCode.Message);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in LinkEmail.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred while sending the LINK invite.");
            }
        }

        /// <summary>
        /// Confirms an emailed LINK invitation (opened as an in-app deep link)
        /// and logs the recipient in. No login token is required yet — the
        /// verification code itself authorizes the confirmation.
        /// </summary>
        public async Task<ResponseWrapper<UserDetails>> ConfirmEmailLink(ConfirmEmailLinkRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            try
            {
                ResponseWrapper<(string LoginToken, string InitiatorName)> confirm =
                    await _database.ConfirmLinkAsync(request.VerificationCode);
                if (!confirm.IsSuccess)
                    return ResponseWrapper<UserDetails>.Fail(confirm.ErrorCode, confirm.Message);

                ResponseWrapper<UserDetails> user = await _database.GetUserAsync(confirm.Data.LoginToken);
                if (!user.IsSuccess)
                    return ResponseWrapper<UserDetails>.Fail(user.ErrorCode, user.Message);

                // Attach this connection to the new user's group immediately so push
                // routing works without waiting for a reconnect.
                await SetSignalRGroupId(user.Data.UserId);
                return user;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in ConfirmEmailLink.");
                return ResponseWrapper<UserDetails>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred while confirming the LINK.");
            }
        }

        /// <summary>
        /// Rejects an emailed LINK invitation. No login is required.
        /// </summary>
        public async Task<ResponseWrapper<string>> RejectEmailLink(RejectEmailLinkRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<string>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            try
            {
                return await _database.RejectLinkAsync(request.VerificationCode);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RejectEmailLink.");
                return ResponseWrapper<string>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred while rejecting the LINK.");
            }
        }

        /// <summary>
        /// Returns all confirmed LINK connections for the current user.
        /// </summary>
        public async Task<ResponseWrapper<List<LinkedUser>>> GetLinkedUsers(GetLinkedUsersRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<LinkedUser>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<LinkedUser>>.Fail(notVerified.ErrorCode, notVerified.Message);

            return await _database.GetLinkedUsersAsync(request.LoginToken);
        }

        /// <summary>
        /// Uploads a photo of a linked user. AI validates face presence.
        /// Photo is inserted with subject_confirmed=0 — the subject must confirm before karma is applied.
        /// </summary>
        public async Task<ResponseWrapper<object>> UploadLinkPhoto(UploadLinkPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            byte[] imageData = request.ImageData ?? Array.Empty<byte>();

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                if (!currentUser.IsSuccess) return ResponseWrapper<object>.Fail(currentUser.ErrorCode, currentUser.Message);

                // AI face detection
                ResponseWrapper<bool> faceResult = await _companioNita.DetectFaceAsync(imageData);
                if (!faceResult.IsSuccess)
                    return ResponseWrapper<object>.Fail(faceResult.ErrorCode, faceResult.Message);
                if (!faceResult.Data)
                    return ResponseWrapper<object>.Fail(ErrorCodes.LinkFaceNotDetected, "No face detected in the photo.");

                ResponseWrapper<(Guid ImageGuid, int SubjectUserId, string SubjectEmail, string SubjectName)> result =
                    await _database.UploadLinkPhotoAsync(loginToken, request.ConnectionId, imageData);
                if (!result.IsSuccess)
                    return ResponseWrapper<object>.Fail(result.ErrorCode, result.Message);

                // Notify the subject that a photo of them is pending confirmation
                await SendLinkPhotoPendingEmailAsync(result.Data.SubjectEmail, result.Data.SubjectName, currentUser.Data.Name);

                return ResponseWrapper<object>.Success(null);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in UploadLinkPhoto.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred while uploading the LINK photo.");
            }
        }

        /// <summary>
        /// Deletes any photo belonging to the authenticated user (self-uploaded or LINK photo
        /// where they are the subject). Removes blob and reverses LINK karma when applicable.
        /// </summary>
        public async Task<ResponseWrapper<object>> DeleteUserPhoto(DeleteUserPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            try
            {
                ResponseWrapper<Guid> result = await _database.DeleteUserPhotoAsync(request.LoginToken, request.ImageId);
                if (!result.IsSuccess)
                    return ResponseWrapper<object>.Fail(result.ErrorCode, result.Message);
                return ResponseWrapper<object>.Success(null);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in DeleteUserPhoto.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Sets the visibility of a LINK photo. Only the subject can toggle visibility.
        /// </summary>
        public async Task<ResponseWrapper<object>> SetLinkPhotoVisibility(SetLinkPhotoVisibilityRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<bool> result = await _database.SetLinkPhotoVisibilityAsync(request.LoginToken, request.ImageId, request.Visible);
                if (!result.IsSuccess)
                    return ResponseWrapper<object>.Fail(result.ErrorCode, result.Message);
                return ResponseWrapper<object>.Success(null);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in SetLinkPhotoVisibility.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Confirms a LINK photo (subject confirms "yes, that's me"). Applies +2 karma to both users.
        /// </summary>
        public async Task<ResponseWrapper<object>> ConfirmLinkPhoto(ConfirmLinkPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<bool> result = await _database.ConfirmLinkPhotoAsync(request.LoginToken, request.ImageId);
                if (!result.IsSuccess)
                    return ResponseWrapper<object>.Fail(result.ErrorCode, result.Message);
                return ResponseWrapper<object>.Success(null);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in ConfirmLinkPhoto.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Rejects a LINK photo (subject says "that's not me"). Deducts 1 karma from uploader
        /// and deletes the photo. Logged to ErrorLog for admin visibility.
        /// </summary>
        public async Task<ResponseWrapper<object>> RejectLinkPhoto(RejectLinkPhotoRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<object>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            string loginToken = request.LoginToken ?? string.Empty;
            int imageId = request.ImageId;

            var notVerified = await CheckVerifiedAsync(loginToken);
            if (notVerified != null)
                return ResponseWrapper<object>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<Guid> result = await _database.RejectLinkPhotoAsync(loginToken, imageId);
                if (!result.IsSuccess)
                    return ResponseWrapper<object>.Fail(result.ErrorCode, result.Message);

                // Log the rejection for admin visibility (best-effort; logging failure
                // must not mask the successful rejection from the caller)
                try
                {
                    ResponseWrapper<UserDetails> currentUser = await _database.GetUserAsync(loginToken);
                    string userName = currentUser.IsSuccess ? currentUser.Data.Name : "Unknown";
                    ErrorLog.LogErrorMessage($"LINK photo rejected by subject. User '{userName}' rejected image_id={imageId} (blob {result.Data} deleted).");
                }
                catch (Exception logEx)
                {
                    ErrorLog.LogErrorException(logEx, $"Failed to log LINK photo rejection (image_id={imageId}, blob={result.Data}).");
                }

                return ResponseWrapper<object>.Success(null);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RejectLinkPhoto.");
                return ResponseWrapper<object>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Admin-only: recalculates karma for all users and sends notification on desync.
        /// </summary>
        public async Task<ResponseWrapper<List<KarmaDesync>>> RecalculateKarma(RecalculateKarmaRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<List<KarmaDesync>>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            var notVerified = await CheckVerifiedAsync(request.LoginToken);
            if (notVerified != null)
                return ResponseWrapper<List<KarmaDesync>>.Fail(notVerified.ErrorCode, notVerified.Message);

            try
            {
                ResponseWrapper<List<KarmaDesync>> result = await _database.RecalculateKarmaAsync(request.LoginToken);

                if (result.IsSuccess && result.Data.Count > 0)
                {
                    ErrorLog.LogErrorMessage($"Karma desync detected for {result.Data.Count} users during recalculation.");
                    await SendKarmaDesyncNotificationAsync(result.Data);
                }

                return result;
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in RecalculateKarma.");
                return ResponseWrapper<List<KarmaDesync>>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Admin-only: migrates legacy guarantor_user_id data to connection_id. Idempotent.
        /// </summary>
        public async Task<ResponseWrapper<GuarantorMigrationResult>> MigrateGuarantorData(MigrateGuarantorDataRequest request)
        {
            if (RequiresUpgrade(request))
                return ResponseWrapper<GuarantorMigrationResult>.Fail(ErrorCodes.ClientUpgradeRequired, ClientUpgradeRequiredMessage);

            try
            {
                return await _database.MigrateGuarantorToLinkAsync(request.LoginToken);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error in MigrateGuarantorData.");
                return ResponseWrapper<GuarantorMigrationResult>.Fail(ErrorCodes.UnknownError, "An unexpected error occurred.");
            }
        }

        private async Task SendKarmaDesyncNotificationAsync(List<KarmaDesync> desyncs)
        {
            try
            {
                string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.KarmaDesyncNotification.txt");
                string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.KarmaDesyncNotification.html");

                if (textTemplate == null || htmlTemplate == null)
                {
                    ErrorLog.LogErrorMessage("KarmaDesyncNotification email template not found.");
                    return;
                }

                string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string count = desyncs.Count.ToString();

                // Build HTML table rows
                var htmlRows = new StringBuilder();
                foreach (var d in desyncs)
                {
                    htmlRows.Append("<tr>")
                        .Append($"<td style=\"padding:8px;border:1px solid #ddd;\">{d.UserId}</td>")
                        .Append($"<td style=\"padding:8px;border:1px solid #ddd;\">{d.Name}</td>")
                        .Append($"<td style=\"padding:8px;border:1px solid #ddd;text-align:right;\">{d.StoredRanking}</td>")
                        .Append($"<td style=\"padding:8px;border:1px solid #ddd;text-align:right;\">{d.CalculatedRanking}</td>")
                        .Append($"<td style=\"padding:8px;border:1px solid #ddd;text-align:right;\">{d.Delta:+#;-#;0}</td>")
                        .AppendLine("</tr>");
                }

                // Build plain text rows
                var textRows = new StringBuilder();
                foreach (var d in desyncs)
                {
                    textRows.AppendLine($"  ID: {d.UserId} | {d.Name} | Stored: {d.StoredRanking} | Calculated: {d.CalculatedRanking} | Delta: {d.Delta:+#;-#;0}");
                }

                htmlTemplate = htmlTemplate.Replace("{Count}", count);
                htmlTemplate = htmlTemplate.Replace("{UserRows}", htmlRows.ToString());
                htmlTemplate = htmlTemplate.Replace("{Timestamp}", timestamp);

                textTemplate = textTemplate.Replace("{Count}", count);
                textTemplate = textTemplate.Replace("{UserRows}", textRows.ToString());
                textTemplate = textTemplate.Replace("{Timestamp}", timestamp);

                await Email.SendEmailAsync("errors@companionation.com", $"⚠️ Karma Desync: {count} user(s) corrected", textTemplate, htmlTemplate);
            }
            catch (Exception ex)
            {
                ErrorLog.LogErrorException(ex, "Error sending karma desync notification email.");
            }
        }

        private async Task SendLinkInviteEmailAsync(string email, string verificationCode, string senderName)
        {
            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.LinkInvite.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.LinkInvite.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("LinkInvite email template not found.");
                return;
            }

            textTemplate = textTemplate.Replace("{Name}", senderName);
            textTemplate = textTemplate.Replace("{VerificationCode}", verificationCode);

            htmlTemplate = htmlTemplate.Replace("{Name}", senderName);
            htmlTemplate = htmlTemplate.Replace("{VerificationCode}", verificationCode);

            await Email.SendEmailAsync(email, $"{senderName} wants to LINK with you on CompanioNation™", textTemplate, htmlTemplate);
        }

        private async Task SendLinkPhotoPendingEmailAsync(string email, string subjectName, string uploaderName)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                ErrorLog.LogErrorMessage($"LinkPhotoPending email skipped: no recipient address (subjectName={subjectName}, uploaderName={uploaderName}).");
                return;
            }

            string textTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.LinkPhotoPending.txt");
            string htmlTemplate = LoadEmailTemplate("CompanioNationAPI.EmailTemplates.LinkPhotoPending.html");

            if (textTemplate == null || htmlTemplate == null)
            {
                ErrorLog.LogErrorMessage("LinkPhotoPending email template not found.");
                return;
            }

            string safeSubjectName = string.IsNullOrWhiteSpace(subjectName) ? "there" : subjectName;
            string safeUploaderName = string.IsNullOrWhiteSpace(uploaderName) ? "someone" : uploaderName;

            textTemplate = textTemplate.Replace("{SubjectName}", safeSubjectName);
            textTemplate = textTemplate.Replace("{UploaderName}", safeUploaderName);

            htmlTemplate = htmlTemplate.Replace("{SubjectName}", WebUtility.HtmlEncode(safeSubjectName));
            htmlTemplate = htmlTemplate.Replace("{UploaderName}", WebUtility.HtmlEncode(safeUploaderName));

            bool sent = await Email.SendEmailAsync(email, $"{safeUploaderName} uploaded a photo of you on CompanioNation™", textTemplate, htmlTemplate);
            if (!sent)
                ErrorLog.LogErrorMessage($"LinkPhotoPending email failed to send to {email} (subjectName={subjectName}, uploaderName={uploaderName}).");
        }
    }
}
